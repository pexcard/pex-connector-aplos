#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AplosConnector.Common.Tracing;
using Microsoft.AspNetCore.Http;
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
                return MaskPairs(scope, pairs) ?? scope;
            default:
                return scope;
        }
    }

    #region private methods

    /// <summary>
    /// Copies <paramref name="pairs"/> with session ids masked in their string values.
    /// </summary>
    /// <param name="scope">
    /// The original scope, whose rendered text the copy keeps with the same values masked.
    /// </param>
    /// <param name="pairs">
    /// The scope's key/value pairs.
    /// </param>
    /// <returns>
    /// The masked copy, or null when no value carries a session id.
    /// </returns>
    private static MaskedLogScope? MaskPairs(object scope, IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        var copy = new List<KeyValuePair<string, object?>>();
        List<KeyValuePair<string, string>>? replacements = null;
        foreach (var pair in pairs)
        {
            var text = AsMaskableText(pair.Value);
            if (text != null)
            {
                var redacted = SessionIdRedaction.RedactUrl(text);
                if (!string.Equals(redacted, text, StringComparison.Ordinal))
                {
                    (replacements ??= new List<KeyValuePair<string, string>>()).Add(new KeyValuePair<string, string>(text, redacted));
                    copy.Add(new KeyValuePair<string, object?>(pair.Key, redacted));
                    continue;
                }
            }

            copy.Add(pair);
        }

        if (replacements == null)
        {
            return null;
        }

        return new MaskedLogScope(copy, RenderMasked(scope, copy, replacements));
    }

    /// <summary>
    /// Returns the text of a scope value that can carry a request path, so it can be masked.
    /// </summary>
    /// <param name="value">
    /// A scope value.
    /// </param>
    /// <returns>
    /// The value's text for a string, <see cref="PathString"/> or <see cref="Uri"/>; otherwise null.
    /// </returns>
    private static string? AsMaskableText(object? value)
    {
        return value switch
        {
            string text => text,
            PathString path => path.ToString(),
            Uri uri => uri.OriginalString,
            _ => null,
        };
    }

    /// <summary>
    /// Renders the masked scope as text without the session id.
    /// </summary>
    /// <remarks>
    /// A scope that renders as its own text (a formatted scope renders its message) keeps that text with each raw
    /// value replaced by its masked form. A collection scope, which renders as its type name, or a scope whose text
    /// still carries a session id after the replacement (the value is written differently there), renders its masked
    /// pairs instead, which always pass through <see cref="RedactText"/> as a last guard.
    /// </remarks>
    /// <param name="scope">
    /// The original scope.
    /// </param>
    /// <param name="maskedPairs">
    /// The scope's key/value pairs with session ids masked.
    /// </param>
    /// <param name="replacements">
    /// Raw values (keys) and their masked forms (values).
    /// </param>
    /// <returns>
    /// The rendered scope without the session id.
    /// </returns>
    private static string RenderMasked(object scope, IReadOnlyList<KeyValuePair<string, object?>> maskedPairs, IEnumerable<KeyValuePair<string, string>> replacements)
    {
        var rendered = scope.ToString();
        if (rendered == null || string.Equals(rendered, scope.GetType().ToString(), StringComparison.Ordinal))
        {
            return RedactText(RenderPairs(maskedPairs));
        }

        foreach (var replacement in replacements)
        {
            rendered = rendered.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
        }

        if (!string.Equals(RedactText(rendered), rendered, StringComparison.Ordinal))
        {
            return RedactText(RenderPairs(maskedPairs));
        }

        return rendered;
    }

    /// <summary>
    /// Masks session ids in free text word by word.
    /// </summary>
    /// <remarks>
    /// <see cref="SessionIdRedaction.RedactUrl"/> is built for a single URL and would swallow the text after a session
    /// path up to the next <c>/</c>, <c>?</c> or <c>#</c>; a session id never contains a space, so masking each
    /// space-separated word keeps the rest of the text.
    /// </remarks>
    /// <param name="text">
    /// The text to mask.
    /// </param>
    /// <returns>
    /// The text with every session id masked.
    /// </returns>
    private static string RedactText(string text)
    {
        return string.Join(" ", text.Split(' ').Select(SessionIdRedaction.RedactUrl));
    }

    /// <summary>
    /// Renders key/value pairs as <c>Key:Value</c> separated by spaces, the way ASP.NET Core renders its hosting scope.
    /// </summary>
    /// <param name="pairs">
    /// The pairs to render.
    /// </param>
    /// <returns>
    /// The rendered pairs.
    /// </returns>
    private static string RenderPairs(IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        return string.Join(" ", pairs.Select(pair => $"{pair.Key}:{pair.Value}"));
    }

    #endregion
}
