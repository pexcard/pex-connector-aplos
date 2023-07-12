using AplosConnector.Common.Models;
using System;
using Azure;
using Azure.Data.Tables;

namespace AplosConnector.Common.Entities
{
    public class SyncResultEntity : ITableEntity
    {
        public SyncResultEntity()
        {
            CreatedUtc = DateTime.UtcNow;
        }

        public SyncResultEntity(SyncResultModel model)
        {
            RowKey = model.Id;
            CreatedUtc = model.CreatedUtc;
            SyncType = model.SyncType;
            SyncStatus = model.SyncStatus;
            SyncedRecords = model.SyncedRecords;
            SyncNotes = model.SyncNotes;
            PEXBusinessAcctId = model.PEXBusinessAcctId;
        }

        public int PEXBusinessAcctId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string SyncType { get; set; }
        public string SyncStatus { get; set; }
        public int SyncedRecords { get; set; }
        public string SyncNotes { get; set; }

        public SyncResultModel ToModel()
        {
            return new SyncResultModel
            {
                Id = RowKey,
                CreatedUtc = CreatedUtc,
                SyncedRecords = SyncedRecords,
                SyncNotes = SyncNotes,
                SyncStatus = SyncStatus,
                SyncType = SyncType,
                PEXBusinessAcctId = PEXBusinessAcctId
            };
        }

        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
