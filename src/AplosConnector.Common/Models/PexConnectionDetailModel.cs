using System;

namespace AplosConnector.Common.Models
{
    public class PexConnectionDetailModel
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public bool PexConnection { get; set; }
        public bool AplosConnection { get; set; }
        public bool SyncingSetup { get; set; }
        public bool VendorsSetup { get; set; }
        public bool IsSyncing { get; set; }
        public DateTime? LastSync { get; set; }
        public decimal? AccountBalance { get; set; }
        public bool UseBusinessBalanceEnabled { get; set; }
        public int? VendorCardsAvailable { get; set; }
        public bool IsPrepaid { get; set; }
        public bool IsCredit { get; set; }
    }
}
