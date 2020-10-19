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
        public AplosApiContactDetail Contact { get; set; } //TODO: Might need to split off this into its own class - the example only shows 3 properties populated.
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        [JsonProperty("lines")]
        public AplosApiTransactionLineDetail[] Lines { get; set; } //TODO: Split out this Response into a detail and listing if needed.
    }
}