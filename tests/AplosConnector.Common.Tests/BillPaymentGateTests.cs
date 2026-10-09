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
    public async Task Gate_NoClearingAccountsConfigured_FailsAndNamesTheWaitingBills()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();

        await RunSync(useBillPay: true, achClearingAccountNumber: 0m, cardClearingAccountNumber: 0m);

        VerifyNoPayCall();
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncTypes.BillPayments, historyRow.SyncType);
        Assert.Equal(SyncStatus.Failed.ToString(), historyRow.SyncStatus);
        Assert.Contains("clearing account", historyRow.SyncNotes);
        Assert.Contains("INV-9001", historyRow.SyncNotes);
    }

    [Fact]
    public async Task Gate_NoClearingAccountsConfigured_StaysSilentWhenNoImportedBillIsWaiting()
    {
        SeedUnpaidMapping();
        _billMappingStorage.Rows.Values.Single().PaidSyncedUtc = UtcNow;

        await RunSync(useBillPay: true, achClearingAccountNumber: 0m, cardClearingAccountNumber: 0m);

        Assert.Empty(_historyRows);
        VerifyNoPayCall();
    }

    [Theory]
    [InlineData(0, 1020)]
    [InlineData(1010, 0)]
    public async Task Gate_OnlyOneClearingAccountConfigured_PaysNothingAndSaysWhatToSetUp(int achClearingAccountNumber, int cardClearingAccountNumber)
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true, achClearingAccountNumber: achClearingAccountNumber, cardClearingAccountNumber: cardClearingAccountNumber);

        VerifyNoPayCall();
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), historyRow.SyncStatus);
        Assert.Contains("ACH clearing account and vendor card clearing account", historyRow.SyncNotes);
    }

    [Fact]
    public async Task AnImportedBillIsMarkedPaidWithNoBillPaymentsToggle()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
    }

    [Fact]
    public async Task ABusinessThatNeverImportedABillDoesNoBillPaymentWork()
    {
        SetupPaidPexBill();

        await RunSync(useBillPay: true);

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
            UseBillPayEnabled = true
        };

        var service = new StorageMappingService(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        var roundTripped = service.Map(service.Map(original));
        // Last known, for a run whose business settings refresh fails: the worker loads the mapping from storage, and
        // a false here would hand bill pay card transactions back to the transaction sync.
        Assert.True(roundTripped.UseBillPayEnabled);
        Assert.Equal(AchClearingAccountNumber, roundTripped.BillPaymentsAchClearingAccountNumber);
        Assert.Equal(CardClearingAccountNumber, roundTripped.BillPaymentsCardClearingAccountNumber);

        var rebuilt = new Pex2AplosMappingModel { PEXBusinessAcctId = PexBusinessAcctId };
        rebuilt.UpdateFromSettings(original.ToStorageModel());
        Assert.Equal(AchClearingAccountNumber, rebuilt.BillPaymentsAchClearingAccountNumber);
        Assert.Equal(CardClearingAccountNumber, rebuilt.BillPaymentsCardClearingAccountNumber);
    }
}
