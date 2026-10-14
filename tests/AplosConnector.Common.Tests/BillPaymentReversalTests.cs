using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Storage;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
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

// The connector posts nothing to Aplos on a reversal: it records it on the mapping row and tells the customer,
// on the PEX bill, to delete the payment in Aplos.
public class BillPaymentReversalTests : BillPaymentReversalTestBase
{
    // Every Aplos call goes through IAplosApiClient, so with no delete on it no code path can issue one.
    [Fact]
    public void TheAplosApiClientExposesNoWayToDeleteAnything()
    {
        var deleters = typeof(IAplosApiClient)
            .GetMethods()
            .Select(method => method.Name)
            .Where(name => name.StartsWith("Delete", StringComparison.OrdinalIgnoreCase)
                           || name.StartsWith("Remove", StringComparison.OrdinalIgnoreCase)
                           || name.StartsWith("Void", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(deleters);
    }

    [Fact]
    public async Task ReversingAPaidAplosBillTouchesTheAplosLedgerNotAtAll()
    {
        SeedPaidMapping();
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);
        _payablePaidAmount = BillAmount;

        await RunBillPaymentsSync();

        Assert.Empty(_createdAplosTransactions);
        Assert.Empty(_payments);
        _mockAplosApiClient.Verify(client => client.UpdatePayable(It.IsAny<string>(), It.IsAny<JObject>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheReversalTellsTheUserToDeleteThePaymentInAplos()
    {
        SeedPaidMapping();
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);

        await RunBillPaymentsSync();

        Assert.Equal("Reversed in PEX. This bill still shows paid in Aplos: delete its payment there.", Assert.Single(_relationshipNotes));
    }

    [Fact]
    public async Task OnACardBillTheReversalAlsoPointsAtTheCardChargeTransfer()
    {
        SeedPaidMapping();
        Row().PaidFromClearingAccountNumber = CashAccountNumber;
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);

        await RunBillPaymentsSync();

        Assert.Equal(
            $"Reversed in PEX. This bill still shows paid in Aplos: delete its payment there, and review the transaction \"PEX card charge for Aplos bill {AplosPayableId}\".",
            Assert.Single(_relationshipNotes));
    }

    [Fact]
    public async Task PayThenReverseThenDeleteInAplosThenPayAgainGoesBackThroughTheOrdinaryPayPath()
    {
        SeedUnpaidMapping();

        SetupPexBill(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.Paid);
        await RunBillPaymentsSync();

        Assert.Single(_payments);
        Assert.NotNull(Row().PaidSyncedUtc);
        _payablePaidAmount = BillAmount;

        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);
        await RunBillPaymentsSync();

        Assert.Empty(_createdAplosTransactions);
        Assert.Single(_payments);
        Assert.NotNull(Row().ReversedUtc);

        // Until the customer deletes the payment, Aplos still shows it paid, so a second payment is skipped.
        SetupPexBill(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.Paid);
        await RunBillPaymentsSync();
        Assert.Single(_payments);

        _payablePaidAmount = 0m;

        SetupPexBill(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.Paid);
        await RunBillPaymentsSync();

        Assert.Equal(2, _payments.Count);
        Assert.Empty(_createdAplosTransactions);
        Assert.NotNull(Row().PaidSyncedUtc);
        Assert.Null(Row().ReversedUtc);
    }

    [Fact]
    public async Task ARePayAfterTheReversedPaymentWasDeletedStillHealsALostPaidMarker()
    {
        SeedPaidMapping();
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);
        await RunBillPaymentsSync();

        _payablePaidAmount = 0m;
        _billMappingStorage.FailNext = nameof(FakeBillMappingStorage.MarkPaidAsync);
        SetupPexBill(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.Paid);
        await RunBillPaymentsSync();

        Assert.Single(_payments);
        Assert.Null(Row().PaidSyncedUtc);

        _payablePaidAmount = BillAmount;
        await RunBillPaymentsSync();

        Assert.Single(_payments);
        Assert.Equal(UtcNow, Row().PaidSyncedUtc);
    }

    // The live payable reads unpaid on purpose: the surviving row is then the only guard against a second PEX bill.
    [Fact]
    public async Task AfterAReversalThePayableIsNotReImportedAsANewBill()
    {
        SeedPaidMapping();
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);
        await RunBillPaymentsSync();

        Assert.Null(Row().PaidSyncedUtc);
        Assert.NotNull(Row().ReversedUtc);

        _payablePaidAmount = 0m;
        await RunOutstandingBillsSync();

        Assert.Empty(_createdBillInbox);
        Assert.Single(_billMappingStorage.Rows);
    }

    [Fact]
    public async Task AReversalIsNotRecordedTwiceOnTheNextRun()
    {
        SeedPaidMapping();
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);

        await RunBillPaymentsSync();
        await RunBillPaymentsSync();

        Assert.Single(_relationshipNotes);
        Assert.Empty(_createdAplosTransactions);
    }

    [Fact]
    public async Task AReversedPexOriginatedBillPaymentIsNeverBookedAsAnExpense()
    {
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);

        await RunBillPaymentsSync();

        Assert.Empty(_createdAplosTransactions);
        Assert.Empty(_payments);
    }

    [Fact]
    public async Task WhenTheReversalNoteCannotBeWrittenTheRowIsNotMarkedAndTheNextRunWritesIt()
    {
        SeedPaidMapping();
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);

        _mockPexApiClient
            .Setup(client => client.AddTransactionRelationshipNote(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("PEX is down"));

        await RunBillPaymentsSync();

        Assert.NotNull(Row().PaidSyncedUtc);
        Assert.Null(Row().ReversedUtc);

        _mockPexApiClient
            .Setup(client => client.AddTransactionRelationshipNote(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, long, string, CancellationToken>((_, _, noteText, _) => _relationshipNotes.Add(noteText))
            .Returns(Task.CompletedTask);

        await RunBillPaymentsSync();

        Assert.Contains("delete its payment there", Assert.Single(_relationshipNotes));
        Assert.Null(Row().PaidSyncedUtc);
        Assert.Equal(UtcNow, Row().ReversedUtc);
    }

    // The other order: a repeated note is the price of never losing one.
    [Fact]
    public async Task WhenTheRowCannotBeMarkedAfterTheNoteTheNextRunRecordsTheReversal()
    {
        SeedPaidMapping();
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);
        _billMappingStorage.FailNext = nameof(FakeBillMappingStorage.MarkReversedAsync);

        await RunBillPaymentsSync();

        Assert.Single(_relationshipNotes);
        Assert.NotNull(Row().PaidSyncedUtc);
        Assert.Null(Row().ReversedUtc);

        await RunBillPaymentsSync();

        Assert.Equal(2, _relationshipNotes.Count);
        Assert.Equal(UtcNow, Row().ReversedUtc);
    }
}
