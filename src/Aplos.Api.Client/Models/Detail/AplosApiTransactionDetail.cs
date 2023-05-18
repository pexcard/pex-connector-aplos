using Newtonsoft.Json;
using System;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiPayableDetail
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("bill_date")]
        public DateTime BillDate { get; set; }
        [JsonProperty("due_date")]
        public DateTime DueDate { get; set; }
        [JsonProperty("reference_num")]
        public string ReferenceNumber{ get; set; }
        [JsonProperty("note")]
        public string Note { get; set; }
        [JsonProperty("contact")]
        public AplosApiContactDetail Contact { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        [JsonProperty("amount")]
        public decimal Amount { get; set; }
        [JsonProperty("paid")]
        public decimal PaidAmount { get; set; }
        [JsonProperty("liability_account")]
        public LiabilityAccountDetail LiabilityAccount { get; set; }
    }
}