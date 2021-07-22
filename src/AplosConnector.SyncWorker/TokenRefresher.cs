using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using AplosConnector.Common.Models;
using AplosConnector.Core.Storages;
using PexCard.Api.Client.Core;
using System.Threading;

namespace AplosConnector.SyncWorker
{
    public class TokenRefresher
    {
        private readonly Pex2AplosMappingStorage _mappingStorage;
        private readonly PexOAuthSessionStorage _sessionStorage;
        private readonly IPexApiClient _pexApiClient;
        private readonly List<string> _inUseExternalApiTokens = new List<string>();
        private readonly SyncResultStorage _resultStorage;
        private readonly ILogger<TokenRefresher> _log;

        public TokenRefresher(Pex2AplosMappingStorage mappingStorage, 
            PexOAuthSessionStorage sessionStorage, 
            IPexApiClient pexApiClient, 
            SyncResultStorage resultStorage, 
            ILogger<TokenRefresher> log)
        {
            _mappingStorage = mappingStorage;
            _sessionStorage = sessionStorage;
            _pexApiClient = pexApiClient;
            _resultStorage = resultStorage;
            _log = log;
        }

        [FunctionName("TokenRefresher")]
        public async Task Run(
            [TimerTrigger("0 0 2,14 * * *", RunOnStartup = false)] TimerInfo myTimer,
            CancellationToken cancellationToken,
            ILogger log)
        {
            log.LogInformation($"Running function to refresh tokens and clean up sessions executed at: {DateTime.UtcNow}");
            await RefreshTokens(cancellationToken);
            await CleanupSessions(cancellationToken);
            await CleanupSyncResults(cancellationToken);
        }

        private async Task RefreshTokens(CancellationToken cancellationToken)
        {
            _log.LogInformation("Refreshing Tokens");
            var mappings = await _mappingStorage.GetAllMappings(cancellationToken);
            foreach (var mapping in mappings)
            {
                var externalToken = await RenewExternalToken(mapping, cancellationToken);
                if (!mapping.PEXExternalAPIToken.Equals(externalToken, StringComparison.InvariantCultureIgnoreCase))
                {
                    mapping.PEXExternalAPIToken = externalToken;
                    mapping.LastRenewedUtc = DateTime.UtcNow;
                    await _mappingStorage.UpdateAsync(mapping, cancellationToken);
                }
                if (!_inUseExternalApiTokens.Contains(mapping.PEXExternalAPIToken))
                {
                    _inUseExternalApiTokens.Add(mapping.PEXExternalAPIToken);
                }
            }
        }

        private async Task CleanupSessions(CancellationToken cancellationToken)
        {
            _log.LogInformation("Cleaning up sessions");
            var sessions = await _sessionStorage.GetAllSessions(cancellationToken);
            foreach (var session in sessions)
            {
                if (session.CreatedUtc < DateTime.UtcNow.AddDays(-10) && !_inUseExternalApiTokens.Contains(session.ExternalToken))
                {
                    await _pexApiClient.DeleteExternalToken(session.ExternalToken, cancellationToken);
                    await _sessionStorage.DeleteBySessionGuidAsync(session.SessionGuid, cancellationToken);
                }
            }
        }

        private async Task CleanupSyncResults(CancellationToken cancellationToken)
        {
            _log.LogInformation("Cleaning up Sync Results");
            var syncResults = await _resultStorage.GetOldResults(DateTime.UtcNow.AddDays(-10), cancellationToken);
            foreach(var result in syncResults)
            {
                await _resultStorage.DeleteSyncResult(result, cancellationToken);
            }
        }

        private async Task<string> RenewExternalToken(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            if (mapping.GetLastRenewedDateUtc() < DateTime.UtcNow.AddMonths(-6))
            {
                _log.LogWarning("External API token is older than 6 months and could not be renewed");
                return mapping.PEXExternalAPIToken;
            }

            if (mapping.GetLastRenewedDateUtc() < DateTime.UtcNow.AddMonths(-5))
            {
                _log.LogInformation($"Renewing external API token for business: {mapping.PEXBusinessAcctId}");
                var response = await _pexApiClient.RenewExternalToken(mapping.PEXExternalAPIToken, cancellationToken);
                return response.Token;
            }

            return mapping.PEXExternalAPIToken;
        }
    }
}
