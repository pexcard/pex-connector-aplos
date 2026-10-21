using Moq;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

// GetVendors never returns CustomId, so these vendors come back the way PEX lists them: without one.
public class VendorRecognitionTests : VendorSetupTestBase
{
    private static VendorModel ListedVendor(VendorStatus status = VendorStatus.Onboarded, string name = ContactName)
        => new() { VendorId = VendorId, VendorName = name, VendorStatus = status };

    [Fact]
    public async Task AVendorCreatedOnAnEarlierRunIsApprovedAndGivenItsCardThroughItsRow()
    {
        SetupPexVendors(ListedVendor(VendorStatus.Pending));
        SeedCardOrder(cardAcctId: null);

        await RunSync();

        PexApiClient.Verify(c => c.CreateVendor(It.IsAny<string>(), It.IsAny<CreateVendorRequestModel>(), It.IsAny<CancellationToken>()), Times.Never);
        PexApiClient.Verify(c => c.ApproveVendor(It.IsAny<string>(), VendorId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyCardLinked(Times.Once());
        AssertSucceeded();
    }

    [Fact]
    public async Task AVendorRenamedInPexIsStillTheBillsVendor()
    {
        SetupPexVendors(ListedVendor(name: "Acme Supplies LLC"));
        SeedCardOrder(CardAcctId);

        await RunSync();

        PexApiClient.Verify(c => c.CreateVendor(It.IsAny<string>(), It.IsAny<CreateVendorRequestModel>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(VendorId, Assert.Single(CreatedBillInbox).VendorId);
        AssertSucceeded();
    }

    [Fact]
    public async Task AVendorWhoseCardWasNeverOrderedGetsOneOnALaterRun()
    {
        PexApiClient
            .SetupSequence(c => c.GetMyAdminProfile(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException())
            .ReturnsAsync(new BusinessAdminReponseModel { Admin = new BusinessAdminModel { Email = "admin@example.com", Phone = "5125550100" } });

        await RunSync();
        Assert.Empty(CardOrders);
        AssertReported("has no linked PEX vendor card yet");

        SetupPexVendors(ListedVendor());
        await RunSync();

        Assert.Single(CardOrders);
        VerifyCardLinked(Times.Once());
        AssertSucceeded();
    }

    [Fact]
    public async Task ACardAnOverlappingRunAlreadyRequestedIsNotOrderedAgain()
    {
        SetupPexVendors(ListedVendor());
        SeedCardOrder(cardAcctId: null, orderId: null, cardName: null);
        PexApiClient
            .Setup(c => c.GetMyAdminProfile(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => CardOrderStorage.Rows.Values.Single().CardName = ContactName)
            .ReturnsAsync(new BusinessAdminReponseModel { Admin = new BusinessAdminModel { Email = "admin@example.com", Phone = "5125550100" } });

        await RunSync();

        Assert.Empty(CardOrders);
    }

    [Fact]
    public async Task ANewOrdersCardIsLinkedInTheSameRunOnceItIsProvisioned()
    {
        var notYet = new VendorCardOrderResponseModel { CardOrderId = CardOrderId, Cards = [new VendorCardOrderItemResponse { AcctId = null, VendorName = ContactName }] };
        var provisioned = new VendorCardOrderResponseModel { CardOrderId = CardOrderId, Cards = [new VendorCardOrderItemResponse { AcctId = CardAcctId, VendorName = ContactName }] };
        PexApiClient
            .SetupSequence(c => c.GetVendorCardOrder(It.IsAny<string>(), CardOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notYet)
            .ReturnsAsync(notYet)
            .ReturnsAsync(provisioned);

        await RunSync();

        PexApiClient.Verify(c => c.GetVendorCardOrder(It.IsAny<string>(), CardOrderId, It.IsAny<CancellationToken>()), Times.Exactly(3));
        VerifyCardLinked(Times.Once());
        AssertSucceeded();
    }
}
