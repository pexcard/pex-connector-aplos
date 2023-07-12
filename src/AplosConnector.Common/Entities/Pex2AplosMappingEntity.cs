using System;
using Azure;
using Azure.Data.Tables;

namespace AplosConnector.Common.Entities
{
    public class Pex2AplosMappingEntity : ITableEntity
    {
        public Pex2AplosMappingEntity()
        {
            CreatedUtc = DateTime.UtcNow;
            IsManualSync = false; // always reset to false when saving
        }

        public bool IsManualSync { get; set; }
        public string PEXExternalAPIToken { get; set; }
        public int PEXBusinessAcctId { get; set; }
        public int PEXFundingSource { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? LastSyncUtc { get; set; }
        public DateTime? LastRenewedUtc { get; set; }
        public bool SyncTags { get; set; }
        public bool SyncTaxTagToPex { get; set; }
        public bool SyncTransactions { get; set; }
        public bool SyncTransfers { get; set; }
        public bool SyncInvoices { get; set; }
        public bool SyncPexFees { get; set; }
        public bool SyncApprovedOnly { get; set; }
        public DateTime EarliestTransactionDateToSync { get; set; }
        public DateTime? EndDateUtc { get; set; }

        public string AplosAccountId { get; set; }
        public bool AplosPartnerVerified { get; set; }
        public string AplosClientId { get; set; }
        public string AplosPrivateKey { get; set; }
        public int AplosAuthenticationMode { get; set; }

        public string AplosAccessToken { get; set; }
        public DateTime? AplosAccessTokenExpiresAt { get; set; }

        public string AplosRegisterAccountNumber { get; set; }

        public bool SyncTransactionsCreateContact { get; set; }
        public int DefaultAplosContactId { get; set; }

        public bool SyncFundsToPex { get; set; }
        public string PexFundsTagId { get; set; }
        public string PexTaxTagId { get; set; }
        public int DefaultAplosFundId { get; set; }

        public string DefaultAplosTransactionAccountNumber { get; set; }

        public int TransfersAplosContactId { get; set; }
        public int TransfersAplosFundId { get; set; }
        public string TransfersAplosTransactionAccountNumber { get; set; }

        public int PexFeesAplosContactId { get; set; }
        public int PexFeesAplosFundId { get; set; }
        public string PexFeesAplosTransactionAccountNumber { get; set; }
        public string PexFeesAplosTaxTagId { get; set; }

        public string ExpenseAccountMappings { get; set; }
        public string TagMappings { get; set; }
        
        public string PEXEmailAccount { get; set; }
        public string PEXNameAccount { get; set; }

        public double? SyncTransactionsIntervalDays { get; set; }
        public double? FetchTransactionsIntervalDays { get; set; }
        public bool MapVendorCards { get; set; }
        
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
