using System;
using System.Collections.Generic;
using System.Linq;
using PexCard.Api.Client.Core.Models;

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
            AutomaticSync = mapping.AutomaticSync;
            IsSyncing = mapping.IsSyncing;
            IsManualSync = mapping.IsManualSync;
            CreatedUtc = mapping.ConnectedOn;
            SyncApprovedOnly = mapping.SyncApprovedOnly;
            SyncTags = mapping.SyncTags;
            SyncTaxTagToPex = mapping.SyncTaxTagToPex;
            SyncTransactions = mapping.SyncTransactions;
            SyncTransfers = mapping.SyncTransfers;
            SyncPexFees = mapping.SyncPexFees;
            SyncInvoices = mapping.SyncInvoices;
            SyncRebates = mapping.SyncRebates;
            LastSyncUtc = mapping.LastSync;
            EarliestTransactionDateToSync = mapping.EarliestTransactionDateToSync.ToUniversalTime();
            if (mapping.EndDateUtc != null)
            {
                EndDateUtc = mapping.EndDateUtc.Value.ToUniversalTime();
            }

            AplosAccountId = mapping.AplosAccountId;
            AplosPartnerVerified = mapping.AplosPartnerVerified;
            AplosClientId = mapping.AplosClientId;
            AplosPrivateKey = mapping.AplosPrivateKey;
            AplosAuthenticationMode = mapping.AplosAuthenticationMode;

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
            PexFeesAplosTaxTagId = mapping.PexFeesAplosTaxTag;

            PexRebatesAplosContactId = mapping.PexRebatesAplosContactId;
            PexRebatesAplosFundId = mapping.PexRebatesAplosFundId;
            PexRebatesAplosTransactionAccountNumber = mapping.PexRebatesAplosTransactionAccountNumber;
            PexRebatesAplosTaxTagId = mapping.PexRebatesAplosTaxTag;

            PexFundsTagId = mapping.PexFundsTagId;
            SyncFundsToPex = mapping.SyncFundsToPex;

            PexTaxTagId = GetPexTaxTagId(mapping.TagMappings);

            ExpenseAccountMappings = mapping.ExpenseAccountMappings;
            TagMappings = mapping.TagMappings;
            PEXFundingSource = mapping.PEXFundingSource;
            MapVendorCards = mapping.MapVendorCards;
            UseNormalizedMerchantNames = mapping.UseNormalizedMerchantNames;
            PostDateType = mapping.PostDateType;

            ExpirationEmailLastDate = mapping.ExpirationEmailLastDate;
            ExpirationEmailCount = mapping.ExpirationEmailCount;
            IsTokenExpired = mapping.IsTokenExpired;
        }

        private static string GetPexTaxTagId(IEnumerable<TagMappingModel> tagMappings)
        {
            return tagMappings?.FirstOrDefault(t => t.AplosTagId == "990")?.PexTagId;
        }

        public MappingSettingsModel ToStorageModel()
        {
            return new MappingSettingsModel
            {
                AutomaticSync = AutomaticSync,
                IsSyncing = IsSyncing,
                IsManualSync = IsManualSync,
                ConnectedOn = CreatedUtc,
                SyncApprovedOnly = SyncApprovedOnly,
                SyncTags = SyncTags,
                SyncTaxTagToPex = SyncTaxTagToPex,
                SyncTransactions = SyncTransactions,
                SyncTransfers = SyncTransfers,
                SyncInvoices = SyncInvoices,
                SyncPexFees = SyncPexFees,
                SyncRebates = SyncRebates,
                LastSync = LastSyncUtc,
                EarliestTransactionDateToSync = EarliestTransactionDateToSync,
                EndDateUtc = EndDateUtc,

                AplosAccountId = AplosAccountId,
                AplosPartnerVerified = AplosPartnerVerified,
                AplosClientId = AplosClientId,
                AplosPrivateKey = AplosPrivateKey,
                AplosAuthenticationMode = AplosAuthenticationMode,

                AplosAccessToken = AplosAccessToken,
                AplosAccessTokenExpiresAt = AplosAccessTokenExpiresAt,

                AplosRegisterAccountNumber = AplosRegisterAccountNumber,

                SyncTransactionsCreateContact = SyncTransactionsCreateContact,
                DefaultAplosContactId = DefaultAplosContactId,

                DefaultAplosFundId = DefaultAplosFundId,
                DefaultAplosTransactionAccountNumber = DefaultAplosTransactionAccountNumber,
                PexFundsTagId = PexFundsTagId,

                PexTaxTagId = GetPexTaxTagId(TagMappings),

                TransfersAplosContactId = TransfersAplosContactId,
                TransfersAplosFundId = TransfersAplosFundId,
                TransfersAplosTransactionAccountNumber = TransfersAplosTransactionAccountNumber,

                PexFeesAplosContactId = PexFeesAplosContactId,
                PexFeesAplosFundId = PexFeesAplosFundId,
                PexFeesAplosTransactionAccountNumber = PexFeesAplosTransactionAccountNumber,
                PexFeesAplosTaxTag = PexFeesAplosTaxTagId,

                PexRebatesAplosContactId = PexRebatesAplosContactId,
                PexRebatesAplosFundId = PexRebatesAplosFundId,
                PexRebatesAplosTransactionAccountNumber = PexRebatesAplosTransactionAccountNumber,
                PexRebatesAplosTaxTag = PexRebatesAplosTaxTagId,

                SyncFundsToPex = SyncFundsToPex,

                ExpenseAccountMappings = ExpenseAccountMappings,
                TagMappings = TagMappings,
                PEXFundingSource = PEXFundingSource,

                SyncTransactionsIntervalDays = SyncTransactionsIntervalDays,
                FetchTransactionsIntervalDays = FetchTransactionsIntervalDays,
                MapVendorCards = MapVendorCards,
                UseNormalizedMerchantNames = UseNormalizedMerchantNames,
                PostDateType = PostDateType,

                ExpirationEmailLastDate = ExpirationEmailLastDate,
                ExpirationEmailCount = ExpirationEmailCount,
                IsTokenExpired = IsTokenExpired
            };
        }

        public bool AutomaticSync { get; set; }
        public bool IsSyncing { get; set; }
        public bool IsManualSync { get; set; }
        public string PEXExternalAPIToken { get; set; }
        public int PEXBusinessAcctId { get; set; }
        public FundingSource PEXFundingSource { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? LastSyncUtc { get; set; }
        public DateTime? LastRenewedUtc { get; set; }
        public bool SyncTags { get; set; }
        public bool SyncTaxTagToPex { get; set; }
        public bool SyncTransactions { get; set; }
        public bool SyncTransfers { get; set; }
        public bool SyncPexFees { get; set; }
        public bool SyncInvoices { get; set; }
        public bool SyncRebates { get; set; }
        public bool SyncApprovedOnly { get; set; }
        public DateTime EarliestTransactionDateToSync { get; set; }
        public DateTime? EndDateUtc { get; set; }

        public string AplosAccountId { get; set; }
        public bool AplosPartnerVerified { get; set; }
        public string AplosClientId { get; set; }
        public string AplosPrivateKey { get; set; }
        public AplosAuthenticationMode AplosAuthenticationMode { get; set; } = AplosAuthenticationMode.ClientAuthentication;

        public decimal AplosRegisterAccountNumber { get; set; }

        public bool SyncTransactionsCreateContact { get; set; }
        public int DefaultAplosContactId { get; set; }

        public bool SyncFundsToPex { get; set; }
        public string PexFundsTagId { get; set; }
        public int DefaultAplosFundId { get; set; }
        public string PexTaxTagId { get; set; }
        public decimal DefaultAplosTransactionAccountNumber { get; set; }

        public int TransfersAplosContactId { get; set; }
        public int TransfersAplosFundId { get; set; }
        public decimal TransfersAplosTransactionAccountNumber { get; set; }

        public int PexFeesAplosContactId { get; set; }
        public int PexFeesAplosFundId { get; set; }
        public string PexFeesAplosTaxTagId { get; set; }
        public decimal PexFeesAplosTransactionAccountNumber { get; set; }

        public int PexRebatesAplosContactId { get; set; }
        public int PexRebatesAplosFundId { get; set; }
        public decimal PexRebatesAplosTransactionAccountNumber { get; set; }
        public string PexRebatesAplosTaxTagId { get; set; }

        public ExpenseAccountMappingModel[] ExpenseAccountMappings { get; set; }
        public TagMappingModel[] TagMappings { get; set; }

        public string PEXEmailAccount { get; set; }
        public string PEXNameAccount { get; set; }
        public double? SyncTransactionsIntervalDays { get; set; }
        public double? FetchTransactionsIntervalDays { get; set; }
        public bool MapVendorCards { get; set; }
        public bool UseNormalizedMerchantNames { get; set; }

        public PostDateType PostDateType { get; set; }

        public DateTime? ExpirationEmailLastDate { get; set; }
        public int ExpirationEmailCount { get; set; }
        public int TotalExpirationEmailCount { get; set; }
        public bool IsTokenExpired { get; set; }

        public DateTime GetLastRenewedDateUtc()
        {
            return LastRenewedUtc ?? CreatedUtc;
        }
    }

    public enum PostDateType
    {
        Transaction,
        Settlement
    }

    public enum AplosAuthenticationMode
    {
        ClientAuthentication = 0,
        PartnerAuthentication = 1,
    }
}
