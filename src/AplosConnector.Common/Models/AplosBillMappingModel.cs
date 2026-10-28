using System;

namespace AplosConnector.Common.Models
{
    public class AplosBillMappingModel
    {
        public int PEXBusinessAcctId { get; set; }
        public string AplosPayableId { get; set; }
        public string AplosReferenceNumber { get; set; }
        public int PexBillInboxId { get; set; }
        public long? MetadataRelationId { get; set; }
        public decimal Amount { get; set; }
        // Aplos's pay call returns no transaction id, so this is the paid flag (148047 comment 8495381).
        public DateTime? PaidSyncedUtc { get; set; }
        // Start of the retry window; see BillPaymentRetryWindow.
        public DateTime? FirstFailedUtc { get; set; }
        // Set by a reversal until the customer deletes the payment in Aplos and the bill reads unpaid again.
        public DateTime? ReversedUtc { get; set; }
        // When a card payment was first seen waiting for its charge; see AwaitingCardChargeLimit.
        public DateTime? AwaitingChargeSinceUtc { get; set; }
        // The vendor card clearing account the bill is paid from, written before paying: the transfer out of that account
        // is owed once the payable reads paid, even if the setting has changed since (153880).
        public decimal? PaidFromClearingAccountNumber { get; set; }
        // The card charge claimed for this bill: the transaction sync never books it, whatever the PEX note says.
        public long? CardTransactionId { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
