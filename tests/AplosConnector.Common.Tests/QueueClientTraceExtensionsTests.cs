using AplosConnector.Common.Models;
using AplosConnector.Common.Tracing;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class QueueClientTraceExtensionsTests
{
    private readonly Mock<QueueClient> _queue = new();
    private string _sentMessage;

    public QueueClientTraceExtensionsTests()
    {
        _queue
            .Setup(q => q.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((message, _) => _sentMessage = message)
            .ReturnsAsync((Response<SendReceipt>)null);
    }

    [Fact]
    public async Task SendMessageWithTraceContextAsync_AddsTraceContextToTheMessage_WhenAnActivityIsRunning()
    {
        using var activity = new Activity("enqueue").SetIdFormat(ActivityIdFormat.W3C);
        activity.TraceStateString = "pex=1";
        activity.Start();

        await _queue.Object.SendMessageWithTraceContextAsync(new Pex2AplosMappingModel { PEXBusinessAcctId = 12345 });

        var envelope = DecodeSentMessage();
        Assert.Equal(12345, envelope.Value<int>(nameof(Pex2AplosMappingModel.PEXBusinessAcctId)));
        Assert.Equal(activity.Id, envelope.Value<string>("traceparent"));
        Assert.Equal("pex=1", envelope.Value<string>("tracestate"));
    }

    [Fact]
    public async Task SendMessageWithTraceContextAsync_SendsThePayloadAlone_WhenNoActivityIsRunning()
    {
        Assert.Null(Activity.Current);

        await _queue.Object.SendMessageWithTraceContextAsync(new Pex2AplosMappingModel { PEXBusinessAcctId = 12345 });

        var envelope = DecodeSentMessage();
        Assert.Equal(12345, envelope.Value<int>(nameof(Pex2AplosMappingModel.PEXBusinessAcctId)));
        Assert.Null(envelope["traceparent"]);
        Assert.Null(envelope["tracestate"]);
    }

    // The Functions queue trigger expects base64 by default.
    private JObject DecodeSentMessage()
    {
        Assert.NotNull(_sentMessage);
        return JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(_sentMessage)));
    }
}
