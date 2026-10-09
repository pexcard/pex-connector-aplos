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
        Assert.Equal(AchClearingAccountNumber, payment.CashAccountNumber);

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

        Assert.Equal(CardClearingAccountNumber, Assert.Single(_payments).CashAccountNumber);
        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.Equal(1, Assert.Single(_historyRows).SyncedRecords);
    }

    // Rail-specific clearing accounts (148049). Aplos's pay call has no payment-method field, so the account it
    // credits is the only reportable record of how PEX paid.

    [Fact]
    public async Task AClearingAccountMissingFromAplosFailsTheBillNamingTheAccountAndTheSetting()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupLivePayable(amount: 125.50m, paid: 0m);
        SetupClearingAccount(null);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), historyRow.SyncStatus);
        Assert.Contains("INV-9001", historyRow.SyncNotes);
        Assert.Contains($"ACH clearing account {AchClearingAccountNumber} could not be found in Aplos", historyRow.SyncNotes);
        Assert.Contains("connector settings", historyRow.SyncNotes);
    }

    // A settings problem is the customer's to fix, so it must not start the bill's 30-day retry window.
    [Fact]
    public async Task ABrokenClearingAccountDoesNotStartTheRetryWindow()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupLivePayable(amount: 125.50m, paid: 0m);
        SetupClearingAccount(null);

        await RunSync(useBillPay: true);

        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).FirstFailedUtc);
    }

    [Fact]
    public async Task AClearingAccountAplosAnswersWithAServerErrorIsRetriedNotReportedMissing()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupLivePayable(amount: 125.50m, paid: 0m);
        _mockAplosApiClient
            .Setup(client => client.GetAccount(It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AplosApiException(new AplosApiErrorResponse
            {
                Status = 500,
                Exception = new AplosApiErrorDetail { Code = 5000, Message = "Service Exception: UNKNOWN" }
            }));

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        var notes = Assert.Single(_historyRows).SyncNotes;
        Assert.Contains("could not be read from Aplos; retried on the next sync", notes);
        Assert.DoesNotContain("could not be found", notes);
    }

    // Any other failure is reported against that rail's bills, so the other rail's bills still pay.
    [Fact]
    public async Task AClearingAccountThatCannotBeReadFailsOnlyItsRailsBills()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupLivePayable(amount: 125.50m, paid: 0m);
        _mockAplosApiClient
            .Setup(client => client.GetAccount(It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request timed out."));

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Contains($"Bill INV-9001: the ACH clearing account {AchClearingAccountNumber} could not be read from Aplos", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ADisabledClearingAccountFailsTheBill()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupLivePayable(amount: 125.50m, paid: 0m);
        SetupClearingAccount(new AplosApiAccountDetail { Name = "ACH Clearing", Category = "asset", IsEnabled = false });

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Contains($"ACH clearing account {AchClearingAccountNumber} (ACH Clearing) is disabled in Aplos", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task AClearingAccountThatIsNoLongerAnAssetAccountFailsTheBill()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupLivePayable(amount: 125.50m, paid: 0m);
        SetupClearingAccount(new AplosApiAccountDetail { Name = "Accounts Payable", Category = "liability", IsEnabled = true });

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Contains("is no longer an asset account in Aplos", Assert.Single(_historyRows).SyncNotes);
    }

    // Checked before the card charge is marked: a marker written for a payment that is then refused would hide the
    // charge from the transaction sync with nothing booked in its place.
    [Fact]
    public async Task ABrokenCardClearingAccountLeavesTheCardChargeUnmarked()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(amount: 125.50m, paid: 0m);
        SetupClearingAccount(null);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Contains($"vendor card clearing account {CardClearingAccountNumber}", Assert.Single(_historyRows).SyncNotes);
        Assert.Empty(_cardTransactionNotes);
        _mockAplosApiClient.Verify(client => client.GetPayable(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnlyTheClearingAccountOfTheRailPexPaidByIsChecked()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        _mockAplosApiClient.Verify(client => client.GetAccount(AchClearingAccountNumber, It.IsAny<CancellationToken>()), Times.Once);
        _mockAplosApiClient.Verify(client => client.GetAccount(CardClearingAccountNumber, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task APaymentMethodWithNoClearingAccountStartsTheRetryWindow()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.NonPlatform);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        _mockAplosApiClient.Verify(client => client.GetAccount(It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).FirstFailedUtc);
        Assert.Contains("no Aplos clearing account", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task APaymentMethodWithNoClearingAccountClosesOncePaidInAplosAfterTheRetryWindow()
    {
        SeedUnpaidMapping(firstFailedUtc: UtcNow.AddDays(-31));
        SetupPaidPexBill(PayeeFundsDestinationType.NonPlatform);
        SetupLivePayable(amount: 125.50m, paid: 125.50m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.DoesNotContain(_historyRows, row => row.SyncStatus != SyncStatus.Success.ToString());
    }

    [Fact]
    public async Task AnAlreadyPaidBillDoesNotCheckTheClearingAccounts()
    {
        SeedPaidMapping();
        SetupPaidPexBill();

        await RunSync(useBillPay: true);

        _mockAplosApiClient.Verify(client => client.GetAccount(It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
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
