using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using AplosConnector.Common.Services.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Newtonsoft.Json;
using System.Collections.Generic;
using PexCard.Api.Client.Core.Models;
using System;

namespace AplosConnector.Common.Services
{
    public class StorageMappingService : IStorageMappingService
    {
        private readonly IDataProtector _dataProtector;

        public StorageMappingService(IDataProtectionProvider dataProtectionProvider)
        {
            _dataProtector = dataProtectionProvider.CreateProtector(nameof(StorageMappingService));
        }

        public Pex2AplosMappingEntity Map(Pex2AplosMappingModel model)
        {
            Pex2AplosMappingEntity result = default;
            if (model != null)
            {
                string encryptedAplosClientId = default;
                if (!string.IsNullOrWhiteSpace(model.AplosClientId))
                {
                    encryptedAplosClientId = _dataProtector.Protect(model.AplosClientId);
                }

                string encryptedAplosPrivateKey = default;
                if (!string.IsNullOrWhiteSpace(model.AplosPrivateKey))
                {
                    encryptedAplosPrivateKey = _dataProtector.Protect(model.AplosPrivateKey);
                }

                string encryptedAplosAccessToken = default;
                if (!string.IsNullOrWhiteSpace(model.AplosAccessToken))
                {
                    encryptedAplosAccessToken = _dataProtector.Protect(model.AplosAccessToken);
                }

                result = new Pex2AplosMappingEntity
                {
                    AutomaticSync = model.AutomaticSync,
                    IsSyncing = model.IsSyncing,
                    IsManualSync = false, // always reset to false when saving
                    CreatedUtc = model.CreatedUtc,
                    PEXBusinessAcctId = model.PEXBusinessAcctId,
                    PEXFundingSource = (int)model.PEXFundingSource,
                    LastSyncUtc = model.LastSyncUtc,
                    LastRenewedUtc = model.LastRenewedUtc,
                    PEXExternalAPIToken = model.PEXExternalAPIToken,
                    SyncTags = model.SyncTags,
                    SyncTaxTagToPex = model.SyncTaxTagToPex,
                    SyncTransactions = model.SyncTransactions,
                    SyncTransfers = model.SyncTransfers,
                    SyncInvoices = model.SyncInvoices,
                    SyncPexFees = model.SyncPexFees,
                    SyncRebates = model.SyncRebates,
                    SyncApprovedOnly = model.SyncApprovedOnly,
                    EarliestTransactionDateToSync = model.EarliestTransactionDateToSync,
                    EndDateUtc = model.EndDateUtc,

                    AplosAccountId = model.AplosAccountId,
                    AplosPartnerVerified = model.AplosPartnerVerified,
                    AplosClientId = encryptedAplosClientId,
                    AplosPrivateKey = encryptedAplosPrivateKey,
                    AplosAuthenticationMode = (int)model.AplosAuthenticationMode,

                    AplosAccessToken = encryptedAplosAccessToken,
                    AplosAccessTokenExpiresAt = model.AplosAccessTokenExpiresAt,

                    AplosRegisterAccountNumber = model.AplosRegisterAccountNumber.ToString(),

                    SyncTransactionsCreateContact = model.SyncTransactionsCreateContact,
                    DefaultAplosContactId = model.DefaultAplosContactId,

                    DefaultAplosFundId = model.DefaultAplosFundId,
                    DefaultAplosTransactionAccountNumber = model.DefaultAplosTransactionAccountNumber.ToString(),

                    TransfersAplosContactId = model.TransfersAplosContactId,
                    TransfersAplosFundId = model.TransfersAplosFundId,
                    TransfersAplosTransactionAccountNumber = model.TransfersAplosTransactionAccountNumber.ToString(),

                    PexFeesAplosContactId = model.PexFeesAplosContactId,
                    PexFeesAplosFundId = model.PexFeesAplosFundId,
                    PexFeesAplosTransactionAccountNumber = model.PexFeesAplosTransactionAccountNumber.ToString(),
                    PexFeesAplosTaxTagId = model.PexFeesAplosTaxTagId,

                    PexRebatesAplosContactId = model.PexRebatesAplosContactId,
                    PexRebatesAplosFundId = model.PexRebatesAplosFundId,
                    PexRebatesAplosTransactionAccountNumber = model.PexRebatesAplosTransactionAccountNumber.ToString(),
                    PexRebatesAplosTaxTagId = model.PexRebatesAplosTaxTagId,

                    PexFundsTagId = model.PexFundsTagId,
                    SyncFundsToPex = model.SyncFundsToPex,

                    ExpenseAccountMappings = JsonConvert.SerializeObject(model.ExpenseAccountMappings),
                    TagMappings = JsonConvert.SerializeObject(model.TagMappings),

                    PexTaxTagId = model.PexTaxTagId,

                    PEXEmailAccount = model.PEXEmailAccount,
                    PEXNameAccount = model.PEXNameAccount,

                    SyncTransactionsIntervalDays = model.SyncTransactionsIntervalDays,
                    FetchTransactionsIntervalDays = model.FetchTransactionsIntervalDays,
                    MapVendorCards = model.MapVendorCards,
                    UseNormalizedMerchantNames = model.UseNormalizedMerchantNames,
                    PostDateType = model.PostDateType.ToString(),

                    ExpirationEmailCount = model.ExpirationEmailCount,
                    ExpirationEmailLastDate = model.ExpirationEmailLastDate,
                    IsTokenExpired = model.IsTokenExpired
                };
            }

            return result;
        }

