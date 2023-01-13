namespace AplosConnector.Common.Models
{
    public class SyncSettingsModel
    {
        public double SyncTransactionsIntervalDays { get; set; } = 60;
        public double FetchTransactionsIntervalDays { get; set; } = 14;
    }
}
