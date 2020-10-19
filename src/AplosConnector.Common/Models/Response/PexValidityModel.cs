namespace AplosConnector.Common.Models.Response
{
    public class PexValidityModel
    {
        public bool IsValid => UseTagsEnabled;
        public bool UseTagsEnabled { get; set; }
    }
}
