﻿using Aplos.Api.Client;
using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Const;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Extensions;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Core.Storages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Extensions;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Services
{
    public partial class AplosIntegrationService : IAplosIntegrationService
    {
        private readonly AppSettingsModel _appSettings;
        private readonly IAplosApiClientFactory _aplosApiClientFactory;
        private readonly IAplosIntegrationMappingService _aplosIntegrationMappingService;
        private readonly IPexApiClient _pexApiClient;
        private readonly SyncResultStorage _resultStorage;
        private readonly Pex2AplosMappingStorage _mappingStorage;

        public AplosIntegrationService(
            IOptions<AppSettingsModel> appSettings,
            IAplosApiClientFactory aplosApiClientFactory,
            IAplosIntegrationMappingService aplosIntegrationMappingService,
            IPexApiClient pexApiClient,
            SyncResultStorage resultStorage,
            Pex2AplosMappingStorage mappingStorage)
        {
            _appSettings = appSettings?.Value;
            _aplosApiClientFactory = aplosApiClientFactory;
            _aplosIntegrationMappingService = aplosIntegrationMappingService;
            _pexApiClient = pexApiClient;
            _resultStorage = resultStorage;
            _mappingStorage = mappingStorage;
        }

        public async Task<Pex2AplosMappingModel> EnsureMappingInstalled(PexOAuthSessionModel session, CancellationToken cancellationToken)
        {
            var mapping = await _mappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null)
            {
                mapping = new Pex2AplosMappingModel
                {
                    CreatedUtc = DateTime.UtcNow,
                    PEXBusinessAcctId = session.PEXBusinessAcctId,
                    PEXExternalAPIToken = session.ExternalToken,
                    LastRenewedUtc = session.LastRenewedUtc,
                    EarliestTransactionDateToSync = DateTime.UtcNow,
                    AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication,
                };

                await _mappingStorage.CreateAsync(mapping, cancellationToken);
            }
            else if (mapping.PEXExternalAPIToken != session.ExternalToken)
            {
                mapping.PEXExternalAPIToken = session.ExternalToken;
                await _mappingStorage.UpdateAsync(mapping, cancellationToken);
            }

            await EnsurePartnerInfoPopulated(mapping, cancellationToken);

            return mapping;
        }

        public async Task EnsurePartnerInfoPopulated(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var isChanged = false;

            if (string.IsNullOrWhiteSpace(mapping.AplosAccountId))
            {
                var partnerInfo = await _pexApiClient.GetPartner(mapping.PEXExternalAPIToken, cancellationToken);
                mapping.AplosAccountId = partnerInfo.PartnerBusinessId;
                isChanged |= !string.IsNullOrWhiteSpace(mapping.AplosAccountId);
            }

            if (_appSettings.EnforceAplosPartnerVerification && mapping.AplosAuthenticationMode == AplosAuthenticationMode.ClientAuthentication)
            {
                mapping.AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication;
                isChanged = true;
            }

            if (!string.IsNullOrWhiteSpace(mapping.AplosAccountId) && !mapping.AplosPartnerVerified)
            {
                var aplosApiClient = MakeAplosApiClient(mapping, AplosAuthenticationMode.PartnerAuthentication);

                try
                {
                    var aplosResponse = await aplosApiClient.GetPartnerVerification(cancellationToken);
                    mapping.AplosPartnerVerified = aplosResponse.Data.PartnerVerification.Authorized;
                    isChanged |= mapping.AplosPartnerVerified;
                }
                catch (AplosApiException ex) when (ex.AplosApiError.Status == 401)
                {
                    //Expected if PEX is not authorized to access their Aplos account yet
                }
                catch (AplosApiException ex) when (ex.AplosApiError.Status == 422)
                {
                    //Expected if they aren't verified yet
                }
            }

            if (!string.IsNullOrWhiteSpace(mapping.AplosAccountId) && mapping.AplosPartnerVerified && mapping.AplosAuthenticationMode == AplosAuthenticationMode.ClientAuthentication)
            {
                mapping.AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication;
                isChanged = true;
            }

            if (isChanged)
            {
                await _mappingStorage.UpdateAsync(mapping, cancellationToken);
            }
        }

        public IAplosApiClient MakeAplosApiClient(Pex2AplosMappingModel mapping, AplosAuthenticationMode? overrideAuthenticationMode = null)
        {
            string aplosAccountId;
            string aplosClientId;
            string aplosPrivateKey;

            var authenticationMode = overrideAuthenticationMode ?? mapping.AplosAuthenticationMode;
            switch (authenticationMode)
            {
                case AplosAuthenticationMode.ClientAuthentication:
                    if (string.IsNullOrWhiteSpace(mapping.AplosClientId))
                    {
                        throw new InvalidOperationException($"{nameof(mapping.AplosClientId)} is required.");
                    }

                    if (string.IsNullOrWhiteSpace(mapping.AplosPrivateKey))
                    {
                        throw new InvalidOperationException($"{nameof(mapping.AplosPrivateKey)} is required.");
                    }

                    aplosAccountId = null;
                    aplosClientId = mapping.AplosClientId;
                    aplosPrivateKey = mapping.AplosPrivateKey;
                    break;
                case AplosAuthenticationMode.PartnerAuthentication:
                    if (string.IsNullOrWhiteSpace(mapping.AplosAccountId))
                    {
                        throw new InvalidOperationException($"{nameof(mapping.AplosAccountId)} is required.");
                    }

                    aplosAccountId = mapping.AplosAccountId;
                    aplosClientId = _appSettings.AplosApiClientId;
                    aplosPrivateKey = _appSettings.AplosApiClientSecret;
                    break;
                default:
                    throw new InvalidEnumArgumentException(nameof(mapping.AplosAuthenticationMode), (int)mapping.AplosAuthenticationMode, typeof(AplosAuthenticationMode));
            }

            return _aplosApiClientFactory.CreateClient(
                aplosAccountId,
                aplosClientId,
                aplosPrivateKey,
                _appSettings.AplosApiBaseURL,
                (logger) =>
                {
                    if (mapping.AplosAccessToken != null && mapping.AplosAccessTokenExpiresAt.HasValue)
                    {
                        return new AplosAuthModel { AplosAccessToken = mapping.AplosAccessToken, AplosAccessTokenExpiresAt = mapping.AplosAccessTokenExpiresAt.Value, };
                    }
                    return null;
                },
                async (auth, logger, cancellationToken) =>
                {
                    try
                    {
                        mapping.AplosAccessToken = auth.AplosAccessToken;
                        mapping.AplosAccessTokenExpiresAt = auth.AplosAccessTokenExpiresAt;
                        await _mappingStorage.UpdateAsync(mapping, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, $"Failed to update settings in storage. {ex.Message}");
                        throw;
                    }
                });
        }

        public async Task<PexAplosApiObject> GetAplosContact(Pex2AplosMappingModel mapping, int aplosContactId, CancellationToken cancellationToken)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetContact(aplosContactId, cancellationToken);

            return _aplosIntegrationMappingService.Map(aplosApiResponse?.Data?.Contact);
        }

        public async Task<IEnumerable<PexAplosApiObject>> GetAplosContacts(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetContacts();

            return _aplosIntegrationMappingService.Map(aplosApiResponse);
        }

        public async Task<PexAplosApiObject> GetAplosFund(Pex2AplosMappingModel mapping, int aplosFundId, CancellationToken cancellationToken)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetFund(aplosFundId, cancellationToken);

            return _aplosIntegrationMappingService.Map(aplosApiResponse?.Data?.Fund);
        }

        public async Task<IEnumerable<PexAplosApiObject>> GetAplosFunds(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetFunds(cancellationToken);

            return _aplosIntegrationMappingService.Map(aplosApiResponse);
        }

        public async Task<PexAplosApiObject> GetAplosAccount(Pex2AplosMappingModel mapping, decimal aplosAccountNumber, CancellationToken cancellationToken)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetAccount(aplosAccountNumber, cancellationToken);

            return _aplosIntegrationMappingService.Map(aplosApiResponse?.Data?.Account);
        }

        public async Task<IEnumerable<PexAplosApiObject>> GetAplosAccounts(Pex2AplosMappingModel mapping, string aplosAccountCategory = null, CancellationToken cancellationToken = default)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetAccounts(aplosAccountCategory, cancellationToken);

            var mappedAccounts = _aplosIntegrationMappingService.Map(aplosApiResponse);
            return DedupeAplosAccounts(mappedAccounts);
        }

        public static IEnumerable<PexAplosApiObject> DedupeAplosAccounts(IEnumerable<PexAplosApiObject> aplosAccounts)
        {
            if (aplosAccounts == null)
            {
                return Enumerable.Empty<PexAplosApiObject>();
            }

            var dedupedAccountNames = new HashSet<string>();
            var uniqueAccounts = new Dictionary<string, PexAplosApiObject>(aplosAccounts.Count());
            foreach (var account in aplosAccounts)
            {
                var originalAccountName = account.Name;
                if (uniqueAccounts.TryGetValue(originalAccountName, out var existingAccount))
                {
                    account.Name = DedupeAplosAccountName(account);

                    if (dedupedAccountNames.Add(originalAccountName))
                    {
                        existingAccount.Name = DedupeAplosAccountName(existingAccount);
                    }
                }

                uniqueAccounts.Add(account.Name, account);
            }

            return uniqueAccounts.Values;

            string DedupeAplosAccountName(PexAplosApiObject account)
            {
                return $"{account.Name} ({account.Id})";
            }
        }

        public async Task<List<AplosApiTransactionDetail>> GetTransactions(Pex2AplosMappingModel mapping, DateTime startDate, CancellationToken cancellationToken)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var response = await aplosApiClient.GetTransactions(startDate, cancellationToken);

            return response;
        }

        public async Task<TransactionSyncResult> SyncTransaction(
            IEnumerable<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)> allocationDetails,
            Pex2AplosMappingModel mapping,
            TransactionModel transaction,
            CardholderDetailsModel cardholderDetails,
            CancellationToken cancellationToken)
        {
            var lines = new List<AplosApiTransactionLineDetail>();

            AplosApiContactDetail contact = default;
            foreach (var allocationDetail in allocationDetails)
            {
                //Allocation amounts are always provided as positive here but Aplos wants them as the same sign as the transaction.
                //For example, a purchase should be negative here, otherwise it will show in Aplos as a deposit.
                var allocationAmount = allocationDetail.allocation.Amount * Math.Sign(transaction.TransactionAmount);

                var line1 = new AplosApiTransactionLineDetail
                {
                    Account = new AplosApiAccountDetail { AccountNumber = allocationDetail.pexTagValues.AplosRegisterAccountNumber },
                    Amount = allocationAmount,
                    Fund = new AplosApiFundDetail { Id = allocationDetail.pexTagValues.AplosFundId },
                };
                lines.Add(line1);

                var line2 = new AplosApiTransactionLineDetail
                {
                    Account = new AplosApiAccountDetail { AccountNumber = allocationDetail.pexTagValues.AplosTransactionAccountNumber },
                    Amount = -allocationAmount,
                    Fund = new AplosApiFundDetail { Id = allocationDetail.pexTagValues.AplosFundId },
                };

                if (allocationDetail.pexTagValues.AplosTagIds != null)
                {
                    line2.Tags = new List<AplosApiTagDetail>();
                    foreach (var aplosTagId in allocationDetail.pexTagValues.AplosTagIds)
                    {
                        var tagValue = new AplosApiTagDetail
                        {
                            Id = aplosTagId,
                        };
                        line2.Tags.Add(tagValue);
                    }
                }

                if (!string.IsNullOrWhiteSpace(allocationDetail.pexTagValues.AplosTaxTagId))
                {
                    line2.TaxTag = new AplosApiTaxTagDetail
                    {
                        Id = allocationDetail.pexTagValues.AplosTaxTagId
                    };
                }

                lines.Add(line2);

                if (contact is null)
                {
                    if (mapping.SyncTransactionsCreateContact)
                    {
                        //Specifying the name here will use the existing contact with that name, otherwise it will create a new one.
                        contact = new AplosApiContactDetail { CompanyName = transaction.MerchantName, Type = "company", };
                    }
                    else
                    {
                        contact = new AplosApiContactDetail { Id = allocationDetail.pexTagValues.AplosContactId, };
                    }
                }
            }

            var noteBuilder = new StringBuilder(transaction.TransactionId.ToString());
            if (cardholderDetails != null)
            {
                noteBuilder.Append($" | {cardholderDetails.ProfileAddress.ContactName}");
            }

            transaction.TransactionNotes?.ForEach(n => noteBuilder.Append($" | {n.NoteText}"));

            //Max length of note is 1000 chars (at least UI doesn't allow to enter more)
            const int noteMaxLength = 1000;
            var aplosTransactionNote = noteBuilder.Length > noteMaxLength
                ? noteBuilder.ToString(0, noteMaxLength)
                : noteBuilder.ToString();

            var aplosTransaction = new AplosApiTransactionDetail
            {
                Contact = contact,
                Amount = transaction.TransactionAmount,
                Date = transaction.TransactionTime,
                Note = aplosTransactionNote,
                Lines = lines.ToArray(),
            };

            var aplosApiClient = MakeAplosApiClient(mapping);
            await aplosApiClient.CreateTransaction(aplosTransaction, cancellationToken);

            return TransactionSyncResult.Success;
        }

        public async Task<string> GetAplosAccessToken(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            return await aplosApiClient.GetAplosAccessToken(cancellationToken);
        }

        public async Task<bool> ValidateAplosApiCredentials(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            switch (mapping.AplosAuthenticationMode)
            {
                case AplosAuthenticationMode.ClientAuthentication:
                    if (string.IsNullOrWhiteSpace(mapping.AplosClientId) || string.IsNullOrWhiteSpace(mapping.AplosPrivateKey))
                    {
                        return false;
                    }
                    break;
                case AplosAuthenticationMode.PartnerAuthentication:
                    if (string.IsNullOrWhiteSpace(mapping.AplosAccountId))
                    {
                        return false;
                    }
                    break;
                default:
                    throw new InvalidEnumArgumentException(nameof(mapping.AplosAuthenticationMode), (int)mapping.AplosAuthenticationMode, typeof(AplosAuthenticationMode));
            }

            var aplosApiClient = MakeAplosApiClient(mapping);

            var canObtainAccessToken = await aplosApiClient.GetAndValidateAplosAccessToken(cancellationToken);
            return canObtainAccessToken;
        }

        public async Task Sync(Pex2AplosMappingModel mapping, ILogger log, CancellationToken cancellationToken)
        {
            log.LogInformation("C# Queue trigger function processing.");

            if (mapping.PEXBusinessAcctId == default)
            {
                log.LogWarning($"C# Queue trigger function completed. Business account ID is {mapping.PEXBusinessAcctId}");
                return;
            }

            using (log.BeginScope(GetLoggingScopeForSync(mapping)))
            {

                //Let's refresh Aplos API tokens before sync start and interrupt sync processor in case of invalidity
                var aplosAccessToken = await GetAplosAccessToken(mapping, cancellationToken);
                if (string.IsNullOrEmpty(aplosAccessToken))
                {
                    log.LogCritical(
                        $"Integration for business {mapping.PEXBusinessAcctId} is not working. access API token is invalid");
                    return;
                }

                await EnsurePartnerInfoPopulated(mapping, cancellationToken);

                var utcNow = DateTime.UtcNow;
                List<TransactionModel> additionalFees = default;
                try
                {
                    additionalFees = await SyncTransactions(log, mapping, utcNow, cancellationToken);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, $"Exception during transactions sync for business: {mapping.PEXBusinessAcctId}");
                }

                try
                {
                    await SyncBusinessAccountTransactions(log, mapping, utcNow, additionalFees, cancellationToken);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, $"Exception during business account transactions sync for business: {mapping.PEXBusinessAcctId}.");
                }

                try
                {
                    await SyncFundsToPex(log, mapping, cancellationToken);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, $"Exception during {nameof(SyncFundsToPex)} for business: {mapping.PEXBusinessAcctId}.");
                }

                try
                {
                    await SyncExpenseAccountsToPex(log, mapping, cancellationToken);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, $"Exception during {nameof(SyncExpenseAccountsToPex)} for business: {mapping.PEXBusinessAcctId}.");
                }

                try
                {
                    await SyncAplosTagsToPex(log, mapping, cancellationToken);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, $"Exception during {nameof(SyncAplosTagsToPex)} for business: {mapping.PEXBusinessAcctId}.");
                }

                try
                {
                    await SyncAplosTaxTagsToPex(log, mapping, cancellationToken);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, $"Exception during {nameof(SyncAplosTaxTagsToPex)} for business: {mapping.PEXBusinessAcctId}.");
                }

                mapping.LastSyncUtc = utcNow;
                await _mappingStorage.UpdateAsync(mapping, cancellationToken);

            }

            log.LogInformation("C# Queue trigger function completed.");
        }

        private async Task SyncAplosTagsToPex(ILogger log, Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            if (mapping.TagMappings == null) return;

            var aplosApiClient = MakeAplosApiClient(mapping);
            var aplosTagCategories = await aplosApiClient.GetTags(cancellationToken);

            foreach (var tagMapping in mapping.TagMappings)
            {
                if (!tagMapping.SyncToPex) continue;

                if (string.IsNullOrEmpty(tagMapping.AplosTagId))
                {
                    log.LogWarning($"Tag sync is enabled but {nameof(tagMapping.AplosTagId)} is not specified for business: {mapping.PEXBusinessAcctId}");
                    continue;
                }

                var aplosTagCategory = aplosTagCategories.SingleOrDefault(atc => atc.Id == tagMapping.AplosTagId);
                if (aplosTagCategory == null)
                {
                    log.LogWarning($"Unable to find a single tag for {nameof(tagMapping.AplosTagId)} '{tagMapping.AplosTagId}'. Searched categories: {aplosTagCategories.Count}");
                    continue;
                }

                if (string.IsNullOrEmpty(tagMapping.PexTagId))
                {
                    log.LogWarning($"Tag sync is enabled but {nameof(tagMapping.PexTagId)} is not specified for business: {mapping.PEXBusinessAcctId}");
                    continue;
                }

                var pexTag = await _pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken, tagMapping.PexTagId, cancellationToken);
                if (pexTag == null)
                {
                    log.LogWarning($"{nameof(tagMapping.PexTagId)} is unavailable in business: {mapping.PEXBusinessAcctId}");
                    continue;
                }

                log.LogInformation($"Syncing tags from {nameof(tagMapping.AplosTagId)} '{tagMapping.AplosTagId} / {aplosTagCategory.Name}' to {nameof(tagMapping.PexTagId)} '{tagMapping.PexTagId} / {pexTag.Name}' for business: {mapping.PEXBusinessAcctId}");

                var flattenedAplosTags = GetFlattenedAplosTagValues(aplosTagCategory, cancellationToken);
                var aplosTagsToSync = _aplosIntegrationMappingService.Map(flattenedAplosTags);


                SyncStatus syncStatus;
                var syncCount = 0;
                string syncNotes = null;

                try
                {
                    pexTag.UpsertTagOptions(aplosTagsToSync, out syncCount);

                    await _pexApiClient.UpdateDropdownTag(mapping.PEXExternalAPIToken, pexTag.Id, pexTag, cancellationToken);
                    syncStatus = SyncStatus.Success;
                }
                catch (Exception ex)
                {
                    syncStatus = SyncStatus.Failed;
                    syncNotes = $"Error updating TagId {pexTag.Id}: {ex.Message}";

                    log.LogError(ex, $"Error updating TagId {pexTag.Id}");
                }

                var result = new SyncResultModel
                {
                    PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                    SyncType = $"Tag Values ({aplosTagCategory.Name})",
                    SyncStatus = syncStatus.ToString(),
                    SyncedRecords = syncCount,
                    SyncNotes = syncNotes
                };
                await _resultStorage.CreateAsync(result, cancellationToken);
            }
        }

        private async Task SyncAplosTaxTagsToPex(ILogger log, Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            if (!(mapping.SyncTags && mapping.SyncTaxTagToPex)) return;

            if (string.IsNullOrEmpty(mapping.PexTaxTagId))
            {
                log.LogWarning($"Tag sync is enabled but {nameof(mapping.PexTaxTagId)} is not specified for business: {mapping.PEXBusinessAcctId}");
                return;
            }

            var pexTaxTag = await _pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken, mapping.PexTaxTagId, cancellationToken);
            if (pexTaxTag == null)
            {
                log.LogWarning($"{nameof(mapping.PexTaxTagId)} is unavailable in business: {mapping.PEXBusinessAcctId}");
                return;
            }

            var flattenedTaxTags = await GetFlattenedAplosTaxTagValues(mapping, cancellationToken);
            var aplosTaxTagsToSync = flattenedTaxTags.ToList();
            log.LogInformation($"Syncing {aplosTaxTagsToSync.Count} tax tags to {nameof(mapping.PexTaxTagId)} '{mapping.PexTaxTagId} / {pexTaxTag.Name}' for business: {mapping.PEXBusinessAcctId}");

            SyncStatus syncStatus;
            var syncCount = 0;

            try
            {
                pexTaxTag.UpsertTagOptions(aplosTaxTagsToSync, out syncCount);
                await _pexApiClient.UpdateDropdownTag(mapping.PEXExternalAPIToken, pexTaxTag.Id, pexTaxTag, cancellationToken);
                syncStatus = SyncStatus.Success;
            }
            catch (Exception ex)
            {
                syncStatus = SyncStatus.Failed;
                log.LogError(ex, $"Error updating TagId {pexTaxTag.Id}");
            }

            var result = new SyncResultModel
            {
                PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                SyncType = $"Tag Values (990)",
                SyncStatus = syncStatus.ToString(),
                SyncedRecords = syncCount,
            };
            await _resultStorage.CreateAsync(result, cancellationToken);
        }

        private async Task<IEnumerable<PexAplosApiObject>> GetFlattenedAplosTaxTagValues(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var tagValues = new List<AplosApiTaxTagDetail>();

            var taxTagCategoryDetails = await GetAplosApiTaxTagExpenseCategoryDetails(mapping, cancellationToken);

            foreach (var tagCategory in taxTagCategoryDetails)
            {
                if (tagCategory.TaxTags != null)
                {
                    tagValues.AddRange(tagCategory.TaxTags);
                }
            }

            return _aplosIntegrationMappingService.Map(tagValues);
        }

        public async Task<IEnumerable<AplosApiTaxTagCategoryDetail>> GetAplosApiTaxTagExpenseCategoryDetails(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var taxTags = await aplosApiClient.GetTaxTags(cancellationToken);
            return taxTags.Where(c => c.Type == "expense");
        }

        public async Task<Pex2AplosMappingModel> UpdateFundingSource(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            if (mapping.PEXFundingSource == 0)
            {
                var businessSettings = await _pexApiClient.GetBusinessSettings(mapping.PEXExternalAPIToken, cancellationToken);
                mapping.PEXFundingSource = businessSettings.FundingSource;
            }

            return mapping;
        }

        private async Task<IEnumerable<PexAplosApiObject>> GetFlattenedAplosTagValues(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var tagValues = new List<AplosApiTagDetail>();

            var aplosApiClient = MakeAplosApiClient(mapping);
            foreach (var tagCategory in await aplosApiClient.GetTags(cancellationToken))
            {
                var categoryTagValues = GetFlattenedAplosTagValues(tagCategory, cancellationToken);
                tagValues.AddRange(categoryTagValues);
            }

            return _aplosIntegrationMappingService.Map(tagValues);
        }

        public IEnumerable<AplosApiTagDetail> GetFlattenedAplosTagValues(AplosApiTagCategoryDetail aplosTagCategory, CancellationToken cancellationToken)
        {
            var tagValues = new List<AplosApiTagDetail>();

            if (aplosTagCategory.TagGroups != null)
            {
                foreach (var tagGroup in aplosTagCategory.TagGroups)
                {
                    var groupTagValues = GetFlattenedAplosTagValues(tagGroup, cancellationToken);
                    tagValues.AddRange(groupTagValues);
                }
            }

            return tagValues;
        }

        private IEnumerable<AplosApiTagDetail> GetFlattenedAplosTagValues(AplosApiTagGroupDetail aplosTagGroup, CancellationToken cancellationToken)
        {
            var tagValues = new List<AplosApiTagDetail>();

            if (aplosTagGroup.Tags != null)
            {
                foreach (var tagValue in aplosTagGroup.Tags)
                {
                    var groupTagValues = GetFlattenedAplosTagValues(tagValue, cancellationToken);
                    tagValues.AddRange(groupTagValues);
                }
            }

            return tagValues;
        }

        private IEnumerable<AplosApiTagDetail> GetFlattenedAplosTagValues(AplosApiTagDetail aplosTagValue, CancellationToken cancellationToken)
        {
            var tagValues = new List<AplosApiTagDetail>();
            tagValues.Add(aplosTagValue);

            if (aplosTagValue.SubTags != null)
            {
                foreach (var subTag in aplosTagValue.SubTags)
                {
                    var subTagValues = GetFlattenedAplosTagValues(subTag, cancellationToken);
                    tagValues.AddRange(subTagValues);
                }
            }

            return tagValues;
        }

        #region Private methods

        private async Task SyncFundsToPex(
            ILogger log,
            Pex2AplosMappingModel mapping,
            CancellationToken cancellationToken)
        {
            if (!(mapping.SyncTags && mapping.SyncFundsToPex)) return;

            if (string.IsNullOrEmpty(mapping.PexFundsTagId))
            {
                log.LogWarning($"{nameof(mapping.PexFundsTagId)} is not specified for business: {mapping.PEXBusinessAcctId}");
                return;
            }

            log.LogInformation($"Syncing funds for business: {mapping.PEXBusinessAcctId}");

            var fundsTag = await _pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken, mapping.PexFundsTagId, cancellationToken);
            if (fundsTag == null)
            {
                log.LogWarning($"{nameof(mapping.PexFundsTagId)} is unavailable in business: {mapping.PEXBusinessAcctId}");
                return;
            }

            var aplosFunds = await GetAplosFunds(mapping, cancellationToken);

            SyncStatus syncStatus;
            var syncCount = 0;
            string syncNotes = null;

            try
            {
                fundsTag.UpsertTagOptions(aplosFunds, out syncCount);

                await _pexApiClient.UpdateDropdownTag(mapping.PEXExternalAPIToken, fundsTag.Id, fundsTag, cancellationToken);
                syncStatus = SyncStatus.Success;
            }
            catch (Exception ex)
            {
                syncStatus = SyncStatus.Failed;
                syncNotes = $"Error updating TagId {fundsTag.Id}: {ex.Message}";

                log.LogError(ex, $"Error updating TagId {fundsTag.Id}");
            }

            var result = new SyncResultModel
            {
                PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                SyncType = "Tag Values (Funds)",
                SyncStatus = syncStatus.ToString(),
                SyncedRecords = syncCount,
                SyncNotes = syncNotes
            };
            await _resultStorage.CreateAsync(result, cancellationToken);
        }

        private async Task SyncExpenseAccountsToPex(
            ILogger log,
            Pex2AplosMappingModel mapping,
            CancellationToken cancellationToken)
        {
            if (!(mapping.SyncTags && mapping.ExpenseAccountMappings != null && mapping.ExpenseAccountMappings.Any())) return;

            if (mapping.ExpenseAccountMappings == null || !mapping.ExpenseAccountMappings.Any())
            {
                log.LogWarning($"{nameof(mapping.ExpenseAccountMappings)} is not specified for business: {mapping.PEXBusinessAcctId}");
                return;
            }

            log.LogInformation($"Syncing accounts for business: {mapping.PEXBusinessAcctId}");

            var aplosAccounts = await GetAplosAccounts(mapping, GetAplosAccountCategory(mapping.PEXFundingSource), cancellationToken);

            foreach (var expenseAccountMapping in mapping.ExpenseAccountMappings)
            {
                await SyncExpenseAccount(
                    log,
                    mapping,
                    expenseAccountMapping,
                    aplosAccounts,
                    cancellationToken);
            }
        }

        private async Task SyncExpenseAccount(
            ILogger log,
            Pex2AplosMappingModel model,
            ExpenseAccountMappingModel expenseAccountMapping,
            IEnumerable<PexAplosApiObject> accounts,
            CancellationToken cancellationToken)
        {
            if (!expenseAccountMapping.SyncExpenseAccounts)
            {
                return;
            }

            var accountsTag =
                await _pexApiClient.GetDropdownTag(model.PEXExternalAPIToken, expenseAccountMapping.ExpenseAccountsPexTagId, cancellationToken);
            if (accountsTag == null)
            {
                log.LogWarning($"Expense accounts tag (Id '{expenseAccountMapping.ExpenseAccountsPexTagId}' is unavailable in business: {model.PEXBusinessAcctId}");
                return;
            }


            SyncStatus syncStatus;
            var syncCount = 0;
            string syncNotes = null;

            try
            {
                accountsTag.UpsertTagOptions(accounts, out syncCount);

                await _pexApiClient.UpdateDropdownTag(model.PEXExternalAPIToken, accountsTag.Id, accountsTag, cancellationToken);
                syncStatus = SyncStatus.Success;
            }
            catch (Exception ex)
            {
                syncStatus = SyncStatus.Failed;
                syncNotes = $"Error updating TagId {accountsTag.Id}: {ex.Message}";

                log.LogError(ex, $"Error updating TagId {accountsTag.Id}");
            }

            var result = new SyncResultModel
            {
                PEXBusinessAcctId = model.PEXBusinessAcctId,
                SyncType = "Tag Values (Accounts)",
                SyncStatus = syncStatus.ToString(),
                SyncedRecords = syncCount,
                SyncNotes = syncNotes
            };
            await _resultStorage.CreateAsync(result, cancellationToken);
        }

        private async Task<List<TransactionModel>> SyncTransactions(
            ILogger log,
            Pex2AplosMappingModel mapping,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            if (!mapping.SyncTransactions) return default;

            var oneYearAgo = DateTime.Today.AddYears(-1).ToEst();
            var startDate = mapping.EarliestTransactionDateToSync.ToEst();

            startDate = startDate > oneYearAgo ? startDate : oneYearAgo;

            var endDate = utcNow.AddDays(1).ToEst();

            var allFees = new List<TransactionModel>();

            var dateRangeBatches = GetDateRangeBatches(new DateRange(startDate, endDate), 28);

            foreach (var dateRangeBatch in dateRangeBatches)
            {
                log.LogInformation($"Getting cardholder transactions from {dateRangeBatch.Start} to {dateRangeBatch.End} for business: {mapping.PEXBusinessAcctId}");

                var allCardholderTransactions = await _pexApiClient.GetAllCardholderTransactions(mapping.PEXExternalAPIToken,
                    dateRangeBatch.Start, dateRangeBatch.End, token: cancellationToken);

                var transactions = FilterTransactions(mapping, allCardholderTransactions);

                var fees = allCardholderTransactions.SelectCardAccountFees();
                allFees.AddRange(fees);

                log.LogInformation($"Syncing {transactions.Count} transactions for business: {mapping.PEXBusinessAcctId}");

                var useTags = await _pexApiClient.IsTagsAvailable(mapping.PEXExternalAPIToken, CustomFieldType.Dropdown, cancellationToken);

                var aplosFunds = (await GetAplosFunds(mapping, cancellationToken)).ToList();

                var aplosAccountCategory = GetAplosAccountCategory(mapping.PEXFundingSource);
                
                var aplosExpenseAccounts = (await GetAplosAccounts(mapping, aplosAccountCategory, cancellationToken)).ToList();
                var aplosTags = (await GetFlattenedAplosTagValues(mapping, cancellationToken)).ToList();

                log.LogInformation($"Retrieved ALL funds from Aplos: {JsonConvert.SerializeObject(aplosFunds, new JsonSerializerSettings { Error = (sender, args) => args.ErrorContext.Handled = true })}");
                log.LogInformation($"Retrieved ALL {aplosAccountCategory} accounts from Aplos: {JsonConvert.SerializeObject(aplosExpenseAccounts, new JsonSerializerSettings { Error = (sender, args) => args.ErrorContext.Handled = true })}");
                log.LogInformation($"Retrieved ALL tags from Aplos: {JsonConvert.SerializeObject(aplosTags, new JsonSerializerSettings { Error = (sender, args) => args.ErrorContext.Handled = true })}");

                List<TagDropdownDetailsModel> dropdownTags = default;
                if (useTags)
                {
                    var dropdownTagTasks = new List<Task<TagDropdownDetailsModel>>
                {
                    _pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken, mapping.PexFundsTagId, cancellationToken),
                };
                    if (mapping.ExpenseAccountMappings != null)
                    {
                        foreach (var expenseAccountMapping in mapping.ExpenseAccountMappings)
                        {
                            dropdownTagTasks.Add(_pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken,
                                expenseAccountMapping.ExpenseAccountsPexTagId, cancellationToken));
                        }
                    }
                    if (mapping.TagMappings != null)
                    {
                        foreach (var tagMapping in mapping.TagMappings)
                        {
                            dropdownTagTasks.Add(
                                _pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken, tagMapping.PexTagId, cancellationToken));
                        }
                    }
                    await Task.WhenAll(dropdownTagTasks);
                    dropdownTags = dropdownTagTasks.Where(t => !t.IsFaulted).Select(t => t.Result).ToList();
                    foreach (var failedTask in dropdownTagTasks.Where(t => t.IsFaulted))
                    {
                        log.LogError(failedTask.Exception?.InnerException,
                            $"Exception getting dropdown tag for business {mapping.PEXBusinessAcctId}. {failedTask.Exception?.InnerException}");
                    }
                }

                var allocationMapping = await _pexApiClient.GetTagAllocations(mapping.PEXExternalAPIToken, new CardholderTransactions(transactions), cancellationToken);

                var syncCount = 0;
                var failureCount = 0;
                var eligibleCount = 0;
                foreach (var transaction in transactions)
                {
                    using (log.BeginScope(GetLoggingScopeForTransaction(transaction)))
                    {
                        log.LogInformation($"Processing transaction {transaction.TransactionId}");

                        if (!allocationMapping.TryGetValue(transaction.TransactionId, out var allocations))
                        {
                            log.LogWarning($"Transaction {transaction.TransactionId} doesn't have an associated allocation. Skipping");
                            continue;
                        }

                        var allocationDetails = new List<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)>();
                        string syncIneligibilityReason = null;

                        foreach (var allocation in allocations)
                        {
                            var pexTagValues = new PexTagValuesModel
                            {
                                AplosRegisterAccountNumber = mapping.AplosRegisterAccountNumber,
                                AplosContactId = mapping.DefaultAplosContactId,
                            };

                            if (useTags)
                            {
                                var allocationFundTag = allocation.GetTagValue(mapping.PexFundsTagId);
                                if (allocationFundTag != null)
                                {
                                    var fundTagDefinition = dropdownTags.FirstOrDefault(t => t.Id.Equals(allocationFundTag.TagId, StringComparison.InvariantCultureIgnoreCase));
                                    var allocationFundTagOptionValue = allocationFundTag?.Value?.ToString();
                                    var allocationFundTagOptionName = allocationFundTag?.GetTagOptionName(fundTagDefinition?.Options);
                                    var allocationFundEntity = aplosFunds.FindMatchingEntity(allocationFundTagOptionValue, allocationFundTagOptionName, ':', log);
                                    if (allocationFundEntity == null)
                                    {
                                        log.LogWarning($"Could not match PEX fund tag '{fundTagDefinition?.Name}' option ['{allocationFundTagOptionName}' : '{allocationFundTagOptionValue}'] with an Aplos fund entity.");
                                    }
                                    else
                                    {
                                        if (int.TryParse(allocationFundEntity.Id, out var aplosFundId))
                                        {
                                            log.LogInformation($"Matched PEX fund tag '{fundTagDefinition?.Name}' option ['{allocationFundTagOptionName}' : '{allocationFundTagOptionValue}'] with Aplos fund entity '{allocationFundEntity.Name}' ({allocationFundEntity.Id}).");
                                            pexTagValues.AplosFundId = aplosFundId;
                                        }
                                        else
                                        {
                                            log.LogWarning($"Could not parse Aplos {aplosAccountCategory} fund id '{allocationFundEntity.Id}' into a int.");
                                        }
                                    }
                                }
                                else
                                {
                                    log.LogInformation($"No PEX fund tag on allocation {allocation.Amount:C}.");
                                }

                                TagValueItem expenseAccountTag = null;
                                foreach (var expenseAccountMapping in mapping.ExpenseAccountMappings)
                                {
                                    var expenseAccountTransactionTag = allocation.GetTagValue(expenseAccountMapping.ExpenseAccountsPexTagId);
                                    if (expenseAccountTransactionTag != null)
                                    {
                                        expenseAccountTag = expenseAccountTransactionTag;
                                        break;
                                    }
                                }

                                if (expenseAccountTag == null)
                                {
                                    syncIneligibilityReason = $"Transaction {transaction.TransactionId} doesn't have {aplosAccountCategory} Account tagged. Skipping";
                                    break;
                                }

                                var expenseAccountTagDefinition = dropdownTags.FirstOrDefault(t => t.Id.Equals(expenseAccountTag.TagId, StringComparison.InvariantCultureIgnoreCase));
                                var allocationExpenseAccountTagOptionName = expenseAccountTag.GetTagOptionName(expenseAccountTagDefinition?.Options);
                                var allocationExpenseAccountTagOptionValue = expenseAccountTag.Value?.ToString();
                                var allocationExpenseAccountEntity = aplosExpenseAccounts.FindMatchingEntity(allocationExpenseAccountTagOptionValue, allocationExpenseAccountTagOptionName, ':', log);
                                if (allocationExpenseAccountEntity == null)
                                {
                                    log.LogWarning($"Could not match PEX {aplosAccountCategory} account tag '{expenseAccountTagDefinition?.Name}' option ['{allocationExpenseAccountTagOptionName}' : '{allocationExpenseAccountTagOptionValue}'] with an Aplos expense account entity.");
                                }
                                else
                                {
                                    if (decimal.TryParse(allocationExpenseAccountEntity.Id, out var aplosTransactionAccountNumber))
                                    {
                                        log.LogInformation($"Matched PEX {aplosAccountCategory} account tag '{expenseAccountTagDefinition?.Name}' option ['{allocationExpenseAccountTagOptionName}' : '{allocationExpenseAccountTagOptionValue}'] with Aplos expense account entity '{allocationExpenseAccountEntity.Name}' ({allocationExpenseAccountEntity.Id}).");
                                        pexTagValues.AplosTransactionAccountNumber = aplosTransactionAccountNumber;
                                    }
                                    else
                                    {
                                        log.LogWarning($"Could not parse Aplos {aplosAccountCategory} account id '{allocationExpenseAccountEntity.Id}' into a decimal.");
                                    }
                                }

                                if (mapping.TagMappings?.Any() == true)
                                {
                                    pexTagValues.AplosTagIds = new List<string>();

                                    log.LogInformation($"Processing {mapping.TagMappings.Length} Aplos tag mappings for transaction {transaction.TransactionId} allocation {allocation.Amount:C}");

                                    foreach (var tagMapping in mapping.TagMappings)
                                    {
                                        var allocationTag = allocation.GetTagValue(tagMapping.PexTagId);
                                        if (allocationTag == null || allocationTag.TagId == mapping.PexTaxTagId)
                                        {
                                            log.LogInformation($"No PEX tag {tagMapping.PexTagId} on transaction {transaction.TransactionId} allocation {allocation.Amount:C}");
                                            continue;
                                        }

                                        var allocationTagDefinition = dropdownTags.FirstOrDefault(t => t.Id.Equals(allocationTag.TagId, StringComparison.InvariantCultureIgnoreCase));
                                        var allocationTagOptionValue = allocationTag.Value?.ToString();
                                        var allocationTagOptionName = allocationTag.GetTagOptionName(allocationTagDefinition?.Options);
                                        var allocationTagEntity = aplosTags.FindMatchingEntity(allocationTagOptionValue, allocationTagOptionName, ':', log);
                                        if (allocationTagEntity is null)
                                        {
                                            log.LogWarning($"Could not match PEX tag '{allocationTagDefinition?.Name}' option ['{allocationTagOptionName}' : '{allocationTagOptionValue}'] with an Aplos tag entity.");
                                        }
                                        else
                                        {
                                            log.LogInformation($"Matched PEX tag '{allocationTagDefinition?.Name}' option ['{allocationTagOptionName}' : '{allocationTagOptionValue}'] with Aplos tag entity '{allocationTagEntity.Name}' ({allocationTagEntity.Id}).");
                                            pexTagValues.AplosTagIds.Add(allocationTagEntity.Id);
                                        }
                                    }
                                }
                                else
                                {
                                    log.LogInformation($"No Aplos tag mappings to process.");
                                }

                                var taxTag = allocation.GetTagValue(mapping.PexTaxTagId);
                                pexTagValues.AplosTaxTagId = taxTag?.Value?.ToString();
                            }
                            else
                            {
                                log.LogInformation($"Business does not have tags, or tag are NOT enabled for the account. Using default fund id '{mapping.DefaultAplosFundId}' and default transaction account number '{mapping.DefaultAplosTransactionAccountNumber}'.");
                                pexTagValues.AplosFundId = mapping.DefaultAplosFundId;
                                pexTagValues.AplosTransactionAccountNumber = mapping.DefaultAplosTransactionAccountNumber;
                            }

                            if (pexTagValues.AplosFundId == default || aplosFunds.All(ec => ec.Id != pexTagValues.AplosFundId.ToString()))
                            {
                                syncIneligibilityReason = $"Transaction {transaction.TransactionId}: {nameof(pexTagValues.AplosFundId)} '{pexTagValues.AplosFundId}' not valid for {aplosFunds.Count} funds found in Aplos";
                                break;
                            }

                            if (pexTagValues.AplosTransactionAccountNumber == default || aplosExpenseAccounts.All(ec => decimal.TryParse(ec.Id, out var accountNumber) && accountNumber != pexTagValues.AplosTransactionAccountNumber))
                            {
                                syncIneligibilityReason = $"Transaction {transaction.TransactionId}: {nameof(pexTagValues.AplosTransactionAccountNumber)} '{pexTagValues.AplosTransactionAccountNumber}' not valid for {aplosExpenseAccounts.Count} accounts found in Aplos";
                                break;
                            }

                            allocationDetails.Add((allocation, pexTagValues));
                        }

                        if (!string.IsNullOrEmpty(syncIneligibilityReason))
                        {
                            log.LogWarning(syncIneligibilityReason);
                            continue;
                        }

                        log.LogInformation($"Starting sync for transaction {transaction.TransactionId}");

                        var transactionSyncResult = TransactionSyncResult.Failed;
                        try
                        {
                            var cardholderDetails = await GetCardholderDetails(mapping, transaction.AcctId, log, cancellationToken);
                            transactionSyncResult = await SyncTransaction(
                                allocationDetails,
                                mapping,
                                transaction,
                                cardholderDetails,
                                cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, $"Exception syncing transaction {transaction.TransactionId}.");
                        }

                        if (transactionSyncResult != TransactionSyncResult.NotEligible)
                        {
                            log.LogWarning($"Sync not eligible for transaction {transaction.TransactionId}");
                            eligibleCount++;
                        }
                        if (transactionSyncResult == TransactionSyncResult.Success)
                        {
                            syncCount++;
                            log.LogInformation($"Synced transaction {transaction.TransactionId}");
                            var syncedNoteText = $"{PexCardConst.SyncedWithAplosNote} on {DateTime.UtcNow.ToEst():MM/dd/yyyy h:mm tt}";
                            await _pexApiClient.AddTransactionNote(mapping.PEXExternalAPIToken, transaction, syncedNoteText, cancellationToken);
                        }
                        else if (transactionSyncResult == TransactionSyncResult.Failed)
                        {
                            log.LogWarning($"Failed to sync transaction {transaction.TransactionId}");
                            failureCount++;
                        }
                    }
                }

                var syncNote = failureCount == 0 ? string.Empty : $"Failed to sync {failureCount} transactions from PEX.";
                SyncStatus syncStatus;
                if (syncCount == 0 && eligibleCount > 0 && failureCount > 0)
                {
                    syncStatus = SyncStatus.Failed;
                }
                else
                {
                    syncStatus = syncCount == eligibleCount && failureCount == 0
                        ? SyncStatus.Success
                        : SyncStatus.Partial;
                }
                var result = new SyncResultModel
                {
                    PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                    SyncType = "Transactions",
                    SyncStatus = syncStatus.ToString(),
                    SyncedRecords = syncCount,
                    SyncNotes = syncNote
                };
                await _resultStorage.CreateAsync(result, cancellationToken);
            }

            return allFees;
        }

        private static string GetAplosAccountCategory(FundingSource fundingSource)
        {
            var aplosAccountCategory = fundingSource == FundingSource.Credit
                ? AplosApiClient.APLOS_ACCOUNT_CATEGORY_LIABILITY
                : AplosApiClient.APLOS_ACCOUNT_CATEGORY_EXPENSE;
            return aplosAccountCategory;
        }

        private static List<TransactionModel> FilterTransactions(Pex2AplosMappingModel mapping, CardholderTransactions transactions)
        {
            if (mapping.PEXFundingSource == FundingSource.Credit)
            {
                // TODO Move to pex-sdk-dotnet TransactionsListExtension ?
                var result = transactions?.Where(t =>
                        (string.IsNullOrEmpty(PexCardConst.SyncedWithAplosNote) ||
                         !(t.TransactionNotes?.Exists(n => n.NoteText?.ToLower().Contains(PexCardConst.SyncedWithAplosNote.ToLower()) == true) ?? false)) &&
                        (!mapping.SyncApprovedOnly || string.Equals(t.MetadataApprovalStatus, "Approved", StringComparison.InvariantCultureIgnoreCase)))
                    .ToList();

                return result ?? new List<TransactionModel>();
            }

            return transactions.SelectTransactionsToSync(mapping.SyncApprovedOnly, PexCardConst.SyncedWithAplosNote);
        }

        private async Task SyncBusinessAccountTransactions(
            ILogger log,
            Pex2AplosMappingModel mapping,
            DateTime utcNow,
            List<TransactionModel> additionalFeeTransactions,
            CancellationToken cancellationToken)
        {
            if (!mapping.SyncTransfers && !mapping.SyncPexFees) return;

            var endDate = utcNow.AddDays(1).ToEst();

            var oneYearAgo = DateTime.Today.AddYears(-1).ToEst();
            var startDate = mapping.EarliestTransactionDateToSync.ToEst();

            startDate = startDate > oneYearAgo ? startDate : oneYearAgo;

            var dateRangeBatches = GetDateRangeBatches(new DateRange(startDate, endDate), 28);

            var aplosTransactions = await GetTransactions(mapping, startDate, cancellationToken);

            foreach (var dateRangeBatch in dateRangeBatches)
            {
                log.LogInformation($"Getting business transactions for business {mapping.PEXBusinessAcctId} from {dateRangeBatch.Start} to {dateRangeBatch.End}");

                var businessAccountTransactions = await _pexApiClient.GetBusinessAccountTransactions(mapping.PEXExternalAPIToken, dateRangeBatch.Start, dateRangeBatch.End, token: cancellationToken);

                await SyncTransfers(log, mapping, businessAccountTransactions, aplosTransactions, cancellationToken);

                var additionalFeeTransactionsInRange = additionalFeeTransactions
                    .Where(f => f.TransactionTime.Date >= dateRangeBatch.Start && f.TransactionTime.Date < dateRangeBatch.End)
                    .ToList();

                await SyncPexFees(log, mapping, businessAccountTransactions, aplosTransactions, additionalFeeTransactionsInRange, cancellationToken);
            }
        }

        private async Task SyncTransfers(
            ILogger log,
            Pex2AplosMappingModel model,
            BusinessAccountTransactions businessAccountTransactions,
            List<AplosApiTransactionDetail> aplosTransactions,
            CancellationToken cancellationToken)
        {
            if (!model.SyncTransfers) return;

            var transactions = businessAccountTransactions.SelectBusinessAccountTransfers();
            log.LogInformation($"Syncing {transactions.Count} transfers for business: {model.PEXBusinessAcctId}");

            var transactionsToSync = transactions
                .Where(t => !WasPexTransactionSyncedToAplos(aplosTransactions, t.TransactionId.ToString()))
                .ToList();

            var allocationMapping = await _pexApiClient.GetTagAllocations(model.PEXExternalAPIToken, transactionsToSync, cancellationToken);

            var syncCount = 0;
            var failureCount = 0;
            foreach (var transaction in transactionsToSync)
            {
                using (log.BeginScope(GetLoggingScopeForTransaction(transaction)))
                {
                    if (!allocationMapping.TryGetValue(transaction.TransactionId, out var allocations))
                    {
                        log.LogWarning($"Transaction {transaction.TransactionId} doesn't have an associated allocation. Skipping");
                        continue;
                    }

                    var allocationDetails = new List<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)>();
                    foreach (var allocation in allocations)
                    {
                        //We currently don't support tags on business transactions.
                        //If we add support, we just need to create a setting for mapping and find the value from the allocations based on the tagId.
                        var pexTagValues = new PexTagValuesModel
                        {
                            AplosRegisterAccountNumber = model.AplosRegisterAccountNumber,
                            AplosContactId = model.TransfersAplosContactId,
                            AplosFundId = model.TransfersAplosFundId,
                            AplosTransactionAccountNumber = model.TransfersAplosTransactionAccountNumber
                        };

                        allocationDetails.Add((allocation, pexTagValues));
                    }

                    log.LogInformation($"Starting sync for transfer {transaction.TransactionId}");
                    var transactionSyncResult = TransactionSyncResult.Failed;
                    try
                    {
                        transactionSyncResult = await SyncTransaction(
                            allocationDetails,
                            model,
                            transaction,
                            null,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, $"Exception syncing transfer {transaction.TransactionId}.");
                    }

                    if (transactionSyncResult == TransactionSyncResult.Success)
                    {
                        syncCount++;
                        log.LogInformation($"Synced transfer {transaction.TransactionId} with Aplos");
                    }
                    else if (transactionSyncResult == TransactionSyncResult.Failed)
                    {
                        failureCount++;
                    }
                }

                var syncNote = failureCount == 0 ? string.Empty : $"Failed to sync {failureCount} transfers from PEX.";
                SyncStatus syncStatus;
                if (syncCount == 0 && failureCount > 0)
                {
                    syncStatus = SyncStatus.Failed;
                }
                else
                {
                    syncStatus = failureCount == 0 ? SyncStatus.Success : SyncStatus.Partial;
                }
                var result = new SyncResultModel
                {
                    PEXBusinessAcctId = model.PEXBusinessAcctId,
                    SyncType = "Transfers",
                    SyncStatus = syncStatus.ToString(),
                    SyncedRecords = syncCount,
                    SyncNotes = syncNote
                };
                await _resultStorage.CreateAsync(result, cancellationToken);
            }
        }

        private async Task SyncPexFees(
            ILogger log,
            Pex2AplosMappingModel model,
            BusinessAccountTransactions businessAccountTransactions,
            List<AplosApiTransactionDetail> aplosTransactions,
            List<TransactionModel> additionalFeeTransactions,
            CancellationToken cancellationToken)
        {
            if (!model.SyncPexFees) return;

            var transactions = businessAccountTransactions.SelectBusinessAccountFees();
            if (additionalFeeTransactions != null)
            {
                transactions.AddRange(additionalFeeTransactions);
            }
            log.LogInformation($"Syncing {transactions.Count} PEX account fees for business: {model.PEXBusinessAcctId}");

            var transactionsToSync = transactions
                .Where(t => !WasPexTransactionSyncedToAplos(aplosTransactions, t.TransactionId.ToString()))
                .ToList();

            var allocationMapping = await _pexApiClient.GetTagAllocations(model.PEXExternalAPIToken, transactionsToSync, cancellationToken);

            var syncCount = 0;
            var failureCount = 0;
            foreach (var transaction in transactionsToSync)
            {
                using (log.BeginScope(GetLoggingScopeForTransaction(transaction)))
                {
                    if (!allocationMapping.TryGetValue(transaction.TransactionId, out var allocations))
                    {
                        log.LogWarning($"Transaction {transaction.TransactionId} doesn't have an associated allocation. Skipping");
                        continue;
                    }

                    var allocationDetails = new List<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)>();
                    foreach (var allocation in allocations)
                    {
                        //We currently don't support tags on business transactions.
                        //If we add support, we just need to create a setting for mapping and find the value from the allocations based on the tagId.
                        var pexTagValues = new PexTagValuesModel
                        {
                            AplosRegisterAccountNumber = model.AplosRegisterAccountNumber,
                            AplosContactId = model.PexFeesAplosContactId,
                            AplosFundId = model.PexFeesAplosFundId,
                            AplosTaxTagId = model.PexFeesAplosTaxTagId,
                            AplosTransactionAccountNumber = model.PexFeesAplosTransactionAccountNumber
                        };

                        allocationDetails.Add((allocation, pexTagValues));
                    }

                    log.LogInformation($"Starting sync for PEX account fee {transaction.TransactionId}");
                    var transactionSyncResult = TransactionSyncResult.Failed;
                    try
                    {
                        transactionSyncResult = await SyncTransaction(
                            allocationDetails,
                            model,
                            transaction,
                            null,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, $"Exception syncing PEX account fee {transaction.TransactionId}.");
                    }

                    if (transactionSyncResult == TransactionSyncResult.Success)
                    {
                        syncCount++;
                        log.LogInformation($"Synced PEX account fee {transaction.TransactionId} with Aplos");
                    }
                    else if (transactionSyncResult == TransactionSyncResult.Failed)
                    {
                        failureCount++;
                    }
                }
            }

            var syncNote = failureCount == 0 ? string.Empty : $"Failed to sync {failureCount} PEX account fees from PEX.";
            SyncStatus syncStatus;
            if (syncCount == 0 && failureCount > 0)
            {
                syncStatus = SyncStatus.Failed;
            }
            else
            {
                syncStatus = failureCount == 0 ? SyncStatus.Success : SyncStatus.Partial;
            }
            var result = new SyncResultModel
            {
                PEXBusinessAcctId = model.PEXBusinessAcctId,
                SyncType = "PEX Account Fees",
                SyncStatus = syncStatus.ToString(),
                SyncedRecords = syncCount,
                SyncNotes = syncNote
            };
            await _resultStorage.CreateAsync(result, cancellationToken);
        }

        public bool WasPexTransactionSyncedToAplos(IEnumerable<AplosApiTransactionDetail> aplosTransactions, string pexTransactionId)
        {
            return aplosTransactions.Any(aplosTransaction =>
                    (!string.IsNullOrEmpty(aplosTransaction.Note) && aplosTransaction.Note.Contains(pexTransactionId))
                || (!string.IsNullOrEmpty(aplosTransaction.Memo) && aplosTransaction.Memo.Contains(pexTransactionId)));
        }

        private readonly ConcurrentDictionary<int, CardholderDetailsModel> _cardholderDetailsCache =
            new ConcurrentDictionary<int, CardholderDetailsModel>();

        private async Task<CardholderDetailsModel> GetCardholderDetails(
            Pex2AplosMappingModel mapping,
            int cardholderAccountId,
            ILogger log,
            CancellationToken cancellationToken)
        {
            if (_cardholderDetailsCache.ContainsKey(cardholderAccountId) &&
                _cardholderDetailsCache.TryGetValue(cardholderAccountId, out var cardholderDetails))
            {
                return cardholderDetails;
            }

            CardholderDetailsModel result = null;
            try
            {
                result = await _pexApiClient.GetCardholderDetails(mapping.PEXExternalAPIToken, cardholderAccountId, cancellationToken);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Exception getting account details for cardholder {cardholderAccountId}");
            }

            _cardholderDetailsCache.TryAdd(cardholderAccountId, result);

            return result;
        }

        public async Task<IEnumerable<PexAplosApiObject>> GetAplosTagCategories(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetTags(cancellationToken);

            return _aplosIntegrationMappingService.Map(aplosApiResponse);
        }

        public async Task<IEnumerable<PexAplosApiObject>> GetAplosTaxTagCategories(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetTaxTags(cancellationToken);

            return _aplosIntegrationMappingService.Map(aplosApiResponse);
        }

        private static IDictionary<string, object> GetLoggingScopeForSync(Pex2AplosMappingModel mapping)
        {
            if (mapping is null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }

            return new Dictionary<string, object>
            {
                ["BusinessAccountId"] = mapping.PEXBusinessAcctId,
                ["SyncSessionId"] = Guid.NewGuid(),
            };
        }

        private static IDictionary<string, object> GetLoggingScopeForTransaction(TransactionModel transaction)
        {
            if (transaction is null)
            {
                throw new ArgumentNullException(nameof(transaction));
            }

            return new Dictionary<string, object>
            {
                ["CardholderAccountId"] = transaction.AcctId,
                ["TransactionId"] = transaction.TransactionId,
                ["TransactionDescription"] = transaction.Description,
                ["TransactionAmount"] = transaction.TransactionAmount,
                ["TransactionTime"] = transaction.TransactionTime,
                ["SettlementTime"] = transaction.SettlementTime,
            };
        }

        private IEnumerable<DateRange> GetDateRangeBatches(DateRange dateRange, int batchSizeDays)
        {
            if (batchSizeDays <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSizeDays), "Must be greater than zero.");
            }

            var start = dateRange.Start;
            DateTime? end;

            do
            {
                end = start.AddDays(batchSizeDays).ToEndOfDay();

                if (end > dateRange.End)
                {
                    end = dateRange.End;
                }

                yield return new DateRange(start, end.Value);

                start = end.Value.AddDays(1).ToStartOfDay();
            }
            while (end < dateRange.End);
        }

        #endregion
    }
}
