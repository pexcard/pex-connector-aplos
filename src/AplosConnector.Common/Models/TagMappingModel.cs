namespace AplosConnector.Common.Models
{
    public class TagMappingModel
    {
        public string AplosTagId { get; set; }
        public string PexTagId { get; set; }
        public bool SyncToPex { get; set; }
    }
}
