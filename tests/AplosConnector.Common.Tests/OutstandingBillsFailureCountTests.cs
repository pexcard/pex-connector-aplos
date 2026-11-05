using AplosConnector.Common.Enums;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class OutstandingBillsFailureCountTests : VendorSetupTestBase
{
    [Fact]
    public async Task ACleanImportWritesNoNotes()
    {
        await RunSync();

        AssertSucceeded();
        Assert.Equal(string.Empty, Assert.Single(HistoryRows).SyncNotes);
    }

    [Fact]
    public async Task TwoFailedBillsAreCountedBeforeTheirReasons()
    {
        SetupBills((ContactId, ContactName), (ContactId + 1, "Beta Supplies"));
        SetupContactState("Narnia");

        await RunSync();

        var notes = Assert.Single(HistoryRows).SyncNotes;
        Assert.StartsWith("Failed to sync 2 PEX bills from Aplos.\nBill INV-9001: ", notes);
        Assert.Contains("\nBill INV-9002: ", notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task ABillWithNoReferenceIsNamedByItsVendorDateAndAmount(string referenceNumber)
    {
        SetupBills(_ => referenceNumber, (ContactId, ContactName));
        SetupContactState("Narnia");

        await RunSync();

        Assert.StartsWith("Failed to sync 1 PEX bill from Aplos.\nBill Acme Supplies, 2026-09-01, $10.00: ", Assert.Single(HistoryRows).SyncNotes);
    }

    [Fact]
    public async Task ABillWithNoReferenceAndNoContactNameIsNamedByItsDateAndAmount()
    {
        SetupBills(_ => "", (ContactId, null));
        SetupContactState("Narnia");

        await RunSync();

        Assert.StartsWith("Failed to sync 1 PEX bill from Aplos.\nBill 2026-09-01, $10.00: ", Assert.Single(HistoryRows).SyncNotes);
    }

    [Fact]
    public async Task AnImportedBillWithNoReferenceStoresItsLabelForTheBillPaymentNotes()
    {
        SetupBills(_ => "", (ContactId, ContactName));

        await RunSync();

        Assert.Equal("Acme Supplies, 2026-09-01, $10.00", Assert.Single(StoredBillMappings).AplosReferenceNumber);
    }

    [Fact]
    public async Task AFailedRunWithNoBillToBlameFailsWithoutABillCount()
    {
        PexApiClient
            .Setup(c => c.GetVendors(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PEX unavailable"));

        await RunSync();

        var row = Assert.Single(HistoryRows);
        Assert.Equal(SyncStatus.Failed.ToString(), row.SyncStatus);
        Assert.Equal("PEX unavailable", row.SyncNotes);
    }
}
