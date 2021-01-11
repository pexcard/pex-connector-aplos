using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using AplosConnector.Common.Services.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Newtonsoft.Json;
using System.Collections.Generic;

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
                    CreatedUtc = model.CreatedUtc,
                    PEXBusinessAcctId = model.PEXBusinessAcctId,
                    LastSyncUtc = model.LastSyncUtc,
                    LastRenewedUtc = model.LastRenewedUtc,
                    PEXExternalAPIToken = model.PEXExternalAPIToken,
                    SyncTags = model.SyncTags,
                    SyncTransactions = model.SyncTransactions,
                    SyncTransfers = model.SyncTransfers,
                    SyncPexFees = model.SyncPexFees,
                    SyncApprovedOnly = model.SyncApprovedOnly,
                    EarliestTransactionDateToSync = model.EarliestTransactionDateToSync,

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

                    PexFundsTagId = model.PexFundsTagId,
                    SyncFundsToPex = model.SyncFundsToPex,

                    ExpenseAccountMappings = JsonConvert.SerializeObject(model.ExpenseAccountMappings),
                    TagMappings = JsonConvert.SerializeObject(model.TagMappings),
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

                result = new Pex2AplosMappingModel
                {
                    CreatedUtc = model.CreatedUtc,
                    PEXBusinessAcctId = model.PEXBusinessAcctId,
                    LastSyncUtc = model.LastSyncUtc,
                    LastRenewedUtc = model.LastRenewedUtc,
                    PEXExternalAPIToken = model.PEXExternalAPIToken,
                    SyncTags = model.SyncTags,
                    SyncTransactions = model.SyncTransactions,
                    SyncTransfers = model.SyncTransfers,
                    SyncPexFees = model.SyncPexFees,
                    SyncApprovedOnly = model.SyncApprovedOnly,
                    EarliestTransactionDateToSync = model.EarliestTransactionDateToSync,

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

                    PexFundsTagId = model.PexFundsTagId,
                    SyncFundsToPex = model.SyncFundsToPex,

                    ExpenseAccountMappings = model.ExpenseAccountMappings == null
                        ? null
                        : JsonConvert.DeserializeObject<ExpenseAccountMappingModel[]>(model.ExpenseAccountMappings),

                    TagMappings = model.TagMappings == null
                        ? null
                        : JsonConvert.DeserializeObject<TagMappingModel[]>(model.TagMappings),
                };
            }

            return result;
        }
    }
}
