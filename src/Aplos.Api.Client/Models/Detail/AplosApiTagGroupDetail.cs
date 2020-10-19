using Newtonsoft.Json;
using System.Collections.Generic;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiTagGroupDetail
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("tags")]
        public List<AplosApiTagDetail> Tags { get; set; }
    }
}