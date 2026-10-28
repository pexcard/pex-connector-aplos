using System.Diagnostics;
using System.Net.Http;
using AplosConnector.Common.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.Http;
using Xunit;

namespace AplosConnector.Common.Tests;

public class SessionIdRedactionTests
{
    private const string SessionId = "3f2b8c1e-0000-4000-8000-000000000000";

    [Theory]
    [InlineData("https://acct.table.core.windows.net/PexOAuthSession(PartitionKey='PexOauth',RowKey='" + SessionId + "')?*",
                "https://acct.table.core.windows.net/PexOAuthSession(PartitionKey='PexOauth',RowKey='{sessionId}')?*")]
    [InlineData("https://acct.table.core.windows.net/PexOAuthSession(PartitionKey=%27PexOauth%27,RowKey=%27" + SessionId + "%27)",
                "https://acct.table.core.windows.net/PexOAuthSession(PartitionKey=%27PexOauth%27,RowKey=%27{sessionId}%27)")]
    [InlineData("http://localhost:4200/finish-pex-login/" + SessionId,
                "http://localhost:4200/finish-pex-login/{sessionId}")]
    public void RedactUrl_MasksSessionIds(string url, string expected)
    {
        Assert.Equal(expected, SessionIdRedaction.RedactUrl(url));
    }

    [Theory]
    [InlineData("https://acct.table.core.windows.net/Pex2AplosMapping(PartitionKey='Pex2Aplos',RowKey='12345')")]
    [InlineData("https://coreapi.pexcard.com/v4/Business/Profile")]
    public void RedactUrl_LeavesOtherUrlsAlone(string url)
    {
        Assert.Equal(url, SessionIdRedaction.RedactUrl(url));
    }

    [Fact]
    public void AddSessionIdRedaction_RedactsClientSpanUrl_AndKeepsEarlierEnricher()
    {
        var services = new ServiceCollection();
        services.Configure<HttpClientTraceInstrumentationOptions>(o => o.EnrichWithHttpRequestMessage = (a, _) => a.SetTag("earlier", "ran"));
        services.AddSessionIdRedaction();
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<HttpClientTraceInstrumentationOptions>>().Value;

        using var activity = new Activity("GET");
        activity.SetTag("url.full", "https://acct.table.core.windows.net/PexOAuthSession(PartitionKey='PexOauth',RowKey='" + SessionId + "')");

        options.EnrichWithHttpRequestMessage(activity, new HttpRequestMessage());

        Assert.DoesNotContain(SessionId, (string)activity.GetTagItem("url.full"));
        Assert.Equal("ran", activity.GetTagItem("earlier"));
    }
}
