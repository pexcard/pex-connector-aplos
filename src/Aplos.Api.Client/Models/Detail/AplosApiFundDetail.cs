using Newtonsoft.Json;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiFundDetail
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("balance_account_name")]
        public string BalanceAccountName { get; set; }
        [JsonProperty("balance_account_number")]
        public decimal BalanceAccountNumber { get; set; }
    }
}