using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Models;
using AplosConnector.Common.Services;
using Microsoft.Extensions.Logging;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Extensions;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Services;

public partial class AplosIntegrationService
{
    // MVP reversal (work item 148051, decision 2026-09-18): the connector posts NOTHING to the Aplos ledger.
    //
    // Aplos has no un-pay. An offsetting transaction leaves the payable reading paid forever - verified live
    // 2026-09-17 - and deleting the payment is the only thing that clears it. So the customer is asked to
    // delete the payment in Aplos, which clears paid and makes payables/{id}/pay available again; posting an
    // offsetting entry here as well would leave that mirror orphaned once they do.
    //
    // A later epic may automate this. Until then the connector records the reversal on its own side and says
    // what to do on the PEX bill.
    internal static bool IsBillPaymentReversed(BillPaymentRequestModel paymentRequest)
    {
        if (paymentRequest is null) return false;

        if (paymentRequest.PaymentRequestStatus == PaymentRequestStatus.PaymentException)
        {
            return paymentRequest.PaymentRequestStatusTrigger is PaymentRequestStatusTrigger.PaymentFailed
                or PaymentRequestStatusTrigger.PaymentProcessingException
                or PaymentRequestStatusTrigger.PaymentCancelled;
        }

        return paymentRequest.PaymentRequestStatusTrigger is PaymentRequestStatusTrigger.Cancelled
            or PaymentRequestStatusTrigger.Rejected;
    }

    // Sits on the PEX bill, so it names nothing the customer can't see from there. A card bill also has the
    // clearing-to-funds transfer, which the customer finds by its note.
    internal static string GetBillPaymentReversalActionNote(AplosBillMappingModel billMapping)
        => billMapping.PaidFromClearingAccountNumber is null
            ? "Reversed in PEX. This bill still shows paid in Aplos: delete its payment there."
            : $"Reversed in PEX. This bill still shows paid in Aplos: delete its payment there, and review the transaction \"{GetCardChargeTransferNotePrefix(billMapping.AplosPayableId).TrimEnd(',')}\".";

    // Returns true when the reversal was recorded.
    private async Task<bool> RecordAplosBillPaymentReversal(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        BillPaymentRequestModel paymentRequest,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        logger.LogInformation($"PEX payment request {paymentRequest.PaymentRequestId} for Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId} was reversed in PEX. The Aplos ledger is left untouched; the customer deletes the payment in Aplos.");

        // Note first: marking the row closes the gate, so a note that failed after it would be lost for good.
        if (paymentRequest.MetadataRelationId.HasValue)
        {
            await _pexApiClient.AddTransactionRelationshipNote(mapping.PEXExternalAPIToken, paymentRequest.MetadataRelationId.Value, GetBillPaymentReversalActionNote(billMapping), cancellationToken);
        }

        // The row stays. Clearing the paid marker is what makes the payable payable again once the customer
        // has deleted the Aplos payment; keeping the row is what stops SyncOutstandingBills importing it twice.
        await _billMappingStorage.MarkReversedAsync(billMapping, utcNow, cancellationToken);

        return true;
    }
}
