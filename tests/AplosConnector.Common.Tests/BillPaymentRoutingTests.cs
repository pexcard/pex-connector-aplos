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

public class BillPaymentRoutingTests : BillPaymentsTestBase
{
    [Fact]
    public async Task PayingAnAplosBillByVendorCardMarksThePayablePaidAndTheCardCharge()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: 0m);

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);

        var marker = Assert.Single(_cardTransactionNotes);
        Assert.Contains(AplosIntegrationService.SyncedAsBillPaymentNote, marker);
        Assert.Contains("Paid via Card", marker);
    }

    [Fact]
    public async Task ACardChargeAlreadyBookedAsAPurchaseFlagsTheBillInsteadOfMarkingItPaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: 0m);
        _cardTransactionNotes.Add($"Synced transaction #{VendorCardTransactionId} to Aplos on 2026-09-10T00:00:00.0000000Z");

        await RunSync(useBillPay: true);

        Assert.Empty(_payments);
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        var history = Assert.Single(_historyRows, row => row.SyncType == SyncTypes.BillPayments);
        Assert.Equal(SyncStatus.Failed.ToString(), history.SyncStatus);
        Assert.Contains("INV-9001", history.SyncNotes);
        Assert.Contains("already booked", history.SyncNotes);
    }

    [Fact]
    public async Task ACardPaidBillWithNoSettlementLinkIsNotMarkedPaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, linkedCardCharge: false);
        SetupLivePayable(BillAmount, paid: 0m);

        await RunSync(useBillPay: true);

        Assert.Empty(_payments);
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.DoesNotContain(_cardTransactionNotes, note => note.Contains(AplosIntegrationService.SyncedAsBillPaymentNote));
    }

    [Fact]
    public async Task ACardPaidBillStillUnlinkedAfterTheGracePeriodAsksTheCustomerToMarkItPaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, linkedCardCharge: false, payoutDate: PayoutDate.AddDays(-3));
        SetupLivePayable(BillAmount, paid: 0m);

        await RunSync(useBillPay: true);

        Assert.Empty(_payments);
        var history = Assert.Single(_historyRows, row => row.SyncType == SyncTypes.BillPayments);
        Assert.Equal(SyncStatus.Failed.ToString(), history.SyncStatus);
        Assert.Contains("Mark it paid in Aplos", history.SyncNotes);
        Assert.DoesNotContain("retried", history.SyncNotes);
    }

    [Fact]
    public async Task ACardPaidBillUnlinkedWithinTheGracePeriodIsRetried()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, linkedCardCharge: false);
        SetupLivePayable(BillAmount, paid: 0m);

        await RunSync(useBillPay: true);

        var history = Assert.Single(_historyRows, row => row.SyncType == SyncTypes.BillPayments);
        Assert.Contains("retried on the next sync", history.SyncNotes);
    }

    [Fact]
    public async Task AFailedMarkerWriteLeavesThePayableUnpaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: 0m);
        _mockPexApiClient
            .Setup(client => client.AddTransactionNote(It.IsAny<string>(), It.IsAny<TransactionModel>(), It.Is<string>(note => note.Contains(AplosIntegrationService.SyncedAsBillPaymentNote)), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PEX unavailable"));

        await RunSync(useBillPay: true);

        Assert.Empty(_payments);
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        var history = Assert.Single(_historyRows, row => row.SyncType == SyncTypes.BillPayments);
        Assert.Equal(SyncStatus.Failed.ToString(), history.SyncStatus);
        Assert.Contains("INV-9001", history.SyncNotes);
    }

    [Fact]
    public async Task AFailedPayCallLeavesTheCardChargeMarked()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: 0m);
        _mockAplosApiClient
            .Setup(client => client.PayPayable(It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AplosApiException(new AplosApiErrorResponse { Status = 503 }));

        await RunSync(useBillPay: true);

        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.Contains(_cardTransactionNotes, note => note.Contains(AplosIntegrationService.SyncedAsBillPaymentNote));
    }

    [Fact]
    public async Task ARetriedPayDoesNotWriteTheMarkerTwice()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: 0m);
        _cardTransactionNotes.Add($"{AplosIntegrationService.SyncedAsBillPaymentNote} #{PexBillId}. Paid via Card");

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        Assert.Single(_cardTransactionNotes);
    }

    [Fact]
    public async Task AnAchPaidAplosBillIsMarkedPaidWithoutAnyCardLookup()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupLivePayable(BillAmount, paid: 0m);

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        Assert.Empty(_cardTransactionNotes);
        _mockPexApiClient.Verify(client => client.GetCardholderTransaction(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("Paid via ACH", Assert.Single(_relationshipNotes));
    }

    [Fact]
    public async Task AnAlreadyMarkedPaidAplosBillIsNotPaidOrBookedAgain()
    {
        SeedPaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: BillAmount);
        _cardTransactionNotes.Add($"{AplosIntegrationService.SyncedAsBillPaymentNote} #{PexBillId}. Paid via Card");

        await RunSync(useBillPay: true);

        Assert.Empty(_payments);
    }

    [Fact]
    public async Task AnAplosImportWithNoUsableMappingRowIsFlagged()
    {
        // The business has imported other bills; only this one's row is missing.
        _billMappingStorage.Seed(new AplosBillMappingModel { PEXBusinessAcctId = PexBusinessAcctId, AplosPayableId = "8001", PexBillInboxId = 999, MetadataRelationId = 999, Amount = 10m });
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount, metadata: new PaymentRequestMetadataModel
        {
            Notes = [new TransactionNoteModel { NoteText = $"{AplosIntegrationService.GetAplosBillSyncedNote(AplosPayableId)} with ID #100 on 2026-09-02T00:00:00.0000000Z." }]
        });

        await RunSync(useBillPay: true);

        Assert.Empty(_payments);
        var history = Assert.Single(_historyRows, row => row.SyncType == SyncTypes.BillPayments);
        Assert.Equal(SyncStatus.Failed.ToString(), history.SyncStatus);
        Assert.Contains("INV-9001", history.SyncNotes);
        Assert.Contains("in Aplos", history.SyncNotes);
    }
}
