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
    public async Task Gate_NoCashAccountConfigured_FailsAndNamesTheWaitingBills()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();

        await RunSync(useBillPay: true, cashAccountNumber: 0m);

        VerifyNoPayCall();
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncTypes.BillPayments, historyRow.SyncType);
        Assert.Equal(SyncStatus.Failed.ToString(), historyRow.SyncStatus);
        Assert.Contains("not available", historyRow.SyncNotes);
        Assert.Contains("INV-9001", historyRow.SyncNotes);
    }

    [Fact]
    public async Task Gate_NoCashAccountConfigured_StaysSilentWhenNoImportedBillIsWaiting()
    {
        SeedUnpaidMapping();
        _billMappingStorage.Rows.Values.Single().PaidSyncedUtc = UtcNow;

        await RunSync(useBillPay: true, cashAccountNumber: 0m);

        Assert.Empty(_historyRows);
        VerifyNoPayCall();
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
    public void TheCashAccountSurvivesTheStorageRoundTrip()
    {
        var original = new Pex2AplosMappingModel
        {
            PEXBusinessAcctId = PexBusinessAcctId,
            BillPaymentsAplosCashAccountNumber = CashAccountNumber
        };

        var service = new StorageMappingService(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        var roundTripped = service.Map(service.Map(original));
        Assert.Equal(CashAccountNumber, roundTripped.BillPaymentsAplosCashAccountNumber);

    }

    [Fact]
    public void ASettingsSaveLeavesTheCashAccountAlone()
    {
        var stored = new Pex2AplosMappingModel { PEXBusinessAcctId = PexBusinessAcctId, BillPaymentsAplosCashAccountNumber = CashAccountNumber };

        stored.UpdateFromSettings(new MappingSettingsModel());

        Assert.Equal(CashAccountNumber, stored.BillPaymentsAplosCashAccountNumber);
        Assert.Null(typeof(MappingSettingsModel).GetProperty(nameof(Pex2AplosMappingModel.BillPaymentsAplosCashAccountNumber)));
    }
}
