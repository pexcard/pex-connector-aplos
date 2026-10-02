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

public class BillPaymentReReadTests : BillPaymentsTestBase
{

    [Fact]
    public async Task ThePayableIsRefetchedFromAplosImmediatelyBeforeTheDecision()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        _mockAplosApiClient.Verify(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_payments);
    }

    [Fact]
    public async Task APayableAlreadyPaidInAplosSinceImportIsSkipped()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 125.50m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Success.ToString(), historyRow.SyncStatus);
        Assert.Equal(0, historyRow.SyncedRecords);
    }

    [Fact]
    public async Task ACardPaidBillAlreadyPaidInAplosStaysOpenWhilePexHasNotLinkedItsCharge()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, linkedCardCharge: false);
        SetupLivePayable(amount: 125.50m, paid: 125.50m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.Contains("not linked the card charge", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ACardPaidBillAlreadyPaidInAplosStaysOpenWhenItsChargeCannotBeMarked()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(amount: 125.50m, paid: 125.50m);
        _mockPexApiClient
            .Setup(client => client.AddTransactionNote(It.IsAny<string>(), It.IsAny<TransactionModel>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PEX unavailable"));

        await RunSync(useBillPay: true);

        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.Contains("could not be written on its card charge", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task AnUnreadablePexBillDoesNotHoldBackTheOthers()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);
        _mockPexApiClient
            .Setup(client => client.GetBillPayments(It.IsAny<string>(), It.IsAny<BillPaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillPaymentListResponseModel
            {
                Items =
                [
                    new BillPaymentModel { BillId = 999, BillRefNo = "INV-BROKEN", Amount = 10m, PaymentRequestStatus = PaymentRequestStatus.Closed },
                    new BillPaymentModel { BillId = PexBillId, BillRefNo = "INV-9001", Amount = 125.50m, PaymentRequestStatus = PaymentRequestStatus.Closed }
                ],
                PageInfo = new PageInfoModel { Page = 1, PageSize = 50, TotalItems = 2 }
            });
        _mockPexApiClient
            .Setup(client => client.GetBillPaymentRequest(It.IsAny<string>(), 999, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PEX unavailable"));

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Partial.ToString(), historyRow.SyncStatus);
        Assert.Contains("INV-BROKEN", historyRow.SyncNotes);
    }

    [Fact]
    public async Task APayablePartiallyPaidInAplosSinceImportFailsTheBillAndSaysWhatToDo()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 25.50m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), historyRow.SyncStatus);
        Assert.Contains("INV-9001", historyRow.SyncNotes);
        Assert.Contains("partly paid (25.50 of 125.50)", historyRow.SyncNotes);
        Assert.Contains("Record it on the bill in Aplos", historyRow.SyncNotes);
    }

    [Fact]
    public async Task ADeletedPayableFailsItsOwnBillAndSaysItMayHaveBeenDeleted()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        _mockAplosApiClient
            .Setup(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AplosApiException(new AplosApiErrorResponse
            {
                Status = 500,
                Exception = new AplosApiErrorDetail { Code = 5000, Message = "Service Exception: UNKNOWN" }
            }));

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), historyRow.SyncStatus);
        Assert.Contains("INV-9001", historyRow.SyncNotes);
        Assert.Contains("may have been deleted", historyRow.SyncNotes);
    }

    [Fact]
    public async Task APexAmountThatDoesNotMatchTheAplosBalanceFailsTheBillWithAnActionableNote()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(amount: 100m);
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), historyRow.SyncStatus);
        Assert.Contains("INV-9001", historyRow.SyncNotes);
        Assert.Contains("125.50", historyRow.SyncNotes);
        Assert.Contains("in Aplos", historyRow.SyncNotes);
    }

    [Fact]
    public async Task APayableThatAnswers405AndReadsPaidIsRecordedAsPaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayableSequence((amount: 125.50m, paid: 0m), (amount: 125.50m, paid: 125.50m));
        SetupPayCallAnswers405();

        await RunSync(useBillPay: true);

        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(_historyRows).SyncStatus);
    }

    [Fact]
    public async Task APayableThatAnswers405ButStillReadsUnpaidFailsTheBill()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayableSequence((amount: 125.50m, paid: 0m), (amount: 125.50m, paid: 0m));
        SetupPayCallAnswers405();

        await RunSync(useBillPay: true);

        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), historyRow.SyncStatus);
        Assert.Contains("Aplos refused to mark payable 9001 paid (Caller requested a resource that is not available.)", historyRow.SyncNotes);
    }

    [Fact]
    public async Task AnAplosRejectionReachesTheHistoryInAplossOwnWords()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);
        _mockAplosApiClient
            .Setup(client => client.PayPayable(It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AplosApiException(new AplosApiErrorResponse
            {
                Status = 422,
                Exception = new AplosApiErrorDetail { Code = 2001, Message = "Cash account 1000 is inactive." }
            }));

        await RunSync(useBillPay: true);

        var historyRow = Assert.Single(_historyRows);
        Assert.Contains("INV-9001: Cash account 1000 is inactive.", historyRow.SyncNotes);
        Assert.DoesNotContain("Exception of type", historyRow.SyncNotes);
    }

    [Fact]
    public async Task CancellingTheRunStopsItInsteadOfFailingEveryBill()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        using var cancellation = new CancellationTokenSource();
        _mockAplosApiClient
            .Setup(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()))
            .Callback(() => cancellation.Cancel())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunSync(useBillPay: true, cancellationToken: cancellation.Token));

        Assert.Empty(_historyRows);
        VerifyNoPayCall();
    }

    [Theory]
    [InlineData(12550, 0, AplosPayableAction.Pay)]
    [InlineData(-12550, 0, AplosPayableAction.Pay)]
    [InlineData(12550, 12550, AplosPayableAction.SkipAlreadyPaid)]
    [InlineData(-12550, -12550, AplosPayableAction.SkipAlreadyPaid)]
    [InlineData(12550, 2550, AplosPayableAction.SkipPartiallyPaid)]
    [InlineData(-12550, -2550, AplosPayableAction.SkipPartiallyPaid)]
    public void TheDecisionRuleIgnoresTheSignAplosReportsTheAmountWith(int amount, int paid, AplosPayableAction expected)
    {
        var payable = new AplosApiPayableDetail { Id = AplosPayableId, Amount = amount / 100m, PaidAmount = paid / 100m };

        Assert.Equal(expected, AplosPayableFilter.DetermineAction(payable));
    }
}