        public IEnumerable<Pex2AplosMappingModel> Map(IEnumerable<Pex2AplosMappingEntity> models)
        {
            List<Pex2AplosMappingModel> result = default;
            if (models != null)
            {
                result = new List<Pex2AplosMappingModel>();
                foreach (Pex2AplosMappingEntity model in models)
                {
                    result.Add(Map(model));
                }
            }

            return result;
        }

        public Pex2AplosMappingModel Map(Pex2AplosMappingEntity model)
        {
            Pex2AplosMappingModel result = default;
            if (model != null)
            {
                string decryptedAplosClientId = default;
                if (!string.IsNullOrWhiteSpace(model.AplosClientId))
                {
                    decryptedAplosClientId = _dataProtector.Unprotect(model.AplosClientId);
                }

                string decryptedAplosPrivateKey = default;
                if (!string.IsNullOrWhiteSpace(model.AplosPrivateKey))
                {
                    decryptedAplosPrivateKey = _dataProtector.Unprotect(model.AplosPrivateKey);
                }

                string decryptedAplosAccessToken = default;
                if (!string.IsNullOrWhiteSpace(model.AplosAccessToken))
                {
                    decryptedAplosAccessToken = _dataProtector.Unprotect(model.AplosAccessToken);
                }

                decimal.TryParse(model.AplosRegisterAccountNumber, out var aplosRegisterAccountNumber);
                decimal.TryParse(model.DefaultAplosTransactionAccountNumber, out var defaultAplosTransactionAccountNumber);
                decimal.TryParse(model.TransfersAplosTransactionAccountNumber, out var transfersAplosTransactionAccountNumber);
                decimal.TryParse(model.PexFeesAplosTransactionAccountNumber, out var pexFeesAplosTransactionAccountNumber);
                decimal.TryParse(model.PexRebatesAplosTransactionAccountNumber, out var pexRebatesAplosTransactionAccountNumber);

                result = new Pex2AplosMappingModel
                {
                    AutomaticSync = model.AutomaticSync,
                    IsSyncing = model.IsSyncing,
                    IsManualSync = model.IsManualSync,
                    CreatedUtc = model.CreatedUtc,
                    PEXBusinessAcctId = model.PEXBusinessAcctId,
                    PEXFundingSource = (FundingSource)model.PEXFundingSource,
                    LastSyncUtc = model.LastSyncUtc,
                    LastRenewedUtc = model.LastRenewedUtc,
                    PEXExternalAPIToken = model.PEXExternalAPIToken,
                    SyncTags = model.SyncTags,
                    SyncTaxTagToPex = model.SyncTaxTagToPex,
                    SyncTransactions = model.SyncTransactions,
                    SyncTransfers = model.SyncTransfers,
                    SyncInvoices = model.SyncInvoices,
                    SyncPexFees = model.SyncPexFees,
                    SyncRebates = model.SyncRebates,
                    SyncApprovedOnly = model.SyncApprovedOnly,
                    EarliestTransactionDateToSync = model.EarliestTransactionDateToSync,
                    EndDateUtc = model.EndDateUtc,

                    AplosAccountId = model.AplosAccountId,
                    AplosPartnerVerified = model.AplosPartnerVerified,
                    AplosClientId = decryptedAplosClientId,
                    AplosPrivateKey = decryptedAplosPrivateKey,
                    AplosAuthenticationMode = (AplosAuthenticationMode)model.AplosAuthenticationMode,

                    AplosAccessToken = decryptedAplosAccessToken,
                    AplosAccessTokenExpiresAt = model.AplosAccessTokenExpiresAt,

                    AplosRegisterAccountNumber = aplosRegisterAccountNumber,

                    SyncTransactionsCreateContact = model.SyncTransactionsCreateContact,
                    DefaultAplosContactId = model.DefaultAplosContactId,

                    DefaultAplosFundId = model.DefaultAplosFundId,
                    DefaultAplosTransactionAccountNumber = defaultAplosTransactionAccountNumber,

                    TransfersAplosContactId = model.TransfersAplosContactId,
                    TransfersAplosFundId = model.TransfersAplosFundId,
                    TransfersAplosTransactionAccountNumber = transfersAplosTransactionAccountNumber,

                    PexFeesAplosContactId = model.PexFeesAplosContactId,
                    PexFeesAplosFundId = model.PexFeesAplosFundId,
                    PexFeesAplosTransactionAccountNumber = pexFeesAplosTransactionAccountNumber,
                    PexFeesAplosTaxTagId = model.PexFeesAplosTaxTagId,

                    PexRebatesAplosContactId = model.PexRebatesAplosContactId,
                    PexRebatesAplosFundId = model.PexRebatesAplosFundId,
                    PexRebatesAplosTransactionAccountNumber = pexRebatesAplosTransactionAccountNumber,
                    PexRebatesAplosTaxTagId = model.PexRebatesAplosTaxTagId,

                    PexFundsTagId = model.PexFundsTagId,
                    SyncFundsToPex = model.SyncFundsToPex,

                    PEXEmailAccount = model.PEXEmailAccount,
                    PEXNameAccount = model.PEXNameAccount,

                    ExpenseAccountMappings = model.ExpenseAccountMappings == null
                        ? null
                        : JsonConvert.DeserializeObject<ExpenseAccountMappingModel[]>(model.ExpenseAccountMappings),

                    TagMappings = model.TagMappings == null
                        ? null
                        : JsonConvert.DeserializeObject<TagMappingModel[]>(model.TagMappings),

                    PexTaxTagId = model.PexTaxTagId,

                    SyncTransactionsIntervalDays = model.SyncTransactionsIntervalDays,
                    FetchTransactionsIntervalDays = model.FetchTransactionsIntervalDays,
                    MapVendorCards = model.MapVendorCards,
                    UseNormalizedMerchantNames = model.UseNormalizedMerchantNames,
                    PostDateType = !string.IsNullOrEmpty(model.PostDateType) ? Enum.Parse<PostDateType>(model.PostDateType) : PostDateType.Transaction,

                    ExpirationEmailCount = model.ExpirationEmailCount,
                    ExpirationEmailLastDate = model.ExpirationEmailLastDate,
                    IsTokenExpired = model.IsTokenExpired
                };
            }

            return result;
        }
    }
}
