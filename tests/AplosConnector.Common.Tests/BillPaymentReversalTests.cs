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
// on the PEX bill, to delete the payment in Aplos (work item 148051).
public class BillPaymentReversalTests : BillPaymentReversalTestBase
{
    // The structural half of "never hard-delete an Aplos transaction": every Aplos call the connector can
    // make goes through IAplosApiClient, and the interface has no member that could delete anything. A bill
    // pay code path cannot issue a delete because no delete exists to issue.
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

        // MVP decision 2026-09-18: the customer deletes the payment in Aplos. Posting an offsetting entry as
        // well would leave that mirror orphaned the moment they do.
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

        // The only trace is on the PEX side, and it has to be actionable: deleting the Aplos payment is the
        // one thing that clears paid.
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

    // The whole point of the story, end to end.
    [Fact]
    public async Task PayThenReverseThenDeleteInAplosThenPayAgainGoesBackThroughTheOrdinaryPayPath()
    {
        SeedUnpaidMapping();

        SetupPexBill(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.Paid);
        await RunBillPaymentsSync();

        Assert.Single(_payments);
        Assert.NotNull(Row().PaidSyncedUtc);
        _payablePaidAmount = BillAmount;

        // Reverse. Nothing reaches Aplos.
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);
        await RunBillPaymentsSync();

        Assert.Empty(_createdAplosTransactions);
        Assert.Single(_payments);
        Assert.NotNull(Row().ReversedUtc);

        // Until the customer deletes the Aplos payment the live re-read still reports it paid, so a second
        // payment is skipped rather than double-posted.
        SetupPexBill(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.Paid);
        await RunBillPaymentsSync();
        Assert.Single(_payments);

        // They delete it, which clears paid and puts payables/{id}/pay back in service.
        _payablePaidAmount = 0m;

        SetupPexBill(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.Paid);
        await RunBillPaymentsSync();

        // Ordinary payment path, no hand-built ledger entry anywhere in the cycle.
        Assert.Equal(2, _payments.Count);
        Assert.Empty(_createdAplosTransactions);
        Assert.NotNull(Row().PaidSyncedUtc);
    }

    // The mapping-row invariant: still not importable. The row surviving the reversal is the
    // only thing standing between a reversed bill and a duplicate PEX bill inbox item, so the live payable
    // here deliberately reads unpaid - the weaker of the two guards is the one under test.
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

        // The second run finds PaidSyncedUtc already cleared, so it has nothing to reverse and writes no
        // second note onto the PEX bill.
        Assert.Single(_relationshipNotes);
        Assert.Empty(_createdAplosTransactions);
    }

    // A bill PEX raised itself has no payable to settle, but it must still never book an expense once its
    // payment has failed.
    [Fact]
    public async Task AReversedPexOriginatedBillPaymentIsNeverBookedAsAnExpense()
    {
        SetupPexBill(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed);

        await RunBillPaymentsSync();

        Assert.Empty(_createdAplosTransactions);
        Assert.Empty(_payments);
    }

    // The note is the customer's only instruction, so it is written before the gate closes: a note repeated
    // on a retry is harmless, a lost one is not.
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
}
