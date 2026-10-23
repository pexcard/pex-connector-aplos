using AplosConnector.Common.Models;
using Azure;
using Azure.Data.Tables;
using System;

namespace AplosConnector.Common.Entities
{
    public class AplosBillMappingEntity : ITableEntity
    {
        public AplosBillMappingEntity()
        {
            CreatedUtc = DateTime.UtcNow;
        }

        public AplosBillMappingEntity(AplosBillMappingModel model)
        {
            PartitionKey = model.PEXBusinessAcctId.ToString();
            RowKey = model.AplosPayableId;
            PEXBusinessAcctId = model.PEXBusinessAcctId;
            AplosPayableId = model.AplosPayableId;
            AplosReferenceNumber = model.AplosReferenceNumber;
            PexBillInboxId = model.PexBillInboxId;
            MetadataRelationId = model.MetadataRelationId;
            Amount = (double)model.Amount;
            PaidSyncedUtc = model.PaidSyncedUtc?.ToUniversalTime();
            FirstFailedUtc = model.FirstFailedUtc?.ToUniversalTime();
            ReversedUtc = model.ReversedUtc?.ToUniversalTime();
            AwaitingChargeSinceUtc = model.AwaitingChargeSinceUtc?.ToUniversalTime();
            PaidFromClearingAccountNumber = (double?)model.PaidFromClearingAccountNumber;
            CardTransactionId = model.CardTransactionId;
            CreatedUtc = model.CreatedUtc.ToUniversalTime();
        }

        public int PEXBusinessAcctId { get; set; }
        public string AplosPayableId { get; set; }
        public string AplosReferenceNumber { get; set; }
        public int PexBillInboxId { get; set; }
        public long? MetadataRelationId { get; set; }
        public double Amount { get; set; }
        public DateTime? PaidSyncedUtc { get; set; }
        public DateTime? FirstFailedUtc { get; set; }
        public DateTime? ReversedUtc { get; set; }
        public DateTime? AwaitingChargeSinceUtc { get; set; }
        public double? PaidFromClearingAccountNumber { get; set; }
        public long? CardTransactionId { get; set; }
        public DateTime CreatedUtc { get; set; }

        public AplosBillMappingModel ToModel()
        {
            return new AplosBillMappingModel
            {
                PEXBusinessAcctId = PEXBusinessAcctId,
                AplosPayableId = AplosPayableId,
                AplosReferenceNumber = AplosReferenceNumber,
                PexBillInboxId = PexBillInboxId,
                MetadataRelationId = MetadataRelationId,
                Amount = (decimal)Amount,
                PaidSyncedUtc = PaidSyncedUtc,
                FirstFailedUtc = FirstFailedUtc,
                ReversedUtc = ReversedUtc,
                AwaitingChargeSinceUtc = AwaitingChargeSinceUtc,
                PaidFromClearingAccountNumber = (decimal?)PaidFromClearingAccountNumber,
                CardTransactionId = CardTransactionId,
                CreatedUtc = CreatedUtc
            };
        }

        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
