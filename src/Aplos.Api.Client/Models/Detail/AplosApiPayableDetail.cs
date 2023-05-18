using Newtonsoft.Json;
using System;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiTransactionDetail
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("memo")]
        public string Memo { get; set; }
        [JsonProperty("note")]
        public string Note { get; set; }
        [JsonProperty("date")]
        public DateTime Date { get; set; }
        [JsonProperty("amount")]
        public decimal Amount { get; set; }
        [JsonProperty("contact")]
        public AplosApiContactDetail Contact { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        [JsonProperty("lines")]
        public AplosApiTransactionLineDetail[] Lines { get; set; }
    }
}