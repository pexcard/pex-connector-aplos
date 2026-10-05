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
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
