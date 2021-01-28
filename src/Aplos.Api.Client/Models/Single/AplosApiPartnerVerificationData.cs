using Newtonsoft.Json;

namespace Aplos.Api.Client.Models.Single
{
    public sealed class AplosApiPartnerVerificationData
    {
        [JsonProperty("partner_verification")]
        public AplosApiPartnerVerificationDetails PartnerVerification { get; set; }
    }
}