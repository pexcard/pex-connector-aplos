#nullable enable
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using AplosConnector.Common.Tracing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Instrumentation.AspNetCore;

namespace AplosConnector.Web.Telemetry;

// The PEX OAuth return URL carries the session id, a bearer credential, in its path; keep it out of request logs and spans.
public static class SessionPathRedaction
{
    public const string RedactedFinishPexLoginPath = "/finish-pex-login/{sessionId}";

    private static readonly PathString FinishPexLogin = new("/finish-pex-login");

    public static bool TryRedact(PathString path, out string? redacted)
    {
        if (path.StartsWithSegments(FinishPexLogin, StringComparison.OrdinalIgnoreCase, out var remaining) && remaining.HasValue)
        {
            redacted = RedactedFinishPexLoginPath;
            return true;
        }

        redacted = null;
        return false;
    }

    // Overwrites url.path on the ASP.NET Core request span.
    public static void RedactSpan(Activity activity, HttpRequest request)
    {
        if (TryRedact(request.Path, out var redacted))
        {
            activity.SetTag("url.path", redacted);
        }
    }
}

public static class SessionPathRedactionServiceCollectionExtensions
{
    // Register after ConfigurePexTelemetry so the package's own request enricher still runs first.
    public static IServiceCollection AddSessionPathRedaction(this IServiceCollection services)
    {
        services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            var enrich = options.EnrichWithHttpRequest;
            options.EnrichWithHttpRequest = (activity, request) =>
            {
                enrich?.Invoke(activity, request);
                SessionPathRedaction.RedactSpan(activity, request);
            };
        });
        services.AddHttpLoggingInterceptor<SessionPathHttpLoggingInterceptor>();
        services.AddSessionIdRedaction();
        return services;
    }
}

// Swaps the session path for its redacted form in the AddPexHttpLogging request record.
public sealed class SessionPathHttpLoggingInterceptor : IHttpLoggingInterceptor
{
    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext)
    {
        if (logContext.IsAnyEnabled(HttpLoggingFields.RequestPath)
            && SessionPathRedaction.TryRedact(logContext.HttpContext.Request.Path, out var redacted))
        {
            logContext.Disable(HttpLoggingFields.RequestPath);
            logContext.AddParameter(nameof(HttpRequest.Path), redacted);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext) => ValueTask.CompletedTask;
}
