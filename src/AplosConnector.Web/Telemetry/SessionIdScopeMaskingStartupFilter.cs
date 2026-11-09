#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AplosConnector.Web.Telemetry;

/// <summary>
/// Gives every scope-aware logging provider (the OpenTelemetry one among them) a
/// <see cref="SessionIdMaskingScopeProvider"/> over the logger factory's scopes before the app serves its first
/// request.
/// </summary>
/// <remarks>
/// The providers stay registered as themselves, so provider-level filter rules such as
/// <c>AddFilter&lt;OpenTelemetryLoggerProvider&gt;</c> and the <c>Logging:OpenTelemetry</c> section keep working.
/// </remarks>
public sealed class SessionIdScopeMaskingStartupFilter : IStartupFilter
{
    /// <summary>
    /// Installs the masking scope provider, then builds the rest of the pipeline.
    /// </summary>
    /// <param name="next">
    /// The rest of the pipeline configuration.
    /// </param>
    /// <returns>
    /// The pipeline configuration, run after the masking is installed.
    /// </returns>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            InstallMaskingScopeProvider(app.ApplicationServices);
            next(app);
        };
    }

    #region private methods

    /// <summary>
    /// Points every scope-aware logging provider at a masking view of the logger factory's scopes.
    /// </summary>
    /// <param name="services">
    /// The application service provider.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The logger factory never handed its scope provider to <see cref="LoggerScopeProviderCapture"/>, so the
    /// session id could not be masked.
    /// </exception>
    private static void InstallMaskingScopeProvider(IServiceProvider services)
    {
        // Resolving the factory makes it hand its scope provider to every provider, the capture included.
        services.GetRequiredService<ILoggerFactory>();
        var providers = services.GetServices<ILoggerProvider>().ToList();
        var scopes = providers.OfType<LoggerScopeProviderCapture>().FirstOrDefault()?.ScopeProvider
            ?? throw new InvalidOperationException("The logger factory scope provider was not captured; the PEX OAuth session id cannot be masked in log scopes.");

        var masking = new SessionIdMaskingScopeProvider(scopes);
        foreach (var provider in providers.Where(p => p is not LoggerScopeProviderCapture).OfType<ISupportExternalScope>())
        {
            provider.SetScopeProvider(masking);
        }
    }

    #endregion
}
