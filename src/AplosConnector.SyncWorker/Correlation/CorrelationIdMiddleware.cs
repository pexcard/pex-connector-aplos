using System.Diagnostics;
using System.Threading.Tasks;
using AplosConnector.Common.Correlation;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using PexCard.Shared.OpenTelemetry.Abstractions.Constants;

namespace AplosConnector.SyncWorker.Correlation;

// Mints a correlation id per invocation for outbound PEX calls, the invocation span, and (via baggage) every log record.
public sealed class CorrelationIdMiddleware : IFunctionsWorkerMiddleware
{
    private readonly CorrelationIdAccessor _accessor;

    public CorrelationIdMiddleware(CorrelationIdAccessor accessor) => _accessor = accessor;

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var id = _accessor.Generate();
        var prior = _accessor.Current;
        _accessor.Current = id;
        Activity.Current?.SetTag(DiagnosticNames.CorrelationId, id);
        // Baggage reaches log records through ActivityTrackingOptions.Baggage and flows to child spans.
        Activity.Current?.SetBaggage(DiagnosticNames.CorrelationId, id);
        try
        {
            await next(context);
        }
        finally
        {
            _accessor.Current = prior;
        }
    }
}
