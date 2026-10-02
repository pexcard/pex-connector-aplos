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

public class BillPaymentRailTests : BillPaymentsTestBase
{
    [Fact]
    public void PaidDate_IsAnchoredToTheEstCalendarDay()
    {
        // 23:30 Eastern on 10 September, which is already 11 September in UTC.
        var paymentRequest = new BillPaymentRequestModel { PayoutDate = new DateTimeOffset(2026, 9, 11, 3, 30, 0, TimeSpan.Zero) };

        Assert.Equal(new DateOnly(2026, 9, 10), AplosIntegrationService.GetBillPaymentPaidDate(paymentRequest));
    }

    [Fact]
    public async Task PayingAnImportedBillByAchMarksTheAplosPayablePaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        var payment = Assert.Single(_payments);
        Assert.Equal(new DateOnly(2026, 9, 10), payment.PaidDate);
        Assert.Equal(CashAccountNumber, payment.CashAccountNumber);

        var row = Assert.Single(_billMappingStorage.Rows.Values);
        Assert.Equal(UtcNow, row.PaidSyncedUtc);

        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncTypes.BillPayments, historyRow.SyncType);
        Assert.Equal(SyncStatus.Success.ToString(), historyRow.SyncStatus);
        Assert.Equal(1, historyRow.SyncedRecords);
    }

    [Theory]
    [InlineData(PaymentStatusTrigger.Returned)]
    [InlineData(PaymentStatusTrigger.Cancelled)]
    [InlineData(PaymentStatusTrigger.Settling)]
    public async Task AnAchPaymentThatIsNotSettledLeavesTheAplosPayableOpen(PaymentStatusTrigger trigger)
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupAchPayment(trigger);
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }

    [Fact]
    public async Task ASettledAchPaymentMarksTheAplosPayablePaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupAchPayment(PaymentStatusTrigger.Settled);
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }

    [Fact]
    public async Task AVendorCardPaymentNeverReadsAchPayments()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        _mockPexApiClient.Verify(client => client.GetPayments(
            It.IsAny<string>(), It.IsAny<PaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PayingAnImportedBillByVendorCardMarksTheAplosPayablePaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.Equal(1, Assert.Single(_historyRows).SyncedRecords);
    }

    [Fact]
    public async Task ABillWithNoPayoutDateIsNotPaidBack()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(hasPayoutDate: false);
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Equal(0, Assert.Single(_historyRows).SyncedRecords);
    }

    [Fact]
    public async Task ThePayerNameIsWrittenBackOntoThePexBill()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        var note = Assert.Single(_relationshipNotes);
        Assert.Contains(AplosIntegrationService.GetAplosBillPaidNote(AplosPayableId), note);
        Assert.Contains("Dana Ross", note);
    }

    [Fact]
    public void APaymentRequestWithNoUserNameStillGetsAPayer()
    {
        Assert.Equal("PEX", AplosIntegrationService.GetBillPaymentPayerName(new BillPaymentRequestModel()));
        Assert.Equal("Dana", AplosIntegrationService.GetBillPaymentPayerName(new BillPaymentRequestModel { UserFirstName = "Dana" }));
    }
}
