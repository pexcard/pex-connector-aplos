using AplosConnector.Common.Models;

namespace AplosConnector.Web.Models
{
    public class AplosAuthenticationStatusModel
    {
        public AplosAuthenticationMode AplosAuthenticationMode { get; set; }
        public bool HasAplosAccountId { get; set; }
        public bool IsAuthenticated { get; set; }
        public string PartnerVerificationUrl { get; set; }
    }
}
