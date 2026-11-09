#nullable enable
using System.Collections;
using System.Collections.Generic;

namespace AplosConnector.Web.Telemetry;

/// <summary>
/// A logger scope whose values have had the PEX OAuth session id masked; stands in for the original scope when it is
/// read for export.
/// </summary>
public sealed class MaskedLogScope : IReadOnlyList<KeyValuePair<string, object?>>
{
    /// <summary>
    /// The masked key/value pairs, in the original order.
    /// </summary>
    private readonly IReadOnlyList<KeyValuePair<string, object?>> _pairs;

    /// <summary>
    /// The original scope's rendered text with the session id masked.
    /// </summary>
    private readonly string _rendered;

    /// <summary>
    /// Creates a scope over already-masked pairs and text.
    /// </summary>
    /// <param name="pairs">
    /// The masked key/value pairs, in the original order.
    /// </param>
    /// <param name="rendered">
    /// The original scope's rendered text with the session id masked.
    /// </param>
    public MaskedLogScope(IReadOnlyList<KeyValuePair<string, object?>> pairs, string rendered)
    {
        _pairs = pairs;
        _rendered = rendered;
    }

    /// <summary>
    /// Gets the number of key/value pairs.
    /// </summary>
    public int Count => _pairs.Count;

    /// <summary>
    /// Gets the key/value pair at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">
    /// The zero-based position.
    /// </param>
    /// <returns>
    /// The masked pair.
    /// </returns>
    public KeyValuePair<string, object?> this[int index] => _pairs[index];

    /// <summary>
    /// Enumerates the masked key/value pairs.
    /// </summary>
    /// <returns>
    /// An enumerator over the pairs.
    /// </returns>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        return _pairs.GetEnumerator();
    }

    /// <summary>
    /// Enumerates the masked key/value pairs.
    /// </summary>
    /// <returns>
    /// A non-generic enumerator over the pairs.
    /// </returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Renders the scope as the original scope rendered itself, with the session id masked, so text-only log providers
    /// see the same message they would have seen.
    /// </summary>
    /// <returns>
    /// The rendered scope.
    /// </returns>
    public override string ToString()
    {
        return _rendered;
    }
}
