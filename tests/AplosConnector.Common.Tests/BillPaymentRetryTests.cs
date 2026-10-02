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

public class BillPaymentRetryTests : BillPaymentsTestBase
{

    [Fact]
    public async Task AFailingBillRecordsWhenItFirstFailedAndSaysHowLongItIsRetried()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 25.50m);

        await RunSync(useBillPay: true);

        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).FirstFailedUtc);
        Assert.Contains("Retried until 2026-10-11", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ABillStillInsideTheRetryWindowIsRetriedAndKeepsItsFirstFailure()
    {
        var firstFailedUtc = UtcNow.AddDays(-5);
        SeedUnpaidMapping(firstFailedUtc: firstFailedUtc);
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 25.50m);

        await RunSync(useBillPay: true);

        _mockAplosApiClient.Verify(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(firstFailedUtc, Assert.Single(_billMappingStorage.Rows.Values).FirstFailedUtc);
        Assert.Contains("Retried until 2026-10-06", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ABillFailingForMoreThan30DaysIsNoLongerRetried()
    {
        SeedUnpaidMapping(firstFailedUtc: UtcNow.AddDays(-31));
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.DoesNotContain(_historyRows, row => row.SyncStatus != SyncStatus.Success.ToString());
    }

    [Fact]
    public async Task ABillNoLongerRetriedStillClosesOnceItIsPaidInAplos()
    {
        SeedUnpaidMapping(firstFailedUtc: UtcNow.AddDays(-31));
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(amount: 125.50m, paid: 125.50m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.Contains(_cardTransactionNotes, note => note.Contains(AplosIntegrationService.SyncedAsBillPaymentNote));
        Assert.DoesNotContain(_historyRows, row => row.SyncStatus != SyncStatus.Success.ToString());
    }

    [Fact]
    public async Task ABillNoLongerRetriedStaysQuietWhenItsReReadFails()
    {
        SeedUnpaidMapping(firstFailedUtc: UtcNow.AddDays(-31));
        SetupPaidPexBill();
        _mockAplosApiClient
            .Setup(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Aplos unavailable"));

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.DoesNotContain(_historyRows, row => row.SyncStatus != SyncStatus.Success.ToString());
    }

    [Fact]
    public async Task ABillNoLongerRetriedStopsHoldingTheFetchWindowBack()
    {
        SeedUnpaidMapping(createdUtc: new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc), firstFailedUtc: UtcNow.AddDays(-31));
        SetupPaidPexBill();

        await RunSync(useBillPay: true);

        _mockPexApiClient.Verify(client => client.GetBillPayments(
            It.IsAny<string>(),
            It.Is<BillPaymentListRequestModel>(r => r.CreatedDateFrom <= new DateTime(2026, 7, 1)),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ACardPaidBillUnlinkedPastTheGracePeriodClosesOnceAplosShowsItPaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, linkedCardCharge: false, payoutDate: UtcNow.AddDays(-4));
        SetupLivePayable(amount: 125.50m, paid: 125.50m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(_historyRows).SyncStatus);
    }

    [Fact]
    public async Task AnImportLateInTheEstDayReachesBackToThatDaysEstMidnight()
    {
        // 23:30 Eastern on 31 May, which is already 1 June in UTC.
        SeedUnpaidMapping(createdUtc: new DateTime(2026, 6, 1, 3, 30, 0, DateTimeKind.Utc));
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        _mockPexApiClient.Verify(client => client.GetBillPayments(
            It.IsAny<string>(),
            It.Is<BillPaymentListRequestModel>(r => r.CreatedDateFrom == new DateTime(2026, 5, 31, 4, 0, 0, DateTimeKind.Utc)),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ABillImportedBeforeTheSyncWindowIsStillLookedUp()
    {
        SeedUnpaidMapping(createdUtc: new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        _mockPexApiClient.Verify(client => client.GetBillPayments(
            It.IsAny<string>(),
            // EST midnight of 1 June, as a UTC instant.
            It.Is<BillPaymentListRequestModel>(r => r.CreatedDateFrom == new DateTime(2026, 6, 1, 4, 0, 0, DateTimeKind.Utc)),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.Single(_payments);
    }
}
