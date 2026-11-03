using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AplosConnector.Web.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using OpenTelemetry.Instrumentation.AspNetCore;
using PexCard.Contracts.Interfaces;
using PexCard.Shared.OpenTelemetry.Abstractions.Options;
using PexCard.Shared.OpenTelemetry.Extensions;
using Xunit;

namespace AplosConnector.Web.Tests
{
    public class TelemetryConfigTests
    {
        [Theory]
        [InlineData("QA", "sandbox", "/sandbox/marketplace/pex-connector-aplos", "app-service", true)]
        [InlineData("Production", "production", "/prod/marketplace/pex-connector-aplos", "app-service", true)]
        [InlineData(null, "sandbox", "/sandbox/marketplace/pex-connector-aplos", "local", false)]
        public void AppSettings_BindTelemetryConfig(string environment, string deploymentEnvironment, string sourceCategory, string hostType, bool exporterEnabled)
        {
            var configuration = LoadAppSettings(environment);
            var options = configuration.GetSection("TelemetryConfig").Get<PexTelemetryOptions>();

            // The vault address is an app setting (KeyVault__Uri) per app; it must never be committed to this public repo.
            Assert.Null(configuration["KeyVault:Uri"]);
            Assert.Equal(exporterEnabled, options.Exporter.Enabled);
            Assert.Equal("pex-connector-aplos", options.Resource.ApplicationName);
            Assert.Equal("pex-connector-aplos-api", options.Resource.ServiceName);
            Assert.Equal(deploymentEnvironment, options.Resource.DeploymentEnvironment);
            Assert.Equal(hostType, options.Resource.DeploymentHostType);
            Assert.Equal(sourceCategory, options.Resource.AdditionalAttributes["_sourceCategory"]?.ToString());
            Assert.Contains("/api/Health/Ping", options.Tracing.AspNetCore.ExcludedPaths);
            Assert.Equal("REPLACE", options.Exporter.BaseUrl);
            Assert.True(options.Logging.Enabled);
        }

        [Fact]
        public void ConfigurePexTelemetry_WithSessionPathRedaction_KeepsPackageEnricher()
        {
            var configuration = new ConfigurationBuilder()
                .AddConfiguration(LoadAppSettings("QA"))
                .AddInMemoryCollection(new Dictionary<string, string> { ["TelemetryConfig:Exporter:BaseUrl"] = "http://localhost:4318" })
                .Build();
            var correlationId = new Mock<ICorrelationId>();
            correlationId.SetupGet(c => c.CorrelationId).Returns("ext-aplos-test");

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(correlationId.Object);
            services.ConfigurePexTelemetry(configuration);
            services.AddSessionPathRedaction();
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value;

            var context = new DefaultHttpContext { RequestServices = provider };
            context.Request.Path = "/finish-pex-login/abc";
            using var activity = new Activity("request");
            activity.SetTag("url.path", "/finish-pex-login/abc");

            options.EnrichWithHttpRequest(activity, context.Request);

            Assert.Equal(SessionPathRedaction.RedactedFinishPexLoginPath, activity.GetTagItem("url.path"));
            Assert.Equal("ext-aplos-test", activity.GetTagItem("pex.correlation_id"));
        }

        private static IConfiguration LoadAppSettings(string environment)
        {
            var webRoot = FindWebProjectRoot();
            var builder = new ConfigurationBuilder().AddJsonFile(Path.Combine(webRoot, "appsettings.json"), optional: false);
            if (environment != null)
            {
                builder.AddJsonFile(Path.Combine(webRoot, $"appsettings.{environment}.json"), optional: false);
            }
            return builder.Build();
        }

        private static string FindWebProjectRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "AplosConnector.Web");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("src/AplosConnector.Web not found above the test output directory.");
        }
    }
}
