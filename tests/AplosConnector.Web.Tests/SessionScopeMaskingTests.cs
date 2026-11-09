using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Web.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using OpenTelemetry.Logs;
using Xunit;

namespace AplosConnector.Web.Tests
{
    /// <summary>
    /// Covers the masking of the PEX OAuth session id in logger scopes, which ASP.NET Core's hosting scope fills with
    /// the raw request path before any middleware runs.
    /// </summary>
    public class SessionScopeMaskingTests
    {
        /// <summary>
        /// A request to the PEX OAuth return URL exports its hosting scope with the session id masked, under every
        /// committed appsettings overlay.
        /// </summary>
        /// <param name="environment">
        /// The appsettings environment overlay to load, or null for the base file only.
        /// </param>
        /// <returns>
        /// A task that completes when the request has been served and checked.
        /// </returns>
        [Theory]
        [InlineData(null)]
        [InlineData("QA")]
        [InlineData("Production")]
        public async Task FinishPexLoginRequest_MasksSessionIdInRequestPathScope(string environment)
        {
            var sessionId = Guid.NewGuid().ToString();

            var processor = await ServeRequest(environment, $"/finish-pex-login/{sessionId}");

            Assert.True(processor.RecordCount > 0);
            Assert.Contains($"RequestPath={SessionPathRedaction.RedactedFinishPexLoginPath}", processor.ScopeValues);
            Assert.DoesNotContain(processor.ScopeValues, value => value.Contains(sessionId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A request to any other path keeps its real request path in the hosting scope.
        /// </summary>
        /// <returns>
        /// A task that completes when the request has been served and checked.
        /// </returns>
        [Fact]
        public async Task OtherRequest_KeepsRequestPathScope()
        {
            var processor = await ServeRequest("Production", "/api/Session/Validity");

            Assert.Contains("RequestPath=/api/Session/Validity", processor.ScopeValues);
        }

        /// <summary>
        /// Values without a session id come back as the same scope instance, so ordinary records are not copied.
        /// </summary>
        [Fact]
        public void MaskScope_LeavesScopeWithoutSessionIdUntouched()
        {
            var scope = new Dictionary<string, object> { ["RequestPath"] = "/api/Session/Validity", ["RequestId"] = "0HN:1" };

            Assert.Same(scope, SessionIdMaskingScopeProvider.MaskScope(scope));
        }

        /// <summary>
        /// Every string value carrying a session id is masked, other values and keys are kept in order, and the
        /// masked scope renders without the id.
        /// </summary>
        [Fact]
        public void MaskScope_MasksSessionIdValues()
        {
            var scope = new List<KeyValuePair<string, object>>
            {
                new("RequestId", "0HN:1"),
                new("RequestPath", "/finish-pex-login/abc-123"),
                new("Attempt", 2),
            };

            var masked = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object>>>(SessionIdMaskingScopeProvider.MaskScope(scope));

            Assert.Equal(
                new[]
                {
                    new KeyValuePair<string, object>("RequestId", "0HN:1"),
                    new KeyValuePair<string, object>("RequestPath", SessionPathRedaction.RedactedFinishPexLoginPath),
                    new KeyValuePair<string, object>("Attempt", 2),
                },
                masked);
            Assert.Equal("RequestId:0HN:1 RequestPath:/finish-pex-login/{sessionId} Attempt:2", masked.ToString());
        }

        /// <summary>
        /// A scope whose own text writes the session path differently from its values never renders the session id;
        /// it falls back to its masked pairs.
        /// </summary>
        [Fact]
        public void MaskScope_ScopeTextDiffersFromValues_NeverRendersSessionId()
        {
            var pairs = new List<KeyValuePair<string, object>> { new("RequestPath", "/finish-pex-login/abc-123") };
            var scope = new Mock<IEnumerable<KeyValuePair<string, object>>>();
            scope.Setup(s => s.GetEnumerator()).Returns(() => pairs.GetEnumerator());
            scope.Setup(s => s.ToString()).Returns("GET /FINISH-PEX-LOGIN/abc-123");

            var rendered = SessionIdMaskingScopeProvider.MaskScope(scope.Object).ToString();

            Assert.Equal("RequestPath:/finish-pex-login/{sessionId}", rendered);
        }

        /// <summary>
        /// A request path held as a <see cref="PathString"/> is masked like a string path.
        /// </summary>
        [Fact]
        public void MaskScope_MasksPathStringValue()
        {
            var scope = new Dictionary<string, object> { ["Path"] = new PathString("/finish-pex-login/abc-123") };

            var masked = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object>>>(SessionIdMaskingScopeProvider.MaskScope(scope));

            Assert.Equal(new KeyValuePair<string, object>("Path", SessionPathRedaction.RedactedFinishPexLoginPath), Assert.Single(masked));
            Assert.Equal("Path:/finish-pex-login/{sessionId}", masked.ToString());
        }

        /// <summary>
        /// A scope opened with a message template still renders its message, with the session id masked, while its
        /// key/value pairs carry the masked value and the untouched template.
        /// </summary>
        [Fact]
        public void MaskScope_FormattedScope_KeepsRenderedMessage()
        {
            object scope = null;
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.BeginScope(It.IsAny<It.IsAnyType>())).Callback(new InvocationAction(invocation => scope = invocation.Arguments[0]));
            logger.Object.BeginScope("GET {RequestPath} for {Caller}", "/finish-pex-login/abc-123", "browser");

            var masked = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object>>>(SessionIdMaskingScopeProvider.MaskScope(scope));

            Assert.Equal("GET /finish-pex-login/{sessionId} for browser", masked.ToString());
            Assert.Contains(new KeyValuePair<string, object>("RequestPath", SessionPathRedaction.RedactedFinishPexLoginPath), masked);
            Assert.Contains(new KeyValuePair<string, object>("{OriginalFormat}", "GET {RequestPath} for {Caller}"), masked);
        }

        /// <summary>
        /// A plain string scope carrying a session id is masked too.
        /// </summary>
        [Fact]
        public void MaskScope_MasksStringScope()
        {
            Assert.Equal("GET /finish-pex-login/{sessionId}", SessionIdMaskingScopeProvider.MaskScope("GET /finish-pex-login/abc-123"));
        }

        #region private methods

        /// <summary>
        /// Hosts a minimal app with the committed log levels, the real OpenTelemetry logger provider and
        /// <see cref="SessionPathRedactionServiceCollectionExtensions.AddSessionPathRedaction"/>, serves one request
        /// that writes a warning, and returns what the OpenTelemetry pipeline saw.
        /// </summary>
        /// <param name="environment">
        /// The appsettings environment overlay to load, or null for the base file only.
        /// </param>
        /// <param name="path">
        /// The request path to serve.
        /// </param>
        /// <returns>
        /// The processor holding the scope values of every exported record.
        /// </returns>
        private static async Task<ScopeCapturingLogProcessor> ServeRequest(string environment, string path)
        {
            var configuration = TelemetryConfigTests.LoadAppSettings(environment);
            var processor = new ScopeCapturingLogProcessor();
            ILogger earlyLogger = null;
            using var host = new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddConfiguration(configuration.GetSection("Logging"));
                    })
                    .ConfigureServices(services =>
                    {
                        services.AddOpenTelemetry().WithLogging(
                            logging => logging.AddProcessor(processor),
                            options => options.IncludeScopes = true);
                        services.AddSessionPathRedaction();
                    })
                    .Configure(app => app.Run(context =>
                    {
                        context.RequestServices.GetRequiredService<ILogger<SessionScopeMaskingTests>>().LogWarning("Request handled");
                        earlyLogger.LogWarning("Request handled by a logger created before startup");

                        return Task.CompletedTask;
                    })))
                .Build();
            // Created before the masking is installed at startup, like loggers of singletons built with the host.
            earlyLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EarlyLogger");
            // xUnit 2 has no test-run token; the timeout fails a stuck request instead of hanging the run.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await host.StartAsync(timeout.Token);

            await host.GetTestClient().GetAsync(path, timeout.Token);
            await host.StopAsync(timeout.Token);

            return processor;
        }

        #endregion
    }
}
