using Newtonsoft.Json;
using System;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiContactDetail
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        [JsonProperty("companyname")]
        public string CompanyName { get; set; }
        public string Type { get; set; }
        public string Email { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }

        //TODO: Split out this Response into a detail and listing if needed - these three properties are populated when retrieving an individual contact but not when getting a list of contacts.
        public AplosApiContactEmailDetail[] Emails { get; set; }
        public AplosApiContactPhoneDetail[] Phones { get; set; }
        public AplosApiContactAddressDetail[] Addresses { get; set; }

        public bool ShouldSerializeId()
        {
            //Although there are no direct references to this method, Json.NET calls it by convention when deciding whether to serialize the Id property.
            //This is needed for creating contacts during transaction creation.
            return this.Id != default;
        }
    }
}