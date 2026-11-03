#nullable enable
using System;
using System.Threading;

namespace AplosConnector.Common.Correlation;

// Holds the current request's or invocation's correlation id in an AsyncLocal; middleware sets it, outbound PEX calls read it.
public sealed class CorrelationIdAccessor
{
    private readonly string _prefix;
    private readonly AsyncLocal<string?> _current = new();

    public CorrelationIdAccessor(string prefix) => _prefix = prefix;

    public string? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    public string Generate() => $"{_prefix}-{Guid.NewGuid():N}";

    // Caches a generated id for the rest of the async flow so calls made outside the middleware still share one id.
    public string GetOrCreate()
    {
        var current = _current.Value;
        if (current is not null) return current;

        var generated = Generate();
        _current.Value = generated;
        return generated;
    }
}
