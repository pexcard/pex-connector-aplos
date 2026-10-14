using Aplos.Api.Client.Abstractions;
using AplosConnector.Common.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PexCard.Api.Client.Core.Models;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Services;

public partial class AplosIntegrationService
{
    private const string PayableNoteSeparator = " | ";
    private const int AplosNoteMaxLength = 2000;

    // f_note is a substring match; the closing bracket ends the id, so bill 12 never matches the payment of bill 123.
    internal static string GetPayablePaymentToken(string aplosPayableId) => $"[PEXBP:{aplosPayableId}]";

    // A retry replaces only the connector's own segment, so text the customer added on either side of it survives.
    internal static string AppendPaymentTypeToPayableNote(string note, string aplosPayableId, BillPaymentRequestModel paymentRequest)
    {
        var token = GetPayablePaymentToken(aplosPayableId);
        var paymentNote = $"{token}{PayableNoteSeparator}{GetPaymentTypeNote(paymentRequest)}";
        if (string.IsNullOrEmpty(note)) return paymentNote;

        var ownSegment = new Regex($@"{Regex.Escape(token)}(?:{Regex.Escape(PayableNoteSeparator)}Paid via (?:ACH|Card|Non Platform))?");
        var match = ownSegment.Match(note);

        return match.Success
            ? $"{note[..match.Index]}{paymentNote}{note[(match.Index + match.Length)..]}"
            : $"{note}{PayableNoteSeparator}{paymentNote}";
    }

    // Aplos's pay call drops note and memo, but copies the payable's note onto the payment, so it goes there first. Best
    // effort: a note must never hold up a payment. PUT replaces the whole payable and Aplos has no concurrency check, so a
    // customer edit between the read and the write is lost.
    private async Task TryWritePaymentTypeOnPayable(
        ILogger logger,
        IAplosApiClient aplosApiClient,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        BillPaymentRequestModel paymentRequest,
        CancellationToken cancellationToken)
    {
        try
        {
            var payable = await aplosApiClient.GetPayableForUpdate(billMapping.AplosPayableId, cancellationToken)
                ?? throw new InvalidOperationException($"Aplos returned no payable {billMapping.AplosPayableId}.");

            // Aplos leaves the key out when the note is empty.
            var note = payable.Value<string>("note");
            var paidNote = AppendPaymentTypeToPayableNote(note, billMapping.AplosPayableId, paymentRequest);
            if (paidNote == note) return;

            if (paidNote.Length > AplosNoteMaxLength)
            {
                logger.LogWarning($"Skipping the payment type note on Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}. It would pass Aplos's {AplosNoteMaxLength} character limit, and the customer's note is never cut.");
                return;
            }

            payable["note"] = paidNote;
            await aplosApiClient.UpdatePayable(billMapping.AplosPayableId, payable, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to write the payment type note on Aplos payable {billMapping.AplosPayableId} for PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}.");
        }
    }
}
