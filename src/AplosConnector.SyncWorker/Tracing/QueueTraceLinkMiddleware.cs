using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Newtonsoft.Json.Linq;

namespace AplosConnector.SyncWorker.Tracing;

// Links a queue invocation's span to the producer's trace, read from the traceparent that QueueClientTraceExtensions put in the message.
public sealed class QueueTraceLinkMiddleware : IFunctionsWorkerMiddleware
{
    public Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        TryLinkProducerTrace(context);
        return next(context);
    }

    private static void TryLinkProducerTrace(FunctionContext context)
    {
        try
        {
            var activity = Activity.Current;
            if (activity is null) return;

            if (context.BindingContext?.BindingData is not { } bindingData) return;
            if (!bindingData.TryGetValue("QueueTrigger", out var raw)) return;
            if (raw is not string body || string.IsNullOrWhiteSpace(body)) return;

            var envelope = JObject.Parse(body);
            if (envelope["traceparent"]?.Value<string>() is not { } traceparent) return;

            var tracestate = envelope["tracestate"]?.Value<string>();
            if (!ActivityContext.TryParse(traceparent, tracestate, isRemote: true, out var producerContext)) return;

            // A link, not a parent: a message can sit in the queue for hours.
            activity.AddLink(new ActivityLink(producerContext));
        }
        catch (Exception)
        {
            // Propagation never fails the invocation.
        }
    }
}
