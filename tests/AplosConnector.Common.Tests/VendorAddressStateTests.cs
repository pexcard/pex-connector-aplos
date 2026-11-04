using Moq;
using PexCard.Api.Client.Core.Models;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class VendorAddressStateTests : VendorSetupTestBase
{
    [Theory]
    [InlineData("Alabama", "AL")]
    [InlineData(" new york ", "NY")]
    [InlineData("tx", "TX")]
    public async Task TheVendorIsCreatedWithTheTwoLetterStateCode(string aplosState, string expectedCode)
    {
        SetupContactState(aplosState);

        await RunSync();

        PexApiClient.Verify(c => c.CreateVendor(It.IsAny<string>(), It.Is<CreateVendorRequestModel>(r => r.VendorAddress.State == expectedCode), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(CreatedBillInbox);
    }

    [Fact]
    public async Task AnUnrecognisedStateIsReportedAsMissingAndNoVendorIsCreated()
    {
        SetupContactState("Narnia");

        await RunSync();

        PexApiClient.Verify(c => c.CreateVendor(It.IsAny<string>(), It.IsAny<CreateVendorRequestModel>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(CreatedBillInbox);
        var notes = Assert.Single(HistoryRows).SyncNotes;
        Assert.StartsWith("1 bill from Aplos needs attention. ", notes);
        Assert.Contains("Bill INV-9001: Add a billing address for the Acme Supplies contact in Aplos. Missing: State.", notes);
    }
}
