using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using AplosConnector.Common.Models;
using PexCard.Api.Client.Core;
using System.Threading;
using PexCard.Api.Client.Core.Exceptions;
using System.Net;
using AplosConnector.Common.Storage;
using AplosConnector.Common.Models.Settings;
using Microsoft.Extensions.Options;
using PexCard.App.Infrastructure.AzureServiceBus.Messages;
using PexCard.App.Infrastructure.AzureServiceBus;

namespace AplosConnector.SyncWorker
{
    public class TokenRefresher
    {
        private readonly Pex2AplosMappingStorage _mappingStorage;
        private readonly PexOAuthSessionStorage _sessionStorage;
        private readonly IPexApiClient _pexApiClient;
        private readonly List<string> _inUseExternalApiTokens = new();
        private readonly SyncHistoryStorage _syncHistoryStorage;
        private readonly ILogger<TokenRefresher> _log;
        private readonly AppSettingsModel _appSettings;
        private readonly IAzureServiceBusSender _sender;

        public TokenRefresher(Pex2AplosMappingStorage mappingStorage, 
            PexOAuthSessionStorage sessionStorage, 
            IPexApiClient pexApiClient, 
            SyncHistoryStorage syncHistoryStorage,
            ILogger<TokenRefresher> log,
            IOptions<AppSettingsModel> appSettings,
            IAzureServiceBusSender sender)
        {
            _mappingStorage = mappingStorage;
            _sessionStorage = sessionStorage;
            _pexApiClient = pexApiClient;
            _syncHistoryStorage = syncHistoryStorage;
            _log = log;
            _sender = sender;
            _appSettings = appSettings?.Value;
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
                if (mapping.IsTokenExpired)
                {
                    await SendTokenExpiredEmail(mapping, cancellationToken);
                    continue;
                }

                try
                {
                    var externalToken = await RenewExternalToken(mapping, cancellationToken);
                    if (!mapping.PEXExternalAPIToken.Equals(externalToken, StringComparison.InvariantCultureIgnoreCase))
                    {
                        mapping.PEXExternalAPIToken = externalToken;
                        mapping.LastRenewedUtc = DateTime.UtcNow;
                        mapping.ExpirationEmailLastDate = null;
                        mapping.ExpirationEmailCount = 0;
                        mapping.IsTokenExpired = false;
                        await _mappingStorage.UpdateAsync(mapping, cancellationToken);
                        _log.LogInformation($"Token for business {mapping.PEXBusinessAcctId} is refreshed.");
                    }
                    if (!_inUseExternalApiTokens.Contains(mapping.PEXExternalAPIToken))
                    {
                        _inUseExternalApiTokens.Add(mapping.PEXExternalAPIToken);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"Exception during renew external token for business {mapping.PEXBusinessAcctId}. {ex}");

                    if (ex.Message.Contains("Token expired or does not exist"))
                    {
                        mapping.IsTokenExpired = true;
                        await _mappingStorage.UpdateAsync(mapping, cancellationToken);

                        await SendTokenExpiredEmail(mapping, cancellationToken);
                    }
                }
            }
        }

