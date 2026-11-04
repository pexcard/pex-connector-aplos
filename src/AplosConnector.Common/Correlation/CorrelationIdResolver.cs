using PexCard.Api.Client.Core.Interfaces;

namespace AplosConnector.Common.Correlation;

// PexApiClient reads this to send X-Correlation-Id on every outbound PEX call.
public sealed class CorrelationIdResolver : ICorrelationIdResolver
{
    private readonly CorrelationIdAccessor _accessor;

    public CorrelationIdResolver(CorrelationIdAccessor accessor) => _accessor = accessor;

    public string CorrelationId => _accessor.GetOrCreate();

    public string GetValue() => CorrelationId;
}
