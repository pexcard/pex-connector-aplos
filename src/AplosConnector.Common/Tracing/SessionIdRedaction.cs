using System.Diagnostics;
using System.Text.RegularExpressions;
using AplosConnector.Common.Storage;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Instrumentation.Http;

namespace AplosConnector.Common.Tracing;

// The session id is a bearer credential. Outbound URLs carry it as the session-table row key and in the PEX OAuth return path.
public static class SessionIdRedaction
{
    public const string Placeholder = "{sessionId}";

    private static readonly Regex SessionRowKey = new(
        "(" + Regex.Escape(PexOAuthSessionStorage.TABLE_NAME) + @"\(PartitionKey=(?:'|%27)[^'%]*(?:'|%27),RowKey=(?:'|%27))[^'%]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FinishPexLoginSegment = new(@"(/finish-pex-login/)[^/?#]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string RedactUrl(string url) =>
        SessionRowKey.Replace(FinishPexLoginSegment.Replace(url, "$1" + Placeholder), "$1" + Placeholder);

    // Register after ConfigurePexTelemetry so the package's own HttpClient enricher still runs first.
    public static IServiceCollection AddSessionIdRedaction(this IServiceCollection services)
    {
        services.Configure<HttpClientTraceInstrumentationOptions>(options =>
        {
            var enrich = options.EnrichWithHttpRequestMessage;
            options.EnrichWithHttpRequestMessage = (activity, request) =>
            {
                enrich?.Invoke(activity, request);
                RedactUrlTag(activity);
            };
        });
        return services;
    }

    // Rewrites url.full on an outbound HTTP span when it carries a session id.
    public static void RedactUrlTag(Activity activity)
    {
        if (activity.GetTagItem("url.full") is string url)
        {
            var redacted = RedactUrl(url);
            if (redacted != url)
            {
                activity.SetTag("url.full", redacted);
            }
        }
    }
}
