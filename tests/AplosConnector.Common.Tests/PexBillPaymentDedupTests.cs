using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Services;
using Moq;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class PexBillPaymentDedupTests : PexBillPaymentsTestBase
{
    [Fact]
    public async Task ABillAlreadyInAplosIsNotPostedAgainAndGetsItsLostNoteBack()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount);
        _aplosTransactions.Add(new AplosApiTransactionDetail { Id = 77000, Note = $"PEX-{PexBillId} | {PayeeName} | Paid via ACH | {AplosNoteId(PexBillId)}" });

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.StartsWith($"Synced bill payment #{PexBillId} to Aplos on", Assert.Single(_relationshipNotes));
        Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(_historyRows).SyncStatus);
    }

    [Fact]
    public async Task APurchaseEndingInTheSameNumberDoesNotHideTheBill()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount);
        _aplosTransactions.Add(new AplosApiTransactionDetail { Id = 66000, Note = $"Dana Ross | {PexBillId}" });

        await RunPexBillSync();

        Assert.Single(_postedTransactions);
    }

    [Fact]
    public async Task ABillCarryingItsSyncedNoteIsSkipped()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount, billNotes: [$"Synced bill payment #{PexBillId} to Aplos on 2026-09-11T00:00:00.0000000Z. Aplos transaction 77000."]);

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.Empty(_relationshipNotes);
    }

    [Theory]
    [InlineData("Synced transaction #987654 to Aplos on 2026-09-10T00:00:00.0000000Z")]
    [InlineData("Synced with Aplos on 2024-01-01")]
    public async Task ACardChargeBookedAsAPurchaseSkipsTheBillOnceWithItsNote(string purchaseNote)
    {
        AddPexBill(PayeeFundsDestinationType.VendorVirtualCard);
        _cardTransactionNotes.Add(purchaseNote);

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.DoesNotContain(_cardTransactionNotes, note => note.Contains(AplosIntegrationService.SyncedAsBillPaymentNote));
        Assert.Equal($"Synced to Aplos as a purchase #{VendorCardTransactionId}", Assert.Single(_relationshipNotes));
        var history = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Success.ToString(), history.SyncStatus);
        Assert.Contains($"Bill PEX-{PexBillId}: Skipped: already synced to Aplos as purchase #{VendorCardTransactionId}.", history.SyncNotes);
    }

    [Fact]
    public async Task ACardChargeInAplosAsAPurchaseSkipsTheBillEvenWithItsNoteGone()
    {
        AddPexBill(PayeeFundsDestinationType.VendorVirtualCard);
        _aplosTransactions.Add(new AplosApiTransactionDetail { Id = 66000, Note = $"Acme Supplies | {VendorCardTransactionId}" });

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.Empty(_billMappingStorage.Rows);
        Assert.Equal($"Synced to Aplos as a purchase #{VendorCardTransactionId}", Assert.Single(_relationshipNotes));
    }

    [Fact]
    public async Task ABillFoundInAplosRecordsItsClaimAsPosted()
    {
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        _billMappingStorage.Rows[$"{PexBusinessAcctId}|pex-{PexBillId}"] = new AplosBillMappingModel { PEXBusinessAcctId = PexBusinessAcctId, PexPaymentRequestId = PexBillId, CardTransactionId = VendorCardTransactionId };
        _aplosTransactions.Add(new AplosApiTransactionDetail { Id = 77000, Note = $"PEX-{PexBillId} | {PayeeName} | Paid via Card | {AplosNoteId(PexBillId)}" });

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }

    [Fact]
    public async Task WithSyncBillPaymentsOffAClaimThatNeverReachedAplosIsReported()
    {
        _billMappingStorage.Rows[$"{PexBusinessAcctId}|pex-{PexBillId}"] = new AplosBillMappingModel { PEXBusinessAcctId = PexBusinessAcctId, PexPaymentRequestId = PexBillId, CardTransactionId = VendorCardTransactionId };
        _billMappingStorage.Rows[$"{PexBusinessAcctId}|pex-{PexBillId + 1}"] = new AplosBillMappingModel { PEXBusinessAcctId = PexBusinessAcctId, PexPaymentRequestId = PexBillId + 1, CardTransactionId = VendorCardTransactionId + 1, PaidSyncedUtc = UtcNow };

        await RunSync(useBillPay: true, syncBillPayments: false);

        var history = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), history.SyncStatus);
        Assert.Contains($"PEX bill payment {PexBillId}: its card charge (PEX transaction {VendorCardTransactionId}) is held for a bill payment that never reached Aplos. Turn Sync bill payments back on to post it.", history.SyncNotes);
        // Booking it by hand would be posted again once the toggle is back on: the bill stage only knows its own Aplos note.
        Assert.DoesNotContain("by hand", history.SyncNotes);
        Assert.DoesNotContain($"PEX bill payment {PexBillId + 1}:", history.SyncNotes);
    }

    [Fact]
    public async Task ABillCarryingThePurchaseNoteIsNotCheckedAgain()
    {
        AddPexBill(PayeeFundsDestinationType.VendorVirtualCard, billNotes: [$"Synced to Aplos as a purchase #{VendorCardTransactionId}"]);

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.DoesNotContain("Skipped", Assert.Single(_historyRows).SyncNotes);
        _mockPexApiClient.Verify(client => client.GetCardholderPurchase(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ARetriedPostDoesNotWriteTheMarkerTwice()
    {
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        _cardTransactionNotes.Add($"{AplosIntegrationService.SyncedAsBillPaymentNote} #{PexBillId}. Paid via Card");

        await RunPexBillSync();

        Assert.Single(_postedTransactions);
        Assert.Single(_cardTransactionNotes);
    }

    [Fact]
    public async Task ALostBillNoteIsRecoveredFromAplosOnTheNextRun()
    {
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        var noteWritesFailing = true;
        _mockPexApiClient
            .Setup(client => client.AddTransactionRelationshipNote(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, long, string, CancellationToken>((_, _, noteText, _) =>
            {
                if (noteWritesFailing) throw new System.Exception("PEX unavailable");
                _relationshipNotes.Add(noteText);
            })
            .Returns(Task.CompletedTask);

        await RunPexBillSync();
        Assert.Empty(_relationshipNotes);
        noteWritesFailing = false;
        _aplosTransactions.AddRange(_postedTransactions);
        await RunPexBillSync();

        Assert.Single(_postedTransactions);
        Assert.StartsWith($"Synced bill payment #{PexBillId} to Aplos on", Assert.Single(_relationshipNotes));
        Assert.All(_historyRows, row => Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus));
    }

    [Fact]
    public async Task NothingIsPostedWhileSyncBillPaymentsIsOff()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount);

        await RunSync(useBillPay: true, syncBillPayments: false);

        Assert.Empty(_postedTransactions);
        Assert.Empty(_historyRows);
    }
}
