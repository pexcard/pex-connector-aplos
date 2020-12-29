using Newtonsoft.Json;

namespace Aplos.Api.Client.Models.Single
{
    public sealed class AplosApiPartnerVerificationData
    {
        [JsonProperty("aplos_account_id")]
        public string AplosAccountId { get; set; }

        [JsonProperty("authorized")]
        public bool Authorized { get; set; }
    }
}