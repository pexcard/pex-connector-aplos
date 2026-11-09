#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AplosConnector.Web.Telemetry;

/// <summary>
/// Logging provider that writes nothing; it exists so the logger factory hands it the shared scope provider, which
/// <see cref="SessionIdScopeMaskingStartupFilter"/> then wraps for the real providers.
/// </summary>
public sealed class LoggerScopeProviderCapture : ILoggerProvider, ISupportExternalScope
{
    /// <summary>
    /// Gets the logger factory's scope provider, or null before the factory has been built.
    /// </summary>
    public IExternalScopeProvider? ScopeProvider { get; private set; }

    /// <summary>
    /// Receives the logger factory's scope provider.
    /// </summary>
    /// <param name="scopeProvider">
    /// The shared scope provider.
    /// </param>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        ScopeProvider = scopeProvider;
    }

    /// <summary>
    /// Returns a logger that discards everything.
    /// </summary>
    /// <param name="categoryName">
    /// The logger category; not used.
    /// </param>
    /// <returns>
    /// The no-op logger.
    /// </returns>
    public ILogger CreateLogger(string categoryName)
    {
        return NullLogger.Instance;
    }

    /// <summary>
    /// Releases nothing; the provider holds no resources.
    /// </summary>
    public void Dispose()
    {
    }
}
