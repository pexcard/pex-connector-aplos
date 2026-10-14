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
    // A reversal posts nothing to Aplos: it has no un-pay, an offsetting entry leaves the payable paid, and only
    // deleting the payment clears it. So the PEX bill tells the customer to delete it.
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

    // A card bill also has the clearing-to-funds transfer, which the customer finds by its note.
    internal static string GetBillPaymentReversalActionNote(AplosBillMappingModel billMapping)
        => billMapping.PaidFromClearingAccountNumber is null
            ? "Reversed in PEX. This bill still shows paid in Aplos: delete its payment there."
            : $"Reversed in PEX. This bill still shows paid in Aplos: delete its payment there, and review the transaction \"{GetCardChargeTransferNotePrefix(billMapping.AplosPayableId).TrimEnd(',')}\".";

    private async Task RecordAplosBillPaymentReversal(
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

        await _billMappingStorage.MarkReversedAsync(billMapping, utcNow, cancellationToken);
    }
}
