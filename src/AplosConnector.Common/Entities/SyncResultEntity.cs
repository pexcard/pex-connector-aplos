using AplosConnector.Common.Models;
using Microsoft.Azure.Cosmos.Table;
using System;

namespace AplosConnector.Common.Entities
{
    public class SyncResultEntity : TableEntity
    {
        public SyncResultEntity()
        {
            CreatedUtc = DateTime.UtcNow;
        }

        public SyncResultEntity(SyncResultModel model)
        {
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
                CreatedUtc = CreatedUtc,
                SyncedRecords = SyncedRecords,
                SyncNotes = SyncNotes,
                SyncStatus = SyncStatus,
                SyncType = SyncType,
                PEXBusinessAcctId = PEXBusinessAcctId
            };
        }
    }
}
