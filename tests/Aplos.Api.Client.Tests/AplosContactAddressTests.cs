using Aplos.Api.Client.Models.Detail;
using Newtonsoft.Json;
using Xunit;

namespace Aplos.Api.Client.Tests;

public class AplosContactAddressTests
{
    [Fact]
    public void AnAddressAsAplosReturnsItKeepsItsPostalCode()
    {
        const string json = """{"name":"Main","is_primary":true,"street1":"1 Main St","street2":null,"city":"Austin","state":"TX","postal_code":"73301"}""";

        var address = JsonConvert.DeserializeObject<AplosApiContactAddressDetail>(json);

        Assert.Equal(("1 Main St", "Austin", "TX", "73301"), (address.Street1, address.City, address.State, address.PostalCode));
    }
}
