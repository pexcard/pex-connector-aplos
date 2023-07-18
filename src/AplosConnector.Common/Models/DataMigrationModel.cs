namespace AplosConnector.Common.Models
{
    public class DataMigrationModel
    {
        public long Ticks { get; set; }
        public int BatchSize { get; set; } = 1000;
    }
}
