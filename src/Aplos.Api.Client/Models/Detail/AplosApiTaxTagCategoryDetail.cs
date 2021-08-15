using Newtonsoft.Json;
using System.Collections.Generic;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiTaxTagCategoryDetail
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("tax_tags")]
        public List<AplosApiTaxTagDetail> TaxTags { get; set; }
    }
}