using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using Moq;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class VendorCardRetryTests : VendorSetupTestBase
{
    private const int OtherContactId = 501;
    private const string OtherContactName = "Bolt Hardware";
    private const int OtherVendorId = 8;

    [Fact]
    public async Task NoCardIsOrderedWhenItsCardOrderRowCannotBeWritten()
    {
        CardOrderStorage.FailAdd = true;

        await RunSync();

        Assert.Empty(CardOrders);
        Assert.Single(CreatedBillInbox);
        AssertReported("has no linked PEX vendor card yet");
    }

    // The order may have gone through, so its card is never ordered again; the customer resolves it in PEX.
    [Fact]
    public async Task AnOrderThatMayHaveBeenPlacedIsReportedAndNeverReordered()
    {
        PexApiClient
            .Setup(c => c.CreateVendorCardOrder(It.IsAny<string>(), It.IsAny<VendorCardCreateOrderRequestModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, VendorCardCreateOrderRequestModel, CancellationToken>((_, request, _) => CardOrders.Add(request))
            .ThrowsAsync(new TimeoutException());

        await RunSync();
        AssertReported("vendor card order for Acme Supplies was not confirmed");

        SetupPexVendors(ConnectorVendor());
        await RunSync();

        Assert.Single(CardOrders);
        AssertReported("vendor card order for Acme Supplies was not confirmed");
    }

    [Fact]
    public async Task AnOrderIdThatCannotBeRecordedIsStillUsedToLinkTheCardThisRun()
    {
        CardOrderStorage.FailSetOrderId = true;

        await RunSync();

        VerifyCardLinked(Times.Once());
        Assert.Equal(CardAcctId, CardOrderStorage.Rows[(ContactId, VendorId)].CardAcctId);
        AssertSucceeded();

        SetupPexVendors(ConnectorVendor());
        await RunSync();

        Assert.Single(CardOrders);
        AssertSucceeded();
    }

    [Fact]
    public async Task AnUnrecordedOrderWhoseCardIsNotProvisionedYetIsReportedAsNotConfirmed()
    {
        CardOrderStorage.FailSetOrderId = true;
        SetupProvisionedCard(acctId: null);

        await RunSync();

        Assert.Null(CardOrderStorage.Rows[(ContactId, VendorId)].CardOrderId);
        AssertReported("vendor card order for Acme Supplies was not confirmed");
    }

    [Fact]
    public async Task ACardNotProvisionedYetIsLinkedOnALaterRunWithoutReordering()
    {
        SetupProvisionedCard(acctId: null);

        await RunSync();
        AssertReported("has no linked PEX vendor card yet");
        VerifyCardLinked(Times.Never());

        SetupProvisionedCard(CardAcctId);
        SetupPexVendors(ConnectorVendor());
        await RunSync();

        Assert.Single(CardOrders);
        VerifyCardLinked(Times.Once());
        Assert.Equal(CardAcctId, CardOrderStorage.Rows[(ContactId, VendorId)].CardAcctId);
        AssertSucceeded();
    }

    [Fact]
    public async Task AFailedLinkIsRetriedThroughTheStoredOrderId()
    {
        PexApiClient
            .SetupSequence(c => c.AddVendorCard(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AddVendorCardRequestModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PEX unavailable"))
            .ReturnsAsync(new VendorModel());

        await RunSync();
        AssertReported("has no linked PEX vendor card yet");

        SetupPexVendors(ConnectorVendor());
        await RunSync();

        Assert.Single(CardOrders);
        PexApiClient.Verify(c => c.GetVendorCardOrder(It.IsAny<string>(), CardOrderId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(CardAcctId, CardOrderStorage.Rows[(ContactId, VendorId)].CardAcctId);
        AssertSucceeded();
    }

    [Fact]
    public async Task AVendorWithALinkedCardNeedsNoPexCardCalls()
    {
        SetupPexVendors(ConnectorVendor());
        SeedCardOrder(CardAcctId);

        await RunSync();

        Assert.Empty(CardOrders);
        PexApiClient.Verify(c => c.GetVendorCardOrder(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        AssertSucceeded();
    }

    // 148046 ordered and linked cards without writing rows, and GetVendors can't show whether a vendor has one.
    [Fact]
    public async Task AnExistingConnectorVendorWithoutARowIsReportedAndNeverOrderedACard()
    {
        SetupPexVendors(ConnectorVendor());

        await RunSync();

        Assert.Empty(CardOrders);
        AssertReported("has no linked PEX vendor card yet");
    }

    [Fact]
    public async Task ARowLeftByAnEarlierVendorOfTheSameContactIsIgnored()
    {
        SetupPexVendors(ConnectorVendor(vendorId: VendorId + 100));
        SeedCardOrder(CardAcctId);

        await RunSync();

        Assert.Empty(CardOrders);
        PexApiClient.Verify(c => c.AddVendorCard(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AddVendorCardRequestModel>(), It.IsAny<CancellationToken>()), Times.Never);
        AssertReported("has no linked PEX vendor card yet");
    }

    [Fact]
    public async Task AVendorRecreatedForAContactIsOrderedACardDespiteTheOldVendorsRow()
    {
        const int recreatedVendorId = VendorId + 100;
        SeedCardOrder(CardAcctId);
        PexApiClient
            .Setup(c => c.CreateVendor(It.IsAny<string>(), It.IsAny<CreateVendorRequestModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CreateVendorRequestModel request, CancellationToken _) =>
                new VendorModel { VendorId = recreatedVendorId, VendorName = request.VendorName, CustomId = request.CustomId, VendorStatus = VendorStatus.Pending });

        await RunSync();

        Assert.Single(CardOrders);
        VerifyCardLinked(Times.Once(), recreatedVendorId);
        Assert.Equal(CardAcctId, CardOrderStorage.Rows[(ContactId, recreatedVendorId)].CardAcctId);
        AssertSucceeded();
    }

    [Fact]
    public async Task AFailedTableReadReportsNoCardProblemAndOrdersNothing()
    {
        CardOrderStorage.FailRead = true;

        await RunSync();

        Assert.Single(CreatedBillInbox);
        Assert.Empty(CardOrders);
        AssertSucceeded();
    }

    [Fact]
    public async Task AnOrderThatCannotBeReadDoesNotStopAnotherOrdersLink()
    {
        const int brokenOrderId = 98;
        SetupTwoConnectorVendors();
        SeedCardOrder(cardAcctId: null, orderId: brokenOrderId);
        SeedCardOrder(cardAcctId: null, contactId: OtherContactId, vendorId: OtherVendorId, cardName: OtherContactName);
        PexApiClient
            .Setup(c => c.GetVendorCardOrder(It.IsAny<string>(), brokenOrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("404"));
        SetupProvisionedCard(acctId: null, CardOrderId, (OtherContactName, 322));

        await RunSync();

        VerifyCardLinked(Times.Once(), OtherVendorId, 322);
        AssertReported("Acme Supplies has no linked PEX vendor card yet");
    }

    [Fact]
    public async Task ALinkThatCannotBeRecordedDoesNotStopTheNextCard()
    {
        SetupTwoConnectorVendors();
        SeedCardOrder(cardAcctId: null);
        SeedCardOrder(cardAcctId: null, contactId: OtherContactId, vendorId: OtherVendorId, cardName: OtherContactName);
        SetupProvisionedCard(CardAcctId, CardOrderId, (OtherContactName, 322));
        CardOrderStorage.FailMarkLinkedForContacts.Add(ContactId);

        await RunSync();

        VerifyCardLinked(Times.Once());
        VerifyCardLinked(Times.Once(), OtherVendorId, 322);
        Assert.Equal(322, CardOrderStorage.Rows[(OtherContactId, OtherVendorId)].CardAcctId);
        AssertReported("Acme Supplies has no linked PEX vendor card yet");
    }

    [Fact]
    public void TheCardOrderEntitySurvivesTheTableRoundTrip()
    {
        var original = new AplosVendorCardOrderModel
        {
            PEXBusinessAcctId = BusinessId,
            AplosContactId = ContactId,
            PexVendorId = VendorId,
            CardName = ContactName,
            CardOrderId = CardOrderId,
            CardAcctId = CardAcctId,
            LinkedUtc = UtcNow,
            CreatedUtc = UtcNow
        };

        var entity = new AplosVendorCardOrderEntity(original);

        Assert.Equal((BusinessId.ToString(), $"{ContactId}-{VendorId}"), (entity.PartitionKey, entity.RowKey));
        Assert.Equivalent(original, entity.ToModel());
    }

    private void SetupTwoConnectorVendors()
    {
        SetupBills((ContactId, ContactName), (OtherContactId, OtherContactName));
        SetupPexVendors(ConnectorVendor(), ConnectorVendor(vendorId: OtherVendorId, contactId: OtherContactId, name: OtherContactName));
    }
}
