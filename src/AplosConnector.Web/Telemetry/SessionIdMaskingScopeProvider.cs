#nullable enable
using System;
using System.Collections.Generic;
using AplosConnector.Common.Tracing;
using Microsoft.Extensions.Logging;

namespace AplosConnector.Web.Telemetry;

/// <summary>
/// Logger scope provider that masks the PEX OAuth session id in every scope it hands to a logging provider.
/// </summary>
/// <remarks>
/// ASP.NET Core opens its hosting scope (<c>ConnectionId</c>, <c>RequestId</c>, <c>RequestPath</c>) before any
/// middleware runs and keeps the raw path as a string, so <c>/finish-pex-login/{sessionId}</c> would reach every
/// exported log record of that request. Scopes are pushed to the wrapped provider unchanged and masked only when read,
/// so the stored scope never changes.
/// </remarks>
public sealed class SessionIdMaskingScopeProvider : IExternalScopeProvider
{
    /// <summary>
    /// The logger factory's scope provider that holds the real scopes.
    /// </summary>
    private readonly IExternalScopeProvider _inner;

    /// <summary>
    /// Creates a masking view over <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">
    /// The logger factory's scope provider.
    /// </param>
    public SessionIdMaskingScopeProvider(IExternalScopeProvider inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>
    /// Invokes <paramref name="callback"/> for each active scope, with session ids masked.
    /// </summary>
    /// <typeparam name="TState">
    /// The caller's state type.
    /// </typeparam>
    /// <param name="callback">
    /// The callback that receives each scope.
    /// </param>
    /// <param name="state">
    /// The caller's state, passed through to <paramref name="callback"/>.
    /// </param>
    public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
    {
        _inner.ForEachScope(
            static (scope, context) => context.Callback(MaskScope(scope), context.State),
            (Callback: callback, State: state));
    }

    /// <summary>
    /// Pushes a scope onto the wrapped provider unchanged.
    /// </summary>
    /// <param name="state">
    /// The scope state.
    /// </param>
    /// <returns>
    /// A handle that pops the scope when disposed.
    /// </returns>
    public IDisposable Push(object? state)
    {
        return _inner.Push(state);
    }

    /// <summary>
    /// Masks the session id in one scope.
    /// </summary>
    /// <param name="scope">
    /// A scope as pushed by a logger: usually a list of key/value pairs, sometimes a plain string.
    /// </param>
    /// <returns>
    /// The same instance when nothing carries a session id; otherwise a masked copy.
    /// </returns>
    public static object? MaskScope(object? scope)
    {
        switch (scope)
        {
            case string text:
                return SessionIdRedaction.RedactUrl(text);
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                return MaskPairs(pairs) ?? scope;
            default:
                return scope;
        }
    }

    #region private methods

    /// <summary>
    /// Copies <paramref name="pairs"/> with session ids masked in their string values.
    /// </summary>
    /// <param name="pairs">
    /// The scope's key/value pairs.
    /// </param>
    /// <returns>
    /// The masked copy, or null when no value carries a session id.
    /// </returns>
    private static MaskedLogScope? MaskPairs(IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        var copy = new List<KeyValuePair<string, object?>>();
        var masked = false;
        foreach (var pair in pairs)
        {
            if (pair.Value is string text)
            {
                var redacted = SessionIdRedaction.RedactUrl(text);
                if (!string.Equals(redacted, text, StringComparison.Ordinal))
                {
                    masked = true;
                    copy.Add(new KeyValuePair<string, object?>(pair.Key, redacted));
                    continue;
                }
            }

            copy.Add(pair);
        }

        return masked ? new MaskedLogScope(copy) : null;
    }

    #endregion
}
