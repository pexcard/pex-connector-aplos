using Moq;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class VendorApprovalRetryTests : VendorSetupTestBase
{
    [Fact]
    public async Task ANewVendorIsApprovedAndGivenALinkedCardWithNothingReported()
    {
        await RunSync();

        PexApiClient.Verify(c => c.ApproveVendor(It.IsAny<string>(), VendorId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyCardLinked(Times.Once());
        var row = CardOrderStorage.Rows[(ContactId, VendorId)];
        Assert.Equal((CardOrderId, CardAcctId), (row.CardOrderId, row.CardAcctId));
        AssertSucceeded();
    }

    [Fact]
    public async Task AVendorLeftPendingByAMultiLevelApprovalIsImportedAndReported()
    {
        SetupApproval(VendorStatus.Pending);

        await RunSync();

        Assert.Single(CreatedBillInbox);
        AssertReported("Approve the Acme Supplies vendor in PEX");
    }

    [Fact]
    public async Task ATimedOutApprovalStillImportsTheBillAndReportsIt()
    {
        PexApiClient
            .Setup(c => c.ApproveVendor(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request timed out."));

        await RunSync();

        Assert.Equal(VendorId, Assert.Single(CreatedBillInbox).VendorId);
        AssertReported("Approve the Acme Supplies vendor in PEX");
    }

    [Fact]
    public async Task AConnectorVendorStillPendingFromAnEarlierRunGetsOneMoreApproval()
    {
        SetupPexVendors(ConnectorVendor(VendorStatus.Pending));
        SeedCardOrder(CardAcctId);

        await RunSync();

        PexApiClient.Verify(c => c.ApproveVendor(It.IsAny<string>(), VendorId, It.IsAny<CancellationToken>()), Times.Once);
        AssertSucceeded();
    }

    [Fact]
    public async Task AnOnboardedConnectorVendorIsNotApprovedAgain()
    {
        SetupPexVendors(ConnectorVendor());
        SeedCardOrder(CardAcctId);

        await RunSync();

        PexApiClient.Verify(c => c.ApproveVendor(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        AssertSucceeded();
    }

    // Draft and Closed have no ApproveVendor exit, so the vendor is only reported. A closed vendor can't be
    // approved, so it gets its own message.
    [Theory]
    [InlineData(VendorStatus.Draft, "Approve the Acme Supplies vendor in PEX")]
    [InlineData(VendorStatus.Closed, "The Acme Supplies vendor is closed in PEX; reopen it there")]
    public async Task AConnectorVendorApproveVendorCannotMoveIsReportedWithoutAnAttempt(VendorStatus status, string expectedProblem)
    {
        SetupPexVendors(ConnectorVendor(status));
        SeedCardOrder(CardAcctId);

        await RunSync();

        PexApiClient.Verify(c => c.ApproveVendor(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(CreatedBillInbox);
        AssertReported(expectedProblem);
        Assert.DoesNotContain(status is VendorStatus.Closed ? "Approve the" : "is closed in PEX", Assert.Single(HistoryRows).SyncNotes);
    }

    [Fact]
    public async Task AVendorMatchedByNameIsTheCustomersAndIsNeverApprovedOrGivenACard()
    {
        SetupPexVendors(new VendorModel { VendorId = VendorId, VendorName = ContactName, VendorStatus = VendorStatus.Pending });

        await RunSync();

        Assert.Single(CreatedBillInbox);
        PexApiClient.Verify(c => c.ApproveVendor(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(CardOrders);
        AssertSucceeded();
    }
}
