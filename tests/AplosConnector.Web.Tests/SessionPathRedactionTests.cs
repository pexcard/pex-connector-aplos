using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AplosConnector.Web.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;
using Xunit;

namespace AplosConnector.Web.Tests
{
    public class SessionPathRedactionTests
    {
        [Theory]
        [InlineData("/finish-pex-login/3f2b8c1e-0000-4000-8000-000000000000")]
        [InlineData("/FINISH-PEX-LOGIN/abc")]
        [InlineData("/finish-pex-login/abc/extra")]
        public void TryRedact_RedactsSessionPath(string path)
        {
            var redacted = SessionPathRedaction.TryRedact(new PathString(path), out var value);

            Assert.True(redacted);
            Assert.Equal(SessionPathRedaction.RedactedFinishPexLoginPath, value);
        }

        [Theory]
        [InlineData("/finish-pex-login")]
        [InlineData("/finish-pex-login-other/abc")]
        [InlineData("/api/Session/Validity")]
        [InlineData("/")]
        public void TryRedact_LeavesOtherPathsAlone(string path)
        {
            Assert.False(SessionPathRedaction.TryRedact(new PathString(path), out _));
        }

        [Fact]
        public void AddSessionPathRedaction_RedactsSpanPath_AndKeepsEarlierEnricher()
        {
            var services = new ServiceCollection();
            services.Configure<AspNetCoreTraceInstrumentationOptions>(o => o.EnrichWithHttpRequest = (a, _) => a.SetTag("earlier", "ran"));
            services.AddSessionPathRedaction();
            var options = services.BuildServiceProvider().GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value;

            var context = new DefaultHttpContext();
            context.Request.Path = "/finish-pex-login/abc";
            using var activity = new Activity("request");
            activity.SetTag("url.path", "/finish-pex-login/abc");

            options.EnrichWithHttpRequest(activity, context.Request);

            Assert.Equal(SessionPathRedaction.RedactedFinishPexLoginPath, activity.GetTagItem("url.path"));
            Assert.Equal("ran", activity.GetTagItem("earlier"));
        }

        [Fact]
        public async Task Interceptor_ReplacesLoggedPath()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/finish-pex-login/abc";
            var logContext = new HttpLoggingInterceptorContext
            {
                HttpContext = context,
                LoggingFields = HttpLoggingFields.RequestPath | HttpLoggingFields.RequestMethod
            };

            await new SessionPathHttpLoggingInterceptor().OnRequestAsync(logContext);

            Assert.False(logContext.IsAnyEnabled(HttpLoggingFields.RequestPath));
            Assert.Contains(logContext.Parameters, p => p.Key == nameof(HttpRequest.Path) && (string)p.Value == SessionPathRedaction.RedactedFinishPexLoginPath);
        }

        [Fact]
        public async Task Interceptor_IgnoresOtherPaths()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/Mapping/Settings";
            var logContext = new HttpLoggingInterceptorContext
            {
                HttpContext = context,
                LoggingFields = HttpLoggingFields.RequestPath
            };

            await new SessionPathHttpLoggingInterceptor().OnRequestAsync(logContext);

            Assert.True(logContext.IsAnyEnabled(HttpLoggingFields.RequestPath));
            Assert.Empty(logContext.Parameters);
        }
    }
}
