using Newtonsoft.Json;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiContactPhoneDetail
    {
        public string Name { get; set; }
        [JsonProperty("telnum")]
        public string TelephoneNumber { get; set; }
        [JsonProperty("is_primary")]
        public bool IsPrimary { get; set; }
    }
}