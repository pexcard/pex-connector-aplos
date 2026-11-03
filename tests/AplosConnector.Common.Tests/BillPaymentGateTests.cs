using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Const;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Storage;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class BillPaymentGateTests : BillPaymentsTestBase
{
    [Fact]
    public async Task Gate_BillPayDisabledForTheBusiness_DoesNothing()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();

        await RunSync(useBillPay: false);

        Assert.Empty(_historyRows);
        VerifyNoPayCall();
    }

    [Fact]
    public async Task Gate_NoBillPayAccountsConfigured_FailsAndNamesTheWaitingBills()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();

        await RunSync(useBillPay: true, achClearingAccountNumber: 0m, pexFundsAccountNumber: 0m);

        VerifyNoPayCall();
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncTypes.BillPayments, historyRow.SyncType);
        Assert.Equal(SyncStatus.Failed.ToString(), historyRow.SyncStatus);
        Assert.Contains("ACH bank account", historyRow.SyncNotes);
        Assert.Contains("INV-9001", historyRow.SyncNotes);
    }

    [Fact]
    public async Task Gate_NoBillPayAccountsConfigured_IsNotReportedOnAFailedRunWithTheImportOff()
    {
        SeedUnpaidMapping();
        _mockPexApiClient
            .Setup(client => client.GetBillPayments(It.IsAny<string>(), It.IsAny<BillPaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("PEX is unavailable."));

        await RunSync(useBillPay: true, achClearingAccountNumber: 0m, pexFundsAccountNumber: 0m, configure: mapping => mapping.SyncOutstandingBills = false);

        var historyRow = Assert.Single(_historyRows);
        Assert.Contains("PEX is unavailable.", historyRow.SyncNotes);
        Assert.DoesNotContain("ACH bank account", historyRow.SyncNotes);
    }

    [Fact]
    public async Task Gate_NoBillPayAccountsConfigured_ReportsNoFailureWhenNoImportedBillIsWaiting()
    {
        SeedUnpaidMapping();
        _billMappingStorage.Rows.Values.Single().PaidSyncedUtc = UtcNow;

        await RunSync(useBillPay: true, achClearingAccountNumber: 0m, pexFundsAccountNumber: 0m);

        var historyRow = Assert.Single(_historyRows);
        Assert.Equal((SyncStatus.Success.ToString(), ""), (historyRow.SyncStatus, historyRow.SyncNotes));
        VerifyNoPayCall();
    }

    // The clearing account is not part of the gate: whether card bills need it depends on the PEX funds account's type,
    // which is read at pay time (153880).
    [Theory]
    [InlineData(0, 1030)]
    [InlineData(1010, 0)]
    public async Task Gate_OnlyOneRailsAccountConfigured_PaysNothingAndSaysWhatToSetUp(int achClearingAccountNumber, int pexFundsAccountNumber)
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true, achClearingAccountNumber: achClearingAccountNumber, pexFundsAccountNumber: pexFundsAccountNumber);

        VerifyNoPayCall();
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), historyRow.SyncStatus);
        Assert.Contains("ACH bank account for bill payments and the Aplos account that represents your PEX funds", historyRow.SyncNotes);
    }

    [Fact]
    public async Task AnImportedBillWaitsWhileSyncBillPaymentsIsOffAndIsPaidOnceItIsBackOn()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true, syncBillPayments: false, configure: mapping => mapping.SyncOutstandingBills = true);
        Assert.Empty(_payments);

        await RunSync(useBillPay: true, syncBillPayments: true);
        Assert.Single(_payments);
    }

    [Fact]
    public async Task ABusinessThatNeverImportedABillDoesNoBillPaymentWork()
    {
        SetupPaidPexBill();

        await RunSync(useBillPay: true, syncBillPayments: false);

        Assert.Empty(_historyRows);
        _mockPexApiClient.Verify(client => client.GetBillPayments(
            It.IsAny<string>(), It.IsAny<BillPaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task APexBillWithNoAplosMappingIsNeverMarkedPaid()
    {
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        _mockAplosApiClient.Verify(client => client.GetPayable(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheClearingAccountsSurviveTheStorageAndSettingsRoundTrips()
    {
        var original = new Pex2AplosMappingModel
        {
            PEXBusinessAcctId = PexBusinessAcctId,
            BillPaymentsAchClearingAccountNumber = AchClearingAccountNumber,
            BillPaymentsCardClearingAccountNumber = CardClearingAccountNumber,
            UseBillPayEnabled = true,
            SyncBillPayments = true
        };

        var service = new StorageMappingService(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        var roundTripped = service.Map(service.Map(original));
        // Last known, for a run whose business settings refresh fails: the worker loads the mapping from storage, and
        // a false here would hand bill pay card transactions back to the transaction sync.
        Assert.True(roundTripped.UseBillPayEnabled);
        AssertBillPaymentSettings(roundTripped);

        var rebuilt = new Pex2AplosMappingModel { PEXBusinessAcctId = PexBusinessAcctId };
        rebuilt.UpdateFromSettings(original.ToStorageModel());
        AssertBillPaymentSettings(rebuilt);
    }

    private static void AssertBillPaymentSettings(Pex2AplosMappingModel mapping)
    {
        Assert.True(mapping.SyncBillPayments);
        Assert.Equal(AchClearingAccountNumber, mapping.BillPaymentsAchClearingAccountNumber);
        Assert.Equal(CardClearingAccountNumber, mapping.BillPaymentsCardClearingAccountNumber);
    }
}
