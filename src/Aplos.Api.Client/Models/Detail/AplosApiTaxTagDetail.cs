using Newtonsoft.Json;
using System.Collections.Generic;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiTaxTagDetail
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("group_name")]
        public string GroupName { get; set; }

        public override bool Equals(object obj)
        {
            return obj is AplosApiTagDetail detail &&
                   Id == detail.Id &&
                   Name == detail.Name;
        }

        public override int GetHashCode()
        {
            return 2108858624 + EqualityComparer<string>.Default.GetHashCode(Id);
        }
    }
}