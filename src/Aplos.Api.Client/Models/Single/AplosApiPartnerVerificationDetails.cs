using Newtonsoft.Json;

namespace Aplos.Api.Client.Models.Single
{
    public sealed class AplosApiPartnerVerificationDetails
    {
        [JsonProperty("aplos_account_id")]
        public string AplosAccountId { get; set; }

        [JsonProperty("authorized")]
        public bool Authorized { get; set; }
    }
}