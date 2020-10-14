using Newtonsoft.Json;
using System.Collections.Generic;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiTagDetail
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("sub_tags")]
        public List<AplosApiTagDetail> SubTags { get; set; }

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