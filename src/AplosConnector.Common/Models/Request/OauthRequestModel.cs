namespace AplosConnector.Common.Models.Request
{
    public class OAuthRequestModel
    {
        public string AppId { get; set; }
        public string AppSecret { get; set; }
        public string ServerCallbackUrl { get; set; }
        public string BrowserClosingUrl { get; set; }
    }
}
