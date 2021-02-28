using Aplos.Api.Client;
using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using AplosConnector.Common.Const;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Extensions;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Core.Storages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        public async Task<Pex2AplosMappingModel> EnsureMappingInstalled(PexOAuthSessionModel session)
        {
            var mapping = await _mappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId);
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

                await _mappingStorage.CreateAsync(mapping);
            }

            await EnsurePartnerInfoPopulated(mapping);

            return mapping;
        }

        public async Task EnsurePartnerInfoPopulated(Pex2AplosMappingModel mapping)
        {
            bool isChanged = false;

            if (string.IsNullOrWhiteSpace(mapping.AplosAccountId))
            {
                PartnerModel parterInfo = await _pexApiClient.GetPartner(mapping.PEXExternalAPIToken);
                mapping.AplosAccountId = parterInfo.PartnerBusinessId;
                isChanged |= !string.IsNullOrWhiteSpace(mapping.AplosAccountId);
            }

            if (_appSettings.EnforceAplosPartnerVerification && mapping.AplosAuthenticationMode == AplosAuthenticationMode.ClientAuthentication)
            {
                mapping.AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication;
                isChanged |= true;
            }

            if (!string.IsNullOrWhiteSpace(mapping.AplosAccountId) && !mapping.AplosPartnerVerified)
            {
                IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping, AplosAuthenticationMode.PartnerAuthentication);

                try
                {
                    AplosApiPartnerVerificationResponse aplosResponse = await aplosApiClient.GetPartnerVerification();
                    mapping.AplosPartnerVerified = aplosResponse.Data.PartnerVerification.Authorized;
                    isChanged |= mapping.AplosPartnerVerified;
                }
                catch (AplosApiException ex) when (ex.AplosApiError.Status == StatusCodes.Status422UnprocessableEntity)
                {
                    //Expected if they aren't verified yet
                }
            }

            if (!string.IsNullOrWhiteSpace(mapping.AplosAccountId) && mapping.AplosPartnerVerified && mapping.AplosAuthenticationMode == AplosAuthenticationMode.ClientAuthentication)
            {
                mapping.AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication;
                isChanged |= true;
            }

            if (isChanged)
            {
                await _mappingStorage.UpdateAsync(mapping);
            }
        }

        public IAplosApiClient MakeAplosApiClient(Pex2AplosMappingModel mapping, AplosAuthenticationMode? overrideAuthenticationMode = null)
        {
            string aplosAccountId;
            string aplosClientId;
            string aplosPrivateKey;

            AplosAuthenticationMode authenticationMode = overrideAuthenticationMode ?? mapping.AplosAuthenticationMode;
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
                async (auth, logger) =>
                {
                    try
                    {
                        mapping.AplosAccessToken = auth.AplosAccessToken;
                        mapping.AplosAccessTokenExpiresAt = auth.AplosAccessTokenExpiresAt;
                        await _mappingStorage.UpdateAsync(mapping);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, $"Failed to update settings in storage. {ex.Message}");
                        throw;
                    }
                });
        }

        public async Task<PexAplosApiObject> GetAplosContact(Pex2AplosMappingModel mapping, int aplosContactId)
        {
            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            AplosApiContactResponse aplosApiResponse = await aplosApiClient.GetContact(aplosContactId);

            return _aplosIntegrationMappingService.Map(aplosApiResponse?.Data?.Contact);
        }

        public async Task<IEnumerable<PexAplosApiObject>> GetAplosContacts(Pex2AplosMappingModel mapping)
        {
            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetContacts();

            return _aplosIntegrationMappingService.Map(aplosApiResponse);
        }

        public async Task<PexAplosApiObject> GetAplosFund(Pex2AplosMappingModel mapping, int aplosFundId)
        {
            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            AplosApiFundResponse aplosApiResponse = await aplosApiClient.GetFund(aplosFundId);

            return _aplosIntegrationMappingService.Map(aplosApiResponse?.Data?.Fund);
        }

        public async Task<IEnumerable<PexAplosApiObject>> GetAplosFunds(Pex2AplosMappingModel mapping)
        {
            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetFunds();

            return _aplosIntegrationMappingService.Map(aplosApiResponse);
        }

        public async Task<PexAplosApiObject> GetAplosAccount(Pex2AplosMappingModel mapping, decimal aplosAccountNumber)
        {
            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            AplosApiAccountResponse aplosApiResponse = await aplosApiClient.GetAccount(aplosAccountNumber);

            return _aplosIntegrationMappingService.Map(aplosApiResponse?.Data?.Account);
        }

        public async Task<IEnumerable<PexAplosApiObject>> GetAplosExpenseAccounts(Pex2AplosMappingModel mapping, string aplosAccountCategory = null)
        {
            return await GetAplosAccounts(mapping, AplosApiClient.APLOS_ACCOUNT_CATEGORY_EXPENSE);
        }

        public async Task<IEnumerable<PexAplosApiObject>> GetAplosAccounts(Pex2AplosMappingModel mapping, string aplosAccountCategory = null)
        {
            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetAccounts(aplosAccountCategory);

            var mappedAccounts = _aplosIntegrationMappingService.Map(aplosApiResponse);
            return DedupeAplosAccounts(mappedAccounts);
        }

        public IEnumerable<PexAplosApiObject> DedupeAplosAccounts(IEnumerable<PexAplosApiObject> aplosAccounts)
        {
            var uniqueAccounts = new Dictionary<string, PexAplosApiObject>(aplosAccounts.Count());
            foreach (var account in aplosAccounts)
            {
                string accountName = account.Name;
                if (uniqueAccounts.TryGetValue(accountName, out PexAplosApiObject existingAccount))
                {
                    existingAccount.Name = DedupeAplosAccountName(existingAccount);
                    accountName = DedupeAplosAccountName(account);
                }

                uniqueAccounts.Add(accountName, account);
            }

            return uniqueAccounts.Values;

            string DedupeAplosAccountName(PexAplosApiObject account)
            {
                return $"{account.Name} ({account.Id})";
            }
        }

        public async Task<List<AplosApiTransactionDetail>> GetTransactions(Pex2AplosMappingModel mapping, DateTime startDate)
        {
            var aplosApiClient = MakeAplosApiClient(mapping);
            var response = await aplosApiClient.GetTransactions(startDate);

            return response;
        }

        public async Task<TransactionSyncResult> SyncTransaction(
            IEnumerable<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)> allocationDetails,
            Pex2AplosMappingModel mapping,
            TransactionModel transaction,
            CardholderDetailsModel cardholderDetails)
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

            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            await aplosApiClient.CreateTransaction(aplosTransaction);

            return TransactionSyncResult.Success;
        }

        public async Task<string> GetAplosAccessToken(Pex2AplosMappingModel mapping)
        {
            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            return await aplosApiClient.GetAplosAccessToken();
        }

        public async Task<bool> ValidateAplosApiCredentials(Pex2AplosMappingModel mapping)
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

            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);

            var canObtainAccessToken = await aplosApiClient.GetAndValidateAplosAccessToken();
            return canObtainAccessToken;
        }

        public async Task Sync(Pex2AplosMappingModel mapping, ILogger log)
        {
            log.LogInformation("C# Queue trigger function processing.");

            if (mapping.PEXBusinessAcctId == default)
            {
                log.LogWarning($"C# Queue trigger function completed. Business account ID is {mapping.PEXBusinessAcctId}");
                return;
            }

            //Let's refresh Aplos API tokens before sync start and interrupt sync processor in case of invalidity
            string aplosAccessToken = await GetAplosAccessToken(mapping);
            if (string.IsNullOrEmpty(aplosAccessToken))
            {
                log.LogCritical(
                    $"Integration for business {mapping.PEXBusinessAcctId} is not working. access API token is invalid");
                return;
            }

            await EnsurePartnerInfoPopulated(mapping);

            var utcNow = DateTime.UtcNow;

            try
            {
                await SyncTransactions(log, mapping, utcNow);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, $"Exception during transactions sync for business: {mapping.PEXBusinessAcctId}");
            }

            try
            {
                await SyncBusinessAccountTransactions(log, mapping, utcNow);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, $"Exception during business account transactions sync for business: {mapping.PEXBusinessAcctId}.");
            }

            try
            {
                await SyncFundsToPex(log, mapping);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, $"Exception during {nameof(SyncFundsToPex)} for business: {mapping.PEXBusinessAcctId}.");
            }

            try
            {
                await SyncExpenseAccountsToPex(log, mapping);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, $"Exception during {nameof(SyncExpenseAccountsToPex)} for business: {mapping.PEXBusinessAcctId}.");
            }

            try
            {
                await SyncAplosTagsToPex(log, mapping);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, $"Exception during {nameof(SyncAplosTagsToPex)} for business: {mapping.PEXBusinessAcctId}.");
            }

            mapping.LastSyncUtc = utcNow;
            await _mappingStorage.UpdateAsync(mapping);

            log.LogInformation("C# Queue trigger function completed.");
        }

        private async Task SyncAplosTagsToPex(ILogger log, Pex2AplosMappingModel mapping)
        {
            if (mapping.TagMappings == null) return;

            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            List<AplosApiTagCategoryDetail> aplosTagCategories = await aplosApiClient.GetTags();

            foreach (TagMappingModel tagMapping in mapping.TagMappings)
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

                TagDropdownDetailsModel pexTag = await _pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken, tagMapping.PexTagId);
                if (pexTag == null)
                {
                    log.LogWarning($"{nameof(tagMapping.PexTagId)} is unavailable in business: {mapping.PEXBusinessAcctId}");
                    continue;
                }

                log.LogInformation($"Syncing tags from {nameof(tagMapping.AplosTagId)} '{tagMapping.AplosTagId} / {aplosTagCategory.Name}' to {nameof(tagMapping.PexTagId)} '{tagMapping.PexTagId} / {pexTag.Name}' for business: {mapping.PEXBusinessAcctId}");

                IEnumerable<AplosApiTagDetail> flattenedAplosTags = GetFlattenedAplosTagValues(aplosTagCategory);
                IEnumerable<PexAplosApiObject> aplosTagsToSync = _aplosIntegrationMappingService.Map(flattenedAplosTags);

                pexTag.InitTagOptions(aplosTagsToSync, out var syncCount, out var removalCount);
                await _pexApiClient.UpdateDropdownTag(mapping.PEXExternalAPIToken, pexTag.Id, pexTag);

                var removalNote = removalCount == 0 ? string.Empty : $"Disabled {removalCount} tag options from PEX.";
                var result = new SyncResultModel
                {
                    PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                    SyncType = $"Tag Values ({aplosTagCategory.Name})",
                    SyncStatus = SyncStatus.Success.ToString(),
                    SyncedRecords = syncCount,
                    SyncNotes = removalNote,
                };
                await _resultStorage.CreateAsync(result);
            }
        }

        private async Task<IEnumerable<PexAplosApiObject>> GetFlattenedAplosTagValues(Pex2AplosMappingModel mapping)
        {
            var tagValues = new List<AplosApiTagDetail>();

            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            foreach (var tagCategory in await aplosApiClient.GetTags())
            {
                var categoryTagValues = GetFlattenedAplosTagValues(tagCategory);
                tagValues.AddRange(categoryTagValues);
            }

            return _aplosIntegrationMappingService.Map(tagValues);
        }

        public IEnumerable<AplosApiTagDetail> GetFlattenedAplosTagValues(AplosApiTagCategoryDetail aplosTagCategory)
        {
            var tagValues = new List<AplosApiTagDetail>();

            if (aplosTagCategory.TagGroups != null)
            {
                foreach (var tagGroup in aplosTagCategory.TagGroups)
                {
                    var groupTagValues = GetFlattenedAplosTagValues(tagGroup);
                    tagValues.AddRange(groupTagValues);
                }
            }

            return tagValues;
        }

        private IEnumerable<AplosApiTagDetail> GetFlattenedAplosTagValues(AplosApiTagGroupDetail aplosTagGroup)
        {
            var tagValues = new List<AplosApiTagDetail>();

            if (aplosTagGroup.Tags != null)
            {
                foreach (var tagValue in aplosTagGroup.Tags)
                {
                    var groupTagValues = GetFlattenedAplosTagValues(tagValue);
                    tagValues.AddRange(groupTagValues);
                }
            }

            return tagValues;
        }

        private IEnumerable<AplosApiTagDetail> GetFlattenedAplosTagValues(AplosApiTagDetail aplosTagValue)
        {
            var tagValues = new List<AplosApiTagDetail>();
            tagValues.Add(aplosTagValue);

            if (aplosTagValue.SubTags != null)
            {
                foreach (var subTag in aplosTagValue.SubTags)
                {
                    var subTagValues = GetFlattenedAplosTagValues(subTag);
                    tagValues.AddRange(subTagValues);
                }
            }

            return tagValues;
        }

        #region Private methods

        private async Task SyncFundsToPex(
            ILogger log,
            Pex2AplosMappingModel mapping)
        {
            if (!(mapping.SyncTags && mapping.SyncFundsToPex)) return;

            if (string.IsNullOrEmpty(mapping.PexFundsTagId))
            {
                log.LogWarning($"{nameof(mapping.PexFundsTagId)} is not specified for business: {mapping.PEXBusinessAcctId}");
                return;
            }

            log.LogInformation($"Syncing funds for business: {mapping.PEXBusinessAcctId}");

            var fundsTag = await _pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken, mapping.PexFundsTagId);
            if (fundsTag == null)
            {
                log.LogWarning($"{nameof(mapping.PexFundsTagId)} is unavailable in business: {mapping.PEXBusinessAcctId}");
                return;
            }

            var aplosFunds = await GetAplosFunds(mapping);
            fundsTag.InitTagOptions(aplosFunds, out var syncCount, out var removalCount);
            await _pexApiClient.UpdateDropdownTag(mapping.PEXExternalAPIToken, fundsTag.Id, fundsTag);

            var removalNote = removalCount == 0 ? string.Empty : $"Disabled {removalCount} tag options from PEX.";
            var result = new SyncResultModel
            {
                PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                SyncType = "Tag Values (Funds)",
                SyncStatus = SyncStatus.Success.ToString(),
                SyncedRecords = syncCount,
                SyncNotes = removalNote,
            };
            await _resultStorage.CreateAsync(result);
        }

        private async Task SyncExpenseAccountsToPex(
            ILogger log,
            Pex2AplosMappingModel mapping)
        {
            if (!(mapping.SyncTags && mapping.ExpenseAccountMappings != null && mapping.ExpenseAccountMappings.Any())) return;

            if (mapping.ExpenseAccountMappings == null || !mapping.ExpenseAccountMappings.Any())
            {
                log.LogWarning($"{nameof(mapping.ExpenseAccountMappings)} is not specified for business: {mapping.PEXBusinessAcctId}");
                return;
            }

            log.LogInformation($"Syncing accounts for business: {mapping.PEXBusinessAcctId}");

            var aplosAccounts = await GetAplosExpenseAccounts(mapping);

            foreach (ExpenseAccountMappingModel expenseAccountMapping in mapping.ExpenseAccountMappings)
            {
                await SyncExpenseAccount(
                    log,
                    mapping,
                    expenseAccountMapping,
                    aplosAccounts);
            }
        }

        private async Task SyncExpenseAccount(
            ILogger log,
            Pex2AplosMappingModel model,
            ExpenseAccountMappingModel expenseAccountMapping,
            IEnumerable<PexAplosApiObject> accounts)
        {
            if (!expenseAccountMapping.SyncExpenseAccounts)
            {
                return;
            }

            var accountsTag =
                await _pexApiClient.GetDropdownTag(model.PEXExternalAPIToken, expenseAccountMapping.ExpenseAccountsPexTagId);
            if (accountsTag == null)
            {
                log.LogWarning($"Expense accounts tag (Id '{expenseAccountMapping.ExpenseAccountsPexTagId}' is unavailable in business: {model.PEXBusinessAcctId}");
                return;
            }

            accountsTag.InitTagOptions(accounts, out var syncCount, out var removalCount);
            await _pexApiClient.UpdateDropdownTag(model.PEXExternalAPIToken, accountsTag.Id, accountsTag);

            var removalNote = removalCount == 0 ? string.Empty : $"Disabled {removalCount} tag options from PEX.";
            var result = new SyncResultModel
            {
                PEXBusinessAcctId = model.PEXBusinessAcctId,
                SyncType = "Tag Values (Accounts)",
                SyncStatus = SyncStatus.Success.ToString(),
                SyncedRecords = syncCount,
                SyncNotes = removalNote
            };
            await _resultStorage.CreateAsync(result);
        }

        private async Task SyncTransactions(
            ILogger log,
            Pex2AplosMappingModel mapping,
            DateTime utcNow)
        {
            if (!mapping.SyncTransactions) return;

            //var startDateUtc = utcNow.AddDays(-_syncSettings.SyncTransactionsIntervalDays);
            //var startDate = (startDateUtc < mapping.EarliestTransactionDateToSync
            //        ? mapping.EarliestTransactionDateToSync
            //        : startDateUtc)
            //    .ToEst();

            var startDate = mapping.EarliestTransactionDateToSync.ToEst();

            var endDate = utcNow.AddDays(1).ToEst();
            log.LogInformation($"Getting transactions from {startDate} to {endDate}");

            var allCardholderTransactions = await _pexApiClient.GetAllCardholderTransactions(
                mapping.PEXExternalAPIToken,
                startDate,
                endDate);

            var transactions =
                allCardholderTransactions.SelectTransactionsToSync(mapping.SyncApprovedOnly,
                PexCardConst.SyncedWithAplosNote);

            log.LogInformation($"Syncing {transactions.Count} transactions for business: {mapping.PEXBusinessAcctId}");

            var useTags = await _pexApiClient.IsTagsAvailable(mapping.PEXExternalAPIToken, CustomFieldType.Dropdown);

            var aplosFunds = (await GetAplosFunds(mapping)).ToList();
            var aplosAccounts = (await GetAplosAccounts(mapping)).ToList();
            var aplosTags = (await GetFlattenedAplosTagValues(mapping)).ToList();

            List<TagDropdownDetailsModel> dropdownTags = default;
            if (useTags)
            {
                var dropdownTagTasks = new List<Task<TagDropdownDetailsModel>>
                {
                    _pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken, mapping.PexFundsTagId),
                };
                if (mapping.ExpenseAccountMappings != null)
                {
                    foreach (var expenseAccountMapping in mapping.ExpenseAccountMappings)
                    {
                        dropdownTagTasks.Add(_pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken,
                            expenseAccountMapping.ExpenseAccountsPexTagId));
                    }
                }
                if (mapping.TagMappings != null)
                {
                    foreach (var tagMapping in mapping.TagMappings)
                    {
                        dropdownTagTasks.Add(
                            _pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken, tagMapping.PexTagId));
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

            Dictionary<long, List<AllocationTagValue>> allocationMapping = await _pexApiClient.GetTagAllocations(mapping.PEXExternalAPIToken, new CardholderTransactions(transactions));

            var syncCount = 0;
            var failureCount = 0;
            var eligibleCount = 0;
            foreach (var transaction in transactions)
            {
                log.LogInformation($"Processing transaction {transaction.TransactionId}");

                if (!allocationMapping.TryGetValue(transaction.TransactionId, out var allocations))
                {
                    log.LogWarning($"Transaction {transaction.TransactionId} doesn't have an associated allocation. Skipping");
                    continue;
                }

                var allocationDetails = new List<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)>();
                string syncIneligibilityReason = null;

                foreach (AllocationTagValue allocation in allocations)
                {
                    var pexTagValues = new PexTagValuesModel
                    {
                        AplosRegisterAccountNumber = mapping.AplosRegisterAccountNumber,
                        AplosContactId = mapping.DefaultAplosContactId,
                    };

                    if (useTags)
                    {
                        var fundTransactionTag = allocation.GetTagValue(mapping.PexFundsTagId);
                        if (!mapping.SyncFundsToPex)
                        {
                            var fundOptions = dropdownTags.FirstOrDefault(t =>
                                t.Id.Equals(mapping.PexFundsTagId, StringComparison.InvariantCultureIgnoreCase))?.Options;
                            var fundName = fundTransactionTag?.GetTagOptionName(fundOptions);
                            if (int.TryParse(aplosFunds.MatchEntityByName(fundName)?.Id, out var aplosFundId))
                            {
                                pexTagValues.AplosFundId = aplosFundId;
                            }
                        }
                        else if (int.TryParse(fundTransactionTag?.Value.ToString(), out var aplosFundId))
                        {
                            pexTagValues.AplosFundId = aplosFundId;
                        }

                        string expenseAccountTagId = null;
                        TagValueItem expenseAccountTag = null;
                        var areExpenseAccountTagsSynced = false;
                        foreach (var expenseAccountMapping in mapping.ExpenseAccountMappings)
                        {
                            var expenseAccountTransactionTag = allocation.GetTagValue(expenseAccountMapping.ExpenseAccountsPexTagId);
                            if (expenseAccountTransactionTag != null)
                            {
                                expenseAccountTagId = expenseAccountMapping.ExpenseAccountsPexTagId;
                                expenseAccountTag = expenseAccountTransactionTag;
                                areExpenseAccountTagsSynced = expenseAccountMapping.SyncExpenseAccounts;
                                break;
                            }
                        }

                        if (expenseAccountTag == null)
                        {
                            syncIneligibilityReason = $"Transaction {transaction.TransactionId} doesn't have Expense Account tagged. Skipping";
                            break;
                        }

                        string expenseAccountNumberTagValue;
                        if (areExpenseAccountTagsSynced)
                        {
                            expenseAccountNumberTagValue = expenseAccountTag.Value.ToString();
                        }
                        else
                        {
                            var expenseAccountOptions = dropdownTags.FirstOrDefault(t =>
                                t.Id.Equals(expenseAccountTagId,
                                    StringComparison.InvariantCultureIgnoreCase))?.Options;
                            var expenseAccountName = expenseAccountTag.GetTagOptionName(expenseAccountOptions);
                            log.LogInformation($"Attempting to match expense account tag value {expenseAccountName} by name");
                            expenseAccountNumberTagValue = aplosAccounts.MatchEntityByName(expenseAccountName, ':')?.Id;
                        }

                        if (decimal.TryParse(expenseAccountNumberTagValue, out decimal transactionAccountNumber))
                        {
                            pexTagValues.AplosTransactionAccountNumber = transactionAccountNumber;
                        }
                        else
                        {
                            log.LogWarning($"Could not parse {nameof(expenseAccountNumberTagValue)} '{expenseAccountNumberTagValue}' into a decimal.");
                        }

                        if (mapping.TagMappings != null)
                        {
                            pexTagValues.AplosTagIds = new List<string>();
                            foreach (var tagMapping in mapping.TagMappings)
                            {
                                var mappedTagValue = allocation.GetTagValue(tagMapping.PexTagId);
                                if (mappedTagValue != null)
                                {
                                    string aplosTagId = null;
                                    if (tagMapping.SyncToPex)
                                    {
                                        aplosTagId = mappedTagValue.Value.ToString();
                                    }
                                    else
                                    {
                                        var pexTagOptions = dropdownTags.FirstOrDefault(t =>
                                            t.Id.Equals(tagMapping.PexTagId,
                                                StringComparison.InvariantCultureIgnoreCase))?.Options;
                                        var pexTagName = mappedTagValue.GetTagOptionName(pexTagOptions);
                                        log.LogInformation($"Attempting to match mapped tag value {pexTagName} by name");
                                        aplosTagId = aplosTags.MatchEntityByName(pexTagName, ':')?.Id;
                                    }

                                    pexTagValues.AplosTagIds.Add(aplosTagId);
                                }
                            }
                        }
                    }
                    else
                    {
                        pexTagValues.AplosFundId = mapping.DefaultAplosFundId;
                        pexTagValues.AplosTransactionAccountNumber = mapping.DefaultAplosTransactionAccountNumber;
                    }

                    if (pexTagValues.AplosFundId == default || aplosFunds.All(ec => ec.Id != pexTagValues.AplosFundId.ToString()))
                    {
                        syncIneligibilityReason = $"Transaction {transaction.TransactionId}: {nameof(pexTagValues.AplosFundId)} '{pexTagValues.AplosFundId}' not valid for {aplosFunds.Count} funds found in Aplos";
                        break;
                    }

                    if (pexTagValues.AplosTransactionAccountNumber == default || aplosAccounts.All(ec => decimal.TryParse(ec.Id, out decimal accountNumber) && accountNumber != pexTagValues.AplosTransactionAccountNumber))
                    {
                        syncIneligibilityReason = $"Transaction {transaction.TransactionId}: {nameof(pexTagValues.AplosTransactionAccountNumber)} '{pexTagValues.AplosTransactionAccountNumber}' not valid for {aplosAccounts.Count} accounts found in Aplos";
                        break;
                    }

                    allocationDetails.Add((allocation, pexTagValues));
                }

                if (!string.IsNullOrEmpty(syncIneligibilityReason))
                {
                    log.LogInformation(syncIneligibilityReason);
                    continue;
                }

                log.LogInformation($"Starting sync for transaction {transaction.TransactionId}");

                var transactionSyncResult = TransactionSyncResult.Failed;
                try
                {
                    CardholderDetailsModel cardholderDetails = await GetCardholderDetails(mapping, transaction.AcctId, log);
                    transactionSyncResult = await SyncTransaction(
                        allocationDetails,
                        mapping,
                        transaction,
                        cardholderDetails);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, $"Exception syncing transaction {transaction.TransactionId}.");
                }

                if (transactionSyncResult != TransactionSyncResult.NotEligible)
                {
                    eligibleCount++;
                }
                if (transactionSyncResult == TransactionSyncResult.Success)
                {
                    syncCount++;
                    log.LogInformation($"Synced transaction {transaction.TransactionId} with Aplos");
                    var syncedNoteText =
                        $"{PexCardConst.SyncedWithAplosNote} on {DateTime.UtcNow.ToEst():MM/dd/yyyy h:mm tt}";
                    await _pexApiClient.AddTransactionNote(mapping.PEXExternalAPIToken, transaction, syncedNoteText);
                }
                else if (transactionSyncResult == TransactionSyncResult.Failed)
                {
                    failureCount++;
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
            await _resultStorage.CreateAsync(result);
        }

        private async Task SyncBusinessAccountTransactions(
            ILogger log,
            Pex2AplosMappingModel mapping,
            DateTime utcNow)
        {
            if (!mapping.SyncTransfers && !mapping.SyncPexFees) return;

            //var startDateUtc = utcNow.AddDays(-_syncSettings.SyncTransactionsIntervalDays);
            //var startDate = (startDateUtc < mapping.EarliestTransactionDateToSync
            //        ? mapping.EarliestTransactionDateToSync
            //        : startDateUtc)
            //    .ToEst();
            var endDate = utcNow.AddDays(1).ToEst();

            var startDate = mapping.EarliestTransactionDateToSync.ToEst();

            log.LogInformation(
                $"Getting business transactions for business {mapping.PEXBusinessAcctId} from {startDate} to {endDate}");

            var businessAccountTransactions =
                await _pexApiClient.GetBusinessAccountTransactions(mapping.PEXExternalAPIToken, startDate, endDate);

            var aplosTransactions = await GetTransactions(
                mapping,
                startDate);

            await SyncTransfers(log, mapping, businessAccountTransactions, aplosTransactions);
            await SyncPexFees(log, mapping, businessAccountTransactions, aplosTransactions);
        }

        private async Task SyncTransfers(
            ILogger log,
            Pex2AplosMappingModel model,
            BusinessAccountTransactions businessAccountTransactions,
            List<AplosApiTransactionDetail> aplosTransactions)
        {
            if (!model.SyncTransfers) return;

            var transactions = businessAccountTransactions.SelectBusinessAccountTransfers();
            log.LogInformation($"Syncing {transactions.Count} transfers for business: {model.PEXBusinessAcctId}");

            var transactionsToSync = transactions
                .Where(t => !WasPexTransactionSyncedToAplos(aplosTransactions, t.TransactionId.ToString()))
                .ToList();

            Dictionary<long, List<AllocationTagValue>> allocationMapping = await _pexApiClient.GetTagAllocations(model.PEXExternalAPIToken, transactionsToSync);

            var syncCount = 0;
            var failureCount = 0;
            foreach (var transaction in transactionsToSync)
            {
                if (!allocationMapping.TryGetValue(transaction.TransactionId, out var allocations))
                {
                    log.LogWarning($"Transaction {transaction.TransactionId} doesn't have an associated allocation. Skipping");
                    continue;
                }

                var allocationDetails = new List<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)>();
                foreach (AllocationTagValue allocation in allocations)
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
                        null);
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
            await _resultStorage.CreateAsync(result);
        }

        private async Task SyncPexFees(
            ILogger log,
            Pex2AplosMappingModel model,
            BusinessAccountTransactions businessAccountTransactions,
            List<AplosApiTransactionDetail> aplosTransactions)
        {
            if (!model.SyncPexFees) return;

            var transactions = businessAccountTransactions.SelectBusinessAccountFees();
            log.LogInformation($"Syncing {transactions.Count} PEX account fees for business: {model.PEXBusinessAcctId}");

            var transactionsToSync = transactions
                .Where(t => !WasPexTransactionSyncedToAplos(aplosTransactions, t.TransactionId.ToString()))
                .ToList();

            Dictionary<long, List<AllocationTagValue>> allocationMapping = await _pexApiClient.GetTagAllocations(model.PEXExternalAPIToken, transactionsToSync);

            var syncCount = 0;
            var failureCount = 0;
            foreach (var transaction in transactionsToSync)
            {
                if (!allocationMapping.TryGetValue(transaction.TransactionId, out var allocations))
                {
                    log.LogWarning($"Transaction {transaction.TransactionId} doesn't have an associated allocation. Skipping");
                    continue;
                }

                var allocationDetails = new List<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)>();
                foreach (AllocationTagValue allocation in allocations)
                {
                    //We currently don't support tags on business transactions.
                    //If we add support, we just need to create a setting for mapping and find the value from the allocations based on the tagId.
                    var pexTagValues = new PexTagValuesModel
                    {
                        AplosRegisterAccountNumber = model.AplosRegisterAccountNumber,
                        AplosContactId = model.PexFeesAplosContactId,
                        AplosFundId = model.PexFeesAplosFundId,
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
                        null);
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
            await _resultStorage.CreateAsync(result);
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
            ILogger log)
        {
            if (_cardholderDetailsCache.ContainsKey(cardholderAccountId) &&
                _cardholderDetailsCache.TryGetValue(cardholderAccountId, out var cardholderDetails))
            {
                return cardholderDetails;
            }

            CardholderDetailsModel result = null;
            try
            {
                result = await _pexApiClient.GetCardholderDetails(mapping.PEXExternalAPIToken, cardholderAccountId);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Exception getting account details for cardholder {cardholderAccountId}");
            }

            _cardholderDetailsCache.TryAdd(cardholderAccountId, result);

            return result;
        }

        public async Task<IEnumerable<PexAplosApiObject>> GetAplosTagCategories(Pex2AplosMappingModel mapping)
        {
            IAplosApiClient aplosApiClient = MakeAplosApiClient(mapping);
            var aplosApiResponse = await aplosApiClient.GetTags();

            return _aplosIntegrationMappingService.Map(aplosApiResponse);
        }

        #endregion
    }
}
