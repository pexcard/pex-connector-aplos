using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AplosConnector.Web.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            Assert.DoesNotContain("abc-123", masked.ToString());
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
            await host.StartAsync();

            await host.GetTestClient().GetAsync(path);
            await host.StopAsync();

            return processor;
        }

        #endregion
    }
}
