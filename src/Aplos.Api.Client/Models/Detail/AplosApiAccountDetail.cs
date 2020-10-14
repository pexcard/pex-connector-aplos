using Newtonsoft.Json;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiAccountDetail
    {
        [JsonProperty("account_number")]
        public decimal AccountNumber { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        [JsonProperty("is_enabled")]
        public bool IsEnabled { get; set; }
        public string Type { get; set; }
        public string Activity { get; set; }
    }
}