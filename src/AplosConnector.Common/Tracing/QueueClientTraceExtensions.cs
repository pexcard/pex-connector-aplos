using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AplosConnector.Common.Tracing;

// Storage Queue messages have no headers, so W3C trace context rides as top-level JSON keys; the SyncWorker's QueueTraceLinkMiddleware reads them back.
public static class QueueClientTraceExtensions
{
    public static Task<Response<SendReceipt>> SendMessageWithTraceContextAsync<T>(
        this QueueClient queue,
        T payload,
        CancellationToken cancellationToken = default)
    {
        var json = JsonConvert.SerializeObject(payload);
        var activity = Activity.Current;
        if (activity is not null && activity.IdFormat == ActivityIdFormat.W3C)
        {
            try
            {
                var envelope = JObject.Parse(json);
                envelope["traceparent"] = activity.Id;
                if (!string.IsNullOrEmpty(activity.TraceStateString))
                {
                    envelope["tracestate"] = activity.TraceStateString;
                }
                json = envelope.ToString(Formatting.None);
            }
            catch (JsonReaderException)
            {
                // Non-object payloads go out without trace context; propagation never fails the enqueue.
            }
        }

        // Base64, matching the Functions queue trigger's default message encoding.
        var message = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return queue.SendMessageAsync(message, cancellationToken);
    }
}