        private async Task SendTokenExpiredEmail(Pex2AplosMappingModel mapping, CancellationToken token)
        {
            if (mapping.GetLastRenewedDateUtc() < DateTime.UtcNow.AddMonths(-9))
            {
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(mapping.PEXEmailAccount))
                {
                    var isAllowedByDate = mapping.ExpirationEmailLastDate == null
                                          || (mapping.ExpirationEmailLastDate.HasValue
                                              && mapping.ExpirationEmailLastDate.Value.AddDays(_appSettings.EmailPeriodicityDays) < DateTime.Now);

                    var isAllowedByCount = mapping.ExpirationEmailCount < _appSettings.EmailMaxCount;

                    if (isAllowedByDate && isAllowedByCount)
                    {
                        var templateParams = new Dictionary<string, object>
                    {
                        { "APP_NAME", "Aplos" },
                        { "USER_NAME", mapping.PEXNameAccount },
                        { "APP_URL", "https://dashboard.pexcard.com/apps/app/aplosprod" }
                    };

                        var emailTemplateMessage = new EmailTemplateMessage
                        {
                            FromAddress = "adminsupport@pexcard.com",
                            ToAddress = mapping.PEXEmailAccount,
                            TemplateParams = templateParams,
                            TemplateName = "marketplace-app-token-expired-no-action"
                        };

                        await _sender.SendMessageAsync(emailTemplateMessage);

                        _log.LogInformation($"Token expiration email has been sent to the administrator {mapping.PEXEmailAccount} of the business: {mapping.PEXBusinessAcctId}.");

                        mapping.ExpirationEmailLastDate = DateTime.Now.ToUniversalTime();
                        mapping.ExpirationEmailCount++;
                        mapping.TotalExpirationEmailCount++;
                        await _mappingStorage.UpdateAsync(mapping, token);
                    }
                }
                else
                {
                    _log.LogWarning($"Cannot send token expiration email, administrator's email is missing for the business: {mapping.PEXBusinessAcctId}.");
                }
            }
            catch (Exception e)
            {
                _log.LogError(e, $"Exception while sending token expiration email {mapping.PEXEmailAccount} for the business {mapping.PEXBusinessAcctId}. {e}");
            }
        }

        private async Task CleanupSessions(CancellationToken cancellationToken)
        {
            _log.LogInformation("Cleaning up sessions");
            var sessions = await _sessionStorage.GetAllSessions(cancellationToken);
            foreach (var session in sessions)
            {
                try
                {
                    if (session.CreatedUtc < DateTime.UtcNow.AddDays(-10) && !_inUseExternalApiTokens.Contains(session.ExternalToken))
                    {
                        try
                        {
                            await _pexApiClient.DeleteExternalToken(session.ExternalToken, cancellationToken);
                        }
                        catch (PexApiClientException ex) when (ex.Code == HttpStatusCode.Unauthorized)
                        {
                            //Proceed - Token expired or does not exist
                        }
                        catch (PexApiClientException ex) when (ex.Code == HttpStatusCode.Forbidden)
                        {
                            //Proceed - Inactive user / business closed
                        }
                        await _sessionStorage.DeleteBySessionGuidAsync(session.SessionGuid, cancellationToken);
                    }
                }
                catch (Exception e)
                {
                    _log.LogError(e, $"Exception during clean-up of session '{session.SessionGuid}'. {e}");
                }
            }
        }

        private async Task CleanupSyncResults(CancellationToken cancellationToken)
        {
            _log.LogInformation("Cleaning up Sync History");
            var syncHistoryResults = await _syncHistoryStorage.GetOldResults(DateTime.UtcNow.AddYears(-3), cancellationToken);
            foreach (var result in syncHistoryResults)
            {
                await _syncHistoryStorage.DeleteSyncResult(result, cancellationToken);
            }
        }

        private async Task<string> RenewExternalToken(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            if (mapping.GetLastRenewedDateUtc() < DateTime.UtcNow.AddMonths(-6))
            {
                _log.LogWarning($"External API token is older than 6 months and could not be renewed for business: {mapping.PEXBusinessAcctId}");
                return mapping.PEXExternalAPIToken;
            }

            if (mapping.GetLastRenewedDateUtc() < DateTime.UtcNow.AddMonths(-5).AddDays(-1)) //Wait an extra day in case of timing issues (server time differences, DST complications, etc.)
            {
                _log.LogInformation($"Renewing external API token for business: {mapping.PEXBusinessAcctId}");
                var response = await _pexApiClient.RenewExternalToken(mapping.PEXExternalAPIToken, cancellationToken);
                return response.Token;
            }

            return mapping.PEXExternalAPIToken;
        }
    }
}
