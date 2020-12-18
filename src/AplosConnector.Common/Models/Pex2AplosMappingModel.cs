using System;

namespace AplosConnector.Common.Models
{
    public class Pex2AplosMappingModel : AplosAccessTokenModel
    {
        public Pex2AplosMappingModel()
        {
            CreatedUtc = DateTime.UtcNow;
        }

        public void UpdateFromSettings(MappingSettingsModel mapping)
        {
            CreatedUtc = mapping.ConnectedOn;
            SyncApprovedOnly = mapping.SyncApprovedOnly;
            SyncTags = mapping.SyncTags;
            SyncTransactions = mapping.SyncTransactions;
            SyncTransfers = mapping.SyncTransfers;
            SyncPexFees = mapping.SyncPexFees;
            LastSyncUtc = mapping.LastSync;
            EarliestTransactionDateToSync = mapping.EarliestTransactionDateToSync;

            AplosClientId = mapping.AplosClientId;
            AplosPrivateKey = mapping.AplosPrivateKey;
            AplosAccessToken = mapping.AplosAccessToken;
            AplosAccessTokenExpiresAt = mapping.AplosAccessTokenExpiresAt;

            AplosRegisterAccountNumber = mapping.AplosRegisterAccountNumber;

            SyncTransactionsCreateContact = mapping.SyncTransactionsCreateContact;
            DefaultAplosContactId = mapping.DefaultAplosContactId;

            DefaultAplosFundId = mapping.DefaultAplosFundId;
            DefaultAplosTransactionAccountNumber = mapping.DefaultAplosTransactionAccountNumber;

            TransfersAplosContactId = mapping.TransfersAplosContactId;
            TransfersAplosFundId = mapping.TransfersAplosFundId;
            TransfersAplosTransactionAccountNumber = mapping.TransfersAplosTransactionAccountNumber;

            PexFeesAplosContactId = mapping.PexFeesAplosContactId;
            PexFeesAplosFundId = mapping.PexFeesAplosFundId;
            PexFeesAplosTransactionAccountNumber = mapping.PexFeesAplosTransactionAccountNumber;

            PexFundsTagId = mapping.PexFundsTagId;
            SyncFundsToPex = mapping.SyncFundsToPex;

            ExpenseAccountMappings = mapping.ExpenseAccountMappings;
            TagMappings = mapping.TagMappings;
        }

        public MappingSettingsModel ToStorageModel()
        {
            return new MappingSettingsModel
            {
                ConnectedOn =  CreatedUtc,
                SyncApprovedOnly = SyncApprovedOnly,
                SyncTags = SyncTags,
                SyncTransactions = SyncTransactions,
                SyncTransfers = SyncTransfers,
                SyncPexFees = SyncPexFees,
                LastSync = LastSyncUtc,
                EarliestTransactionDateToSync = EarliestTransactionDateToSync,

                AplosAccountId = AplosAccountId,
                AplosClientId = AplosClientId,
                AplosPrivateKey = AplosPrivateKey,
                AplosAccessToken = AplosAccessToken,
                AplosAccessTokenExpiresAt = AplosAccessTokenExpiresAt,

                AplosRegisterAccountNumber = AplosRegisterAccountNumber,

                SyncTransactionsCreateContact = SyncTransactionsCreateContact,
                DefaultAplosContactId = DefaultAplosContactId,

                DefaultAplosFundId = DefaultAplosFundId,
                DefaultAplosTransactionAccountNumber = DefaultAplosTransactionAccountNumber,
                PexFundsTagId = PexFundsTagId,

                TransfersAplosContactId = TransfersAplosContactId,
                TransfersAplosFundId = TransfersAplosFundId,
                TransfersAplosTransactionAccountNumber = TransfersAplosTransactionAccountNumber,

                PexFeesAplosContactId = PexFeesAplosContactId,
                PexFeesAplosFundId = PexFeesAplosFundId,
                PexFeesAplosTransactionAccountNumber = PexFeesAplosTransactionAccountNumber,

                SyncFundsToPex = SyncFundsToPex,

                ExpenseAccountMappings = ExpenseAccountMappings,
                TagMappings = TagMappings,
            };
        }

        public string PEXExternalAPIToken { get; set; }
        public int PEXBusinessAcctId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? LastSyncUtc { get; set; }
        public DateTime? LastRenewedUtc { get; set; }
        public bool SyncTags { get; set; }
        public bool SyncTransactions { get; set; }
        public bool SyncTransfers { get; set; }
        public bool SyncPexFees { get; set; }
        public bool SyncApprovedOnly { get; set; }
        public DateTime EarliestTransactionDateToSync { get; set; }
        public string AplosAccountId { get; set; }
        public bool AplosPartnerVerified { get; set; }
        public string AplosClientId { get; set; }
        public string AplosPrivateKey { get; set; }

        public decimal AplosRegisterAccountNumber { get; set; }

        public bool SyncTransactionsCreateContact { get; set; }
        public int DefaultAplosContactId { get; set; }

        public bool SyncFundsToPex { get; set; }
        public string PexFundsTagId { get; set; }
        public int DefaultAplosFundId { get; set; }
        public decimal DefaultAplosTransactionAccountNumber { get; set; }

        public int TransfersAplosContactId { get; set; }
        public int TransfersAplosFundId { get; set; }
        public decimal TransfersAplosTransactionAccountNumber { get; set; }

        public int PexFeesAplosContactId { get; set; }
        public int PexFeesAplosFundId { get; set; }
        public decimal PexFeesAplosTransactionAccountNumber { get; set; }

        public ExpenseAccountMappingModel[] ExpenseAccountMappings { get; set; }
        public TagMappingModel[] TagMappings { get; set; }

        public DateTime GetLastRenewedDateUtc()
        {
            return LastRenewedUtc ?? CreatedUtc;
        }
    }
}
