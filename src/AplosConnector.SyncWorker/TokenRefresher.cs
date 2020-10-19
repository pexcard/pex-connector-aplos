using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using AplosConnector.Common.Models;
using AplosConnector.Core.Storages;
using PexCard.Api.Client.Core;

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
        public async Task Run([TimerTrigger("0 */55 * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"Running function to refresh tokens and clean up sessions executed at: {DateTime.UtcNow}");
            await RefreshTokens();
            await CleanupSessions();
            await CleanupSyncResults();
        }

        private async Task RefreshTokens()
        {
            _log.LogInformation("Refreshing Tokens");
            var mappings = await _mappingStorage.GetAllMappings();
            foreach (var mapping in mappings)
            {
                var externalToken = await RenewExternalToken(mapping);
                if (!mapping.PEXExternalAPIToken.Equals(externalToken, StringComparison.InvariantCultureIgnoreCase))
                {
                    mapping.PEXExternalAPIToken = externalToken;
                    mapping.LastRenewedUtc = DateTime.UtcNow;
                    await _mappingStorage.UpdateAsync(mapping);
                }
                if (!_inUseExternalApiTokens.Contains(mapping.PEXExternalAPIToken))
                {
                    _inUseExternalApiTokens.Add(mapping.PEXExternalAPIToken);
                }
            }
        }

        private async Task CleanupSessions()
        {
            _log.LogInformation("Cleaning up sessions");
            var sessions = await _sessionStorage.GetAllSessions();
            foreach (var session in sessions)
            {
                if (session.CreatedUtc < DateTime.UtcNow.AddDays(-10))
                {
                    if (!_inUseExternalApiTokens.Contains(session.ExternalToken))
                    {
                        await _pexApiClient.DeleteExternalToken(session.ExternalToken);
                        await _sessionStorage.DeleteBySessionGuidAsync(session.SessionGuid);
                    }
                }
            }
        }

        private async Task CleanupSyncResults()
        {
            _log.LogInformation("Cleaning up Sync Results");
            var syncResults = await _resultStorage.GetOldResults(DateTime.UtcNow.AddDays(-10));
            foreach(var result in syncResults)
            {
                await _resultStorage.DeleteSyncResult(result);
            }
        }

        private async Task<string> RenewExternalToken(Pex2AplosMappingModel mapping)
        {
            if (mapping.GetLastRenewedDateUtc() < DateTime.UtcNow.AddMonths(-6))
            {
                _log.LogWarning("External API token is older than 6 months and could not be renewed");
                return mapping.PEXExternalAPIToken;
            }

            if (mapping.GetLastRenewedDateUtc() < DateTime.UtcNow.AddMonths(-5))
            {
                _log.LogInformation($"Renewing external API token for business: {mapping.PEXBusinessAcctId}");
                var response = await _pexApiClient.RenewExternalToken(mapping.PEXExternalAPIToken);
                return response.Token;
            }

            return mapping.PEXExternalAPIToken;
        }
    }
}
