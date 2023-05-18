using Newtonsoft.Json;

namespace Aplos.Api.Client.Models.Detail;

public class LiabilityAccountDetail
{
    [JsonProperty("account_number")]
    public int AccountNumber { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }
}