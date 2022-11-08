using System;
using PexCard.Api.Client.Core.Models;

namespace AplosConnector.Common.Models
{
    public class MappingSettingsModel
    {
        public DateTime ConnectedOn { get; set; }
        public DateTime? LastSync { get; set; }

        public string AplosAccountId { get; set; }
        public bool AplosPartnerVerified { get; set; }
        public string AplosClientId { get; set; }
        public string AplosPrivateKey { get; set; }
        public AplosAuthenticationMode AplosAuthenticationMode { get; set; } = AplosAuthenticationMode.ClientAuthentication;

        public string AplosAccessToken { get; internal set; }
        public DateTime? AplosAccessTokenExpiresAt { get; internal set; }

        /// <summary>
        /// Whether to sync Aplos entities as PEX tags.
        /// </summary>
        public bool SyncTags { get; set; }
        /// <summary>
        /// Whether to sync Aplos 990 tag.
        /// </summary>
        public bool SyncTaxTagToPex { get; set; }
        /// <summary>
        /// Whether to sync PEX transactions to Aplos.
        /// </summary>
        public bool SyncTransactions { get; set; }
        /// <summary>
        /// Whether to only sync approved transactions.
        /// </summary>
        public bool SyncApprovedOnly { get; set; }
        public DateTime EarliestTransactionDateToSync { get; set; }
        /// <summary>
        /// Whether to sync PEX transfers to Aplos.
        /// </summary>
        public bool SyncTransfers { get; set; }
        /// <summary>
        /// Whether to sync PEX invoices to Aplos.
        /// </summary>
        public bool SyncInvoices { get; set; }
        /// <summary>
        /// Whether to sync PEX account fees to Aplos.
        /// </summary>
        public bool SyncPexFees { get; set; }

        /// <summary>
        /// The AccountId for the register to use in Aplos. This is the account from which money will be taken from in the transaction created in Aplos.
        /// </summary>
        public decimal AplosRegisterAccountNumber { get; set; }

        public bool SyncTransactionsCreateContact { get; set; }
        public int DefaultAplosContactId { get; set; }
        public string PexFundsTagId { get; set; }

        public bool SyncFundsToPex { get; set; }
        public int DefaultAplosFundId { get; set; }

        public string PexTaxTagId { get; set; }

        public decimal DefaultAplosTransactionAccountNumber { get; set; }

        public int TransfersAplosContactId { get; set; }
        public int TransfersAplosFundId { get; set; }
        public decimal TransfersAplosTransactionAccountNumber { get; set; }

        public int PexFeesAplosContactId { get; set; }
        public int PexFeesAplosFundId { get; set; }
        public decimal PexFeesAplosTransactionAccountNumber { get; set; }
        public string PexFeesAplosTaxTag { get; set; }

        public ExpenseAccountMappingModel[] ExpenseAccountMappings { get; set; }
        public TagMappingModel[] TagMappings { get; set; }
        public FundingSource PEXFundingSource { get; set; }
    }
}
