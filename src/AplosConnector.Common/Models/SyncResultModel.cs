using System;

namespace AplosConnector.Common.Models
{
    public class SyncResultModel
    {
        public SyncResultModel()
        {
            CreatedUtc = DateTime.UtcNow;
        }

        public string Id { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string SyncType { get; set; }
        public string SyncStatus { get; set; }
        public int SyncedRecords { get; set; }
        public string SyncNotes { get; set; }
        public int PEXBusinessAcctId { get; set; }
    }
}
