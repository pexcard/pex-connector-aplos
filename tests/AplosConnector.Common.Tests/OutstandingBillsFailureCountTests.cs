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
        Assert.StartsWith("2 bills from Aplos need attention. Bill INV-9001: ", notes);
        Assert.Contains("Bill INV-9002: ", notes);
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
