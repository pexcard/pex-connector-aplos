using Newtonsoft.Json;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiContactEmailDetail
    {
        public string Name { get; set; }
        public string Address { get; set; }
        [JsonProperty("is_primary")]
        public bool IsPrimary { get; set; }
    }
}