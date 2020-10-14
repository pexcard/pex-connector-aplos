using Newtonsoft.Json;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiContactAddressDetail
    {
        public string Name { get; set; }
        [JsonProperty("is_primary")]
        public bool IsPrimary { get; set; }
        public string Street1 { get; set; }
        public string Street2 { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string PostalCode { get; set; }
    }
}