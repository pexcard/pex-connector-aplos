using Newtonsoft.Json;
using System.Collections.Generic;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiTransactionLineDetail
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("amount")]
        public decimal Amount { get; set; }
        [JsonProperty("account")]
        public AplosApiAccountDetail Account { get; set; }
        [JsonProperty("fund")]
        public AplosApiFundDetail Fund { get; set; }
        [JsonProperty("tags")]
        public List<AplosApiTagDetail> Tags { get; set; }
        [JsonProperty("tax_tag")]
        public AplosApiTagDetail TaxTag { get; set; }
    }
}