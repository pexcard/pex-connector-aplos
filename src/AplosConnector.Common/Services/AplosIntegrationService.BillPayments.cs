using Aplos.Api.Client;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using AplosConnector.Common.Const;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using Microsoft.Extensions.Logging;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Extensions;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Services;

public partial class AplosIntegrationService
{
    private const int BillPaymentsPageSize = 50;

    private static readonly TimeSpan UnlinkedCardPaymentGracePeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan AwaitingCardChargeLimit = TimeSpan.FromDays(30);
    // Aplos answers a deleted payable like an outage, so a failing bill can't be told apart from a stuck one.
    private static readonly TimeSpan BillPaymentRetryWindow = TimeSpan.FromDays(30);

    internal const string SyncedAsBillPaymentNote = "Synced to Aplos as a bill payment";

    private const string AchBankAccountSetting = "ACH bank account";

    private const string CardClearingAccountSetting = "vendor card clearing account";

    private const string MissingClearingAccountsNote = "Set the Aplos ACH bank account for bill payments and the Aplos account that represents your PEX funds in the connector settings; until then imported bills cannot be marked paid in Aplos.";

    internal async Task SyncBillPayments(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        // Sync refreshed the business settings before any stage, so a PEX Bill Pay turned off has already cleared the toggle.
        if (!mapping.UseBillPayEnabled) return;

        // With the import off nothing is written to Aplos, but a bill imported earlier and paid since still has its card charge
        // claimed, or the transaction sync books it as a purchase. Turning the import on again marks the payable paid.
        var claimOnly = !mapping.SyncOutstandingBills;

        var billMappings = await _billMappingStorage.GetByBusinessAsync(mapping.PEXBusinessAcctId, cancellationToken);
        if (billMappings.Count == 0) return;

        var ready = BillPayReady(mapping);
        var waiting = billMappings.Where(row => row.PaidSyncedUtc is null).ToList();
        var notReadyNote = $"{MissingClearingAccountsNote} Waiting: {DescribeBills(waiting)}.";

        var syncCount = 0;
        var failureCount = 0;
        List<string> failureNotes = [];
        var stageFailed = false;

        // Claiming needs neither account, so an import-off run never asks for them.
        void AddNotReadyNote()
        {
            if (claimOnly || ready || waiting.Count == 0 || failureNotes.Contains(notReadyNote)) return;

            failureCount += waiting.Count;
            failureNotes.Add(notReadyNote);
        }

        var startDateUtc = GetStartDateUtc(mapping, utcNow, _syncSettings);
        var endDateUtc = GetEndDateUtc(mapping.EndDateUtc, utcNow);
        var (startDate, endDate) = GetEstDayWindow(startDateUtc, endDateUtc);

        if (startDate.Date >= endDate.Date)
        {
            logger.LogInformation($"Skipping sync bill payments for business {mapping.PEXBusinessAcctId}. Empty sync window {startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd}.");
            AddNotReadyNote();
            if (failureCount > 0) await WriteBillPaymentsHistory(mapping, 0, failureCount, failureNotes, stageFailed: false, cancellationToken);
            return;
        }

        try
        {
            // BillInbox.MetadataId comes back as BillPaymentRequestModel.MetadataRelationId. Paid rows stay in: dropping
            // them would make their bill look PEX-originated on a later run.
            Dictionary<long, AplosBillMappingModel> billMappingsByMetadataId = [];

            foreach (var billMapping in billMappings)
            {
                if (!billMapping.MetadataRelationId.HasValue) continue;

                billMappingsByMetadataId[billMapping.MetadataRelationId.Value] = billMapping;
            }

            // PEX filters bill payments only by request creation, which always follows the import, so reach back to the
            // oldest unpaid import or a long-open bill falls out of the window.
            var fetchFrom = GetBillPaymentsFetchStart(logger, mapping, startDate, billMappingsByMetadataId.Values, utcNow);

            var (requests, unreadable) = await GetBillPaymentRequests(logger, mapping, fetchFrom, endDate, cancellationToken);
            failureCount += unreadable.Count;
            failureNotes.AddRange(unreadable);

            List<(BillPaymentRequestModel PaymentRequest, AplosBillMappingModel BillMapping)> reversed = [];
            List<(BillPaymentRequestModel PaymentRequest, AplosBillMappingModel BillMapping)> aplosOriginated = [];

            foreach (var paymentRequest in requests)
            {
                AplosBillMappingModel billMapping = null;
                if (paymentRequest.MetadataRelationId.HasValue)
                {
                    billMappingsByMetadataId.TryGetValue(paymentRequest.MetadataRelationId.Value, out billMapping);
                }

                if (IsBillPaymentReversed(paymentRequest))
                {
                    // Dormant while PEX's Closed has no exits. A reversed payment never books an expense.
                    if (billMapping?.PaidSyncedUtc is not null)
                    {
                        reversed.Add((paymentRequest, billMapping));
                    }

                    continue;
                }

                // Aplos cannot void a pay made before PEX settles.
                if (paymentRequest.PaymentRequestStatus != PaymentRequestStatus.Closed) continue;

                if (billMapping is not null)
                {
                    aplosOriginated.Add((paymentRequest, billMapping));
                }
                else if (paymentRequest.PayoutDate is not null && CameFromAplosImport(paymentRequest))
                {
                    failureCount++;
                    failureNotes.Add($"Bill {paymentRequest.BillRefNo ?? paymentRequest.PaymentRequestId.ToString()}: this bill was imported from Aplos but the connector has no usable record of the import, so its payment was not synced. Mark it paid in Aplos.");
                    logger.LogWarning($"PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId} carries the Aplos import note but matches no bill mapping row.");
                }

                // A bill created in PEX isn't this stage's; the transaction sync owns its card charge.
            }

            logger.LogInformation($"Business {mapping.PEXBusinessAcctId} has {aplosOriginated.Count} paid Aplos-originated bills and {reversed.Count} reversed bill payments in {fetchFrom:yyyy-MM-dd}..{endDate:yyyy-MM-dd}.");

            // Before any check that can stop a bill: a charge left unclaimed is booked as a purchase by the transaction sync.
            var claimFailures = await ClaimCardCharges(logger, mapping, aplosOriginated, cancellationToken);
            failureCount += claimFailures.Count;
            failureNotes.AddRange(claimFailures);

            if (claimOnly)
            {
                if (failureCount > 0) await WriteBillPaymentsHistory(mapping, 0, failureCount, failureNotes, stageFailed: false, cancellationToken);
                return;
            }

            if (!ready)
            {
                if (waiting.Count == 0 && failureCount == 0) return;

                if (waiting.Count > 0)
                {
                    logger.LogWarning($"Business {mapping.PEXBusinessAcctId} has {waiting.Count} imported Aplos bills waiting to be marked paid, but its ACH bank account or PEX funds account is not configured.");
                }

                AddNotReadyNote();
                await WriteBillPaymentsHistory(mapping, 0, failureCount, failureNotes, stageFailed: false, cancellationToken);
                return;
            }

            foreach (var (paymentRequest, billMapping) in reversed)
            {
                try
                {
                    await RecordAplosBillPaymentReversal(logger, mapping, billMapping, paymentRequest, utcNow, cancellationToken);
                    syncCount++;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    failureCount++;
                    failureNotes.Add($"Bill {billMapping.AplosReferenceNumber ?? billMapping.AplosPayableId}: {DescribeFailure(ex)}");
                    logger.LogError(ex, $"Failed to record the reversal of Aplos payable {billMapping.AplosPayableId} for PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}.");
                }
            }

            Dictionary<int, PaymentStatusTrigger> achPaymentTriggers = null;
            List<(BillPaymentRequestModel PaymentRequest, AplosBillMappingModel BillMapping)> readyToPay = [];

            // Read once per run, and only when a card bill needs it.
            CardRail cardRail = null;
            async Task<CardRail> GetCardRailOnce() => cardRail ??= await GetCardRail(logger, mapping, cancellationToken);

            foreach (var (paymentRequest, billMapping) in aplosOriginated)
            {
                if (billMapping.PaidSyncedUtc is not null)
                {
                    logger.LogInformation($"Skipping Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}. It was already marked paid on {billMapping.PaidSyncedUtc:O}.");
                    continue;
                }

                if (paymentRequest.PayoutDate is null)
                {
                    var awaitingNote = await CheckAwaitingCardCharge(logger, mapping, billMapping, paymentRequest, GetCardRailOnce, utcNow, cancellationToken);
                    if (awaitingNote is not null)
                    {
                        failureCount++;
                        failureNotes.Add(awaitingNote);
                    }

                    continue;
                }

                if (HasStoppedRetrying(billMapping, utcNow))
                {
                    await TryCloseStoppedBill(logger, mapping, billMapping, paymentRequest, utcNow, cancellationToken);
                    continue;
                }

                // A Closed request never reflects an ACH return, but its payment record can. Aplos has no void, so
                // wait for Settled; a payment PEX does not list is paid as before.
                if (paymentRequest.PayeeFundsDestinationType == PayeeFundsDestinationType.BankAccount && paymentRequest.PaymentId.HasValue)
                {
                    achPaymentTriggers ??= await GetAchPaymentTriggers(mapping, fetchFrom, endDate, cancellationToken);

                    if (achPaymentTriggers.TryGetValue(paymentRequest.PaymentId.Value, out var trigger) && trigger != PaymentStatusTrigger.Settled)
                    {
                        logger.LogWarning($"Skipping Aplos payable {billMapping.AplosPayableId} for PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}. Its ACH payment {paymentRequest.PaymentId} is {trigger}, not Settled.");
                        continue;
                    }
                }

                readyToPay.Add((paymentRequest, billMapping));
            }

            if (readyToPay.Any(bill => IsCardPayment(bill.PaymentRequest)))
            {
                await GetCardRailOnce();
            }

            // Checked once per account per run, before any bill is paid. Aplos posts against whatever the number means
            // now, so an account deleted, merged or disabled since it was chosen fails its bills by name, with no
            // fallback. A settings problem, so it does not start a bill's retry window.
            List<(BillPaymentRequestModel PaymentRequest, AplosBillMappingModel BillMapping, decimal CashAccountNumber, bool TransferOwed)> toPay = [];
            foreach (var rail in readyToPay.GroupBy(bill => GetBillPaymentCashAccount(mapping, bill.PaymentRequest, cardRail)))
            {
                var (cashAccountNumber, checkedSettingName) = rail.Key;
                var isCardRail = rail.Any(bill => IsCardPayment(bill.PaymentRequest));

                // No setting can fix this one, so it takes the retry window, and the stopped-bill check closes it once
                // it is paid in Aplos.
                if (cashAccountNumber <= 0m)
                {
                    foreach (var (_, billMapping) in rail)
                    {
                        failureCount++;
                        if (billMapping.FirstFailedUtc is null)
                        {
                            await TryMarkBillMappingFailed(logger, mapping, billMapping, utcNow, cancellationToken);
                        }

                        var reportedUntil = ((billMapping.FirstFailedUtc ?? utcNow) + BillPaymentRetryWindow).ToEstCalendarDate();
                        failureNotes.Add($"Bill {billMapping.AplosReferenceNumber ?? billMapping.AplosPayableId}: not marked paid, because the PEX payment method has no Aplos account to pay it from. Mark it paid in Aplos by hand; it is reported until {reportedUntil:yyyy-MM-dd}.");
                    }

                    continue;
                }

                var railError = isCardRail ? cardRail.Error : null;
                if (railError is null && checkedSettingName is not null)
                {
                    try
                    {
                        railError = await CheckClearingAccount(mapping, cashAccountNumber, checkedSettingName, cancellationToken);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, $"Failed to read the {checkedSettingName} {cashAccountNumber} from Aplos for business {mapping.PEXBusinessAcctId}.");
                        railError = $"the {checkedSettingName} {cashAccountNumber} could not be read from Aplos; retried on the next sync.";
                    }
                }

                if (railError is null)
                {
                    toPay.AddRange(rail.Select(bill => (bill.PaymentRequest, bill.BillMapping, cashAccountNumber, isCardRail && cardRail.TransferOwed)));
                    continue;
                }

                // Closing a bill already paid in Aplos needs no clearing account, and marks its card charge before the
                // transaction sync can book it.
                List<AplosBillMappingModel> bills = [];
                foreach (var (paymentRequest, billMapping) in rail)
                {
                    if (!await TryClosePaidBill(logger, mapping, billMapping, paymentRequest, utcNow, cancellationToken))
                    {
                        bills.Add(billMapping);
                    }
                }

                if (bills.Count == 0) continue;

                failureCount += bills.Count;
                failureNotes.Add($"{(bills.Count == 1 ? "Bill" : "Bills")} {DescribeBills(bills)}: {railError}");
                logger.LogWarning($"Business {mapping.PEXBusinessAcctId} has {bills.Count} Aplos bills not marked paid: {railError}");
            }

            foreach (var (paymentRequest, billMapping, cashAccountNumber, transferOwed) in toPay)
            {
                try
                {
                    if (await MarkAplosPayablePaid(logger, mapping, billMapping, paymentRequest, cashAccountNumber, transferOwed, utcNow, cancellationToken))
                    {
                        syncCount++;
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    failureCount++;
                    logger.LogError(ex, $"Failed to mark Aplos payable {billMapping.AplosPayableId} paid for PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}.");

                    if (billMapping.FirstFailedUtc is null)
                    {
                        await TryMarkBillMappingFailed(logger, mapping, billMapping, utcNow, cancellationToken);
                    }

                    var retryUntil = ((billMapping.FirstFailedUtc ?? utcNow) + BillPaymentRetryWindow).ToEstCalendarDate();
                    var byHand = MarkPaidByHand(mapping, paymentRequest.Amount, transferOwed ? cashAccountNumber : null);
                    var retry = ex is CardChargeTransferException or ManualFixRequiredException ? "" : $" Retried until {retryUntil:yyyy-MM-dd}, then {byHand}.";
                    failureNotes.Add($"Bill {billMapping.AplosReferenceNumber ?? billMapping.AplosPayableId}: {DescribeFailure(ex)}{retry}");
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Not a bill, so it fails the run without adding to the bill count.
            stageFailed = true;
            failureNotes.Add(DescribeFailure(ex));
            AddNotReadyNote();
            logger.LogError(ex, $"Failed to sync bill payments for business {mapping.PEXBusinessAcctId}.");
        }

        await WriteBillPaymentsHistory(mapping, syncCount, failureCount, failureNotes, stageFailed, cancellationToken);
    }

    private static bool CameFromAplosImport(BillPaymentRequestModel paymentRequest)
        => paymentRequest.Metadata?.Notes?.Any(note => note.NoteText?.Contains(AplosBillSyncedNotePrefix, StringComparison.OrdinalIgnoreCase) == true) == true;

    // False when the live payable said there was nothing to pay.
    private async Task<bool> MarkAplosPayablePaid(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        BillPaymentRequestModel paymentRequest,
        decimal clearingAccountNumber,
        bool transferOwed,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        // Aplos has no void, so decide on the live payable, never on the snapshot from import time.
        AplosApiPayableDetail payable;
        try
        {
            payable = (await GetAplosPayable(mapping, billMapping.AplosPayableId, cancellationToken))?.Data?.Payable;
        }
        catch (AplosApiException ex) when (ex.AplosApiError?.Status == (int)HttpStatusCode.InternalServerError)
        {
            // Aplos answers a deleted payable with the same 500 as an outage, so it stays retried.
            throw new InvalidOperationException($"Aplos could not return payable {billMapping.AplosPayableId}; it may have been deleted in Aplos. It is retried on the next sync.", ex);
        }

        if (payable is null)
        {
            throw new InvalidOperationException($"Aplos payable {billMapping.AplosPayableId} was not found; it may have been deleted in Aplos.");
        }

        // The charge is also an ordinary card transaction; the marker is what lets the transaction sync skip it.
        var cardTransactionId = IsCardPayment(paymentRequest) ? GetSettlementTransactionId(paymentRequest) : null;

        var action = AplosPayableFilter.DetermineAction(payable);
        if (action == AplosPayableAction.SkipAlreadyPaid)
        {
            logger.LogInformation($"Skipping Aplos payable {billMapping.AplosPayableId} for PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}. It is already paid in Aplos.");
            // Also heals a paid marker lost after an earlier pay call, and a transfer that failed after it.
            await CloseHealedBill(logger, mapping, billMapping, paymentRequest, payable, cardTransactionId, utcNow, cancellationToken);

            return false;
        }

        await TryClearPaidFromClearing(logger, mapping, billMapping, cancellationToken);

        if (action == AplosPayableAction.SkipPartiallyPaid)
        {
            throw new InvalidOperationException($"Aplos shows this bill partly paid ({AplosPayableFilter.NormalizeAmount(payable.PaidAmount):0.00} of {AplosPayableFilter.NormalizeAmount(payable.Amount):0.00}), so the PEX payment of {paymentRequest.Amount:0.00} was not recorded. Record it on the bill in Aplos.");
        }

        // Unpaid again means the customer deleted the reversed payment. Cleared before paying, so a paid marker lost
        // after this payment still heals.
        if (billMapping.ReversedUtc is not null)
        {
            try
            {
                await _billMappingStorage.ClearReversedAsync(billMapping, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("the connector could not record that the reversed payment was deleted in Aplos, so the payable was not marked paid. It is retried on the next sync.", ex);
            }
        }

        // Aplos's pay call ignores any amount and settles the whole balance, so a partial PEX amount would understate A/P.
        var outstanding = AplosPayableFilter.NormalizeAmount(payable.Amount) - AplosPayableFilter.NormalizeAmount(payable.PaidAmount);
        if (paymentRequest.Amount != outstanding)
        {
            throw new InvalidOperationException($"PEX paid {paymentRequest.Amount:0.00} but the Aplos balance is {outstanding:0.00}. Aplos can only settle a bill in full, so record this payment on the bill in Aplos.");
        }

        if (IsCardPayment(paymentRequest))
        {
            var byHand = MarkPaidByHand(mapping, paymentRequest.Amount, transferOwed ? clearingAccountNumber : null);

            if (cardTransactionId is null)
            {
                throw UnlinkedCardPayment(logger, mapping, paymentRequest, utcNow, byHand);
            }

            var cardCharge = await GetCardCharge(mapping, cardTransactionId.Value, cancellationToken);

            if (HasNote(cardCharge, GetSyncedNote(cardTransactionId.Value)))
            {
                throw new ManualFixRequiredException($"its card charge is already booked in Aplos as a card purchase (PEX transaction {cardTransactionId}), so the payable was not marked paid. Delete that expense in Aplos and {byHand}.");
            }

            // Marked before paying: a marker lost after a successful pay would let the transaction sync book the charge too.
            if (!HasNote(cardCharge, SyncedAsBillPaymentNote))
            {
                try
                {
                    await AddCardChargeMarker(mapping, paymentRequest, cardTransactionId.Value, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new InvalidOperationException($"the bill payment note could not be written on its card charge (PEX transaction {cardTransactionId}), so the payable was not marked paid.", ex);
                }
            }
        }

        // Recorded before paying: the payable alone can't tell a payment from the clearing account apart from one made
        // by hand, and only the first owes the transfer.
        if (transferOwed && GetCardChargeFundAmounts(payable) is null)
        {
            throw new InvalidOperationException($"its lines in Aplos don't add up to the bill amount of {AplosPayableFilter.NormalizeAmount(payable.Amount):0.00} by fund, so the card charge could not be moved to your PEX funds account and the payable was not marked paid.");
        }

        if (transferOwed && billMapping.PaidFromClearingAccountNumber is null)
        {
            try
            {
                await _billMappingStorage.MarkPaidFromClearingAsync(billMapping, clearingAccountNumber, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("the connector could not record that it pays this bill from the vendor card clearing account, so it was not marked paid. It is retried on the next sync.", ex);
            }
        }

        var payerName = GetBillPaymentPayerName(paymentRequest);
        var paidDate = GetBillPaymentPaidDate(paymentRequest);
        var aplosApiClient = MakeAplosApiClient(mapping);

        // Before paying: a paid payable is locked for good.
        await TryWritePaymentTypeOnPayable(logger, aplosApiClient, mapping, billMapping, paymentRequest, cancellationToken);

        AplosApiPayableResponse response;
        try
        {
            response = await aplosApiClient.PayPayable(billMapping.AplosPayableId, new AplosApiPayablePaymentModel
            {
                PaidDate = paidDate,
                CashAccountNumber = clearingAccountNumber
            }, cancellationToken);
        }
        catch (AplosApiException ex) when (ex.AplosApiError?.Status == (int)HttpStatusCode.MethodNotAllowed)
        {
            // 405 usually means it was paid since the re-read, but it is also Aplos's generic "not available".
            var current = (await GetAplosPayable(mapping, billMapping.AplosPayableId, cancellationToken))?.Data?.Payable;
            if (current is null || AplosPayableFilter.DetermineAction(current) != AplosPayableAction.SkipAlreadyPaid)
            {
                if (current is not null) await TryClearPaidFromClearing(logger, mapping, billMapping, cancellationToken);
                throw new InvalidOperationException($"Aplos refused to mark payable {billMapping.AplosPayableId} paid ({DescribeFailure(ex)}). Mark it paid in Aplos.", ex);
            }

            logger.LogInformation(ex, $"Skipping Aplos payable {billMapping.AplosPayableId} for PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}. Aplos reports it is no longer payable.");
            await CloseHealedBill(logger, mapping, billMapping, paymentRequest, current, cardTransactionId, utcNow, cancellationToken);

            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await TryClearPaidFromClearingIfUnpaid(logger, mapping, billMapping, cancellationToken);
            throw;
        }

        // paid == amount is the only confirmation Aplos gives.
        var paidPayable = response?.Data?.Payable;
        if (paidPayable is null || AplosPayableFilter.NormalizeAmount(paidPayable.PaidAmount) != AplosPayableFilter.NormalizeAmount(paidPayable.Amount))
        {
            await TryClearPaidFromClearingIfUnpaid(logger, mapping, billMapping, cancellationToken);
            throw new InvalidOperationException($"Aplos payable {billMapping.AplosPayableId} did not come back fully paid after the pay call.");
        }

        logger.LogInformation($"Marked Aplos payable {billMapping.AplosPayableId} paid for PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}.");

        if (paymentRequest.MetadataRelationId.HasValue)
        {
            try
            {
                var noteText = $"{GetAplosBillPaidNote(billMapping.AplosPayableId)} on {utcNow:O}. Paid by {payerName}. {GetPaymentTypeNote(paymentRequest)}";
                await _pexApiClient.AddTransactionRelationshipNote(mapping.PEXExternalAPIToken, paymentRequest.MetadataRelationId.Value, noteText, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, $"Failed to write the paid note on PEX payment request {paymentRequest.PaymentRequestId} for Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}.");
            }
        }

        // The row stays open until the transfer is in, so a failed one is retried through the paid-payable path.
        if (transferOwed)
        {
            await PostCardChargeTransfer(logger, mapping, billMapping, paymentRequest, payable, cancellationToken);
        }

        // Safe to lose: the next run's live re-read sees the payable paid and records it then.
        await TryMarkBillMappingPaid(logger, mapping, billMapping, utcNow, cancellationToken);

        return true;
    }

    // After a failed pay call only a re-read can say whether it went through; an unreadable payable keeps the marker.
    private async Task TryClearPaidFromClearingIfUnpaid(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        CancellationToken cancellationToken)
    {
        if (billMapping.PaidFromClearingAccountNumber is null) return;

        try
        {
            var current = (await GetAplosPayable(mapping, billMapping.AplosPayableId, cancellationToken))?.Data?.Payable;
            if (current is null || AplosPayableFilter.DetermineAction(current) == AplosPayableAction.SkipAlreadyPaid) return;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to re-read Aplos payable {billMapping.AplosPayableId} after its pay call failed for business {mapping.PEXBusinessAcctId}.");
            return;
        }

        await TryClearPaidFromClearing(logger, mapping, billMapping, cancellationToken);
    }

    // Aplos shows the bill unpaid, so no clearing payment happened, and one the customer now makes by hand owes no transfer.
    private async Task TryClearPaidFromClearing(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        CancellationToken cancellationToken)
    {
        if (billMapping.PaidFromClearingAccountNumber is null) return;

        try
        {
            await _billMappingStorage.ClearPaidFromClearingAsync(billMapping, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to clear the vendor card clearing payment of Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}.");
        }
    }

    private async Task TryMarkBillMappingPaid(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        try
        {
            await _billMappingStorage.MarkPaidAsync(billMapping, utcNow, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to mark the bill mapping paid for payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}.");
        }
    }

    private async Task TryMarkBillMappingFailed(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        try
        {
            await _billMappingStorage.MarkFailedAsync(billMapping, utcNow, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to record the first failure on the bill mapping for payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}.");
        }
    }

    // Past the retry window the bill is the customer's, so nothing is paid or reported; once they pay it in Aplos it
    // still has to close, or its card charge never gets the marker.
    private async Task TryCloseStoppedBill(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        BillPaymentRequestModel paymentRequest,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (!await TryClosePaidBill(logger, mapping, billMapping, paymentRequest, utcNow, cancellationToken))
        {
            logger.LogWarning($"Skipping Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}. It has failed since {billMapping.FirstFailedUtc:O} and is no longer retried.");
        }
    }

    // True when Aplos already shows the bill paid and it was closed without a pay call.
    private async Task<bool> TryClosePaidBill(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        BillPaymentRequestModel paymentRequest,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        try
        {
            var payable = (await GetAplosPayable(mapping, billMapping.AplosPayableId, cancellationToken))?.Data?.Payable;
            if (payable is null) return false;
            if (AplosPayableFilter.DetermineAction(payable) != AplosPayableAction.SkipAlreadyPaid)
            {
                await TryClearPaidFromClearing(logger, mapping, billMapping, cancellationToken);
                return false;
            }

            var cardTransactionId = IsCardPayment(paymentRequest) ? GetSettlementTransactionId(paymentRequest) : null;
            await CloseHealedBill(logger, mapping, billMapping, paymentRequest, payable, cardTransactionId, utcNow, cancellationToken);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to check whether Aplos payable {billMapping.AplosPayableId} is already paid for business {mapping.PEXBusinessAcctId}.");
            return false;
        }
    }

    // PEX closes a card request when it sends the vendor the card, and PayoutDate stays null until it links the charge.
    // A link that never arrives looks the same as a vendor who has not charged the card yet.
    private static bool IsAwaitingCardCharge(BillPaymentRequestModel paymentRequest)
        => IsCardPayment(paymentRequest)
           && paymentRequest.PaymentRequestStatus == PaymentRequestStatus.Closed
           && paymentRequest.PaymentRequestStatusTrigger == PaymentRequestStatusTrigger.Paid;

    private async Task<string> CheckAwaitingCardCharge(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        BillPaymentRequestModel paymentRequest,
        Func<Task<CardRail>> getCardRail,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (billMapping.AwaitingChargeSinceUtc is null)
        {
            try
            {
                await _billMappingStorage.MarkAwaitingChargeAsync(billMapping, utcNow, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, $"Failed to record the start of the card charge wait on the bill mapping for payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}.");
            }

            return null;
        }

        var waited = utcNow - billMapping.AwaitingChargeSinceUtc.Value;
        if (waited <= AwaitingCardChargeLimit) return null;

        try
        {
            // Paid by hand in Aplos: say nothing, and a link that arrives later still closes it with its marker.
            var payable = (await GetAplosPayable(mapping, billMapping.AplosPayableId, cancellationToken))?.Data?.Payable;
            if (payable is not null && AplosPayableFilter.DetermineAction(payable) == AplosPayableAction.SkipAlreadyPaid) return null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to check Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}, whose card payment has had no charge for {waited.Days} days.");
        }

        logger.LogWarning($"PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId} has had no card charge linked since {billMapping.AwaitingChargeSinceUtc:O}.");
        // The fix follows the rail the connector pays with; an unreadable rail leaves the plain instruction.
        var cardRail = await getCardRail();
        var clearingAccountNumber = cardRail is { Error: null, TransferOwed: true } ? cardRail.AccountNumber : (decimal?)null;
        return $"Bill {billMapping.AplosReferenceNumber ?? billMapping.AplosPayableId}: PEX sent the vendor a card for this bill at least {waited.Days} days ago and has no charge on it yet (PEX payment {paymentRequest.PaymentRequestId}). If the vendor has charged it, {MarkPaidByHand(mapping, paymentRequest.Amount, clearingAccountNumber, "the bill")} and delete the expense if the sync booked one.";
    }

    private static bool HasStoppedRetrying(AplosBillMappingModel billMapping, DateTime utcNow)
        => billMapping.FirstFailedUtc is not null && utcNow - billMapping.FirstFailedUtc.Value > BillPaymentRetryWindow;

    private static bool IsCardPayment(BillPaymentRequestModel paymentRequest)
        => paymentRequest.PayeeFundsDestinationType is PayeeFundsDestinationType.SingleUseVendorVirtualCard or PayeeFundsDestinationType.VendorVirtualCard;

    private static long? GetSettlementTransactionId(BillPaymentRequestModel paymentRequest)
        => long.TryParse(paymentRequest.SettlementTransactionId, out var transactionId) && transactionId > 0 ? transactionId : null;

    // AplosApiException has no message of its own; the customer needs the one Aplos sent.
    private static string DescribeFailure(Exception ex)
        => ex is AplosApiException aplosException && !string.IsNullOrWhiteSpace(aplosException.AplosApiError?.Exception?.Message)
            ? aplosException.AplosApiError.Exception.Message
            : ex.Message;

    // Aplos's pay call has no payment-method field, so the account it credits is what records the rail. A zero
    // account means the rail has none.
    private static (decimal AccountNumber, string CheckedSettingName) GetBillPaymentCashAccount(Pex2AplosMappingModel mapping, BillPaymentRequestModel paymentRequest, CardRail cardRail)
    {
        if (paymentRequest.PayeeFundsDestinationType == PayeeFundsDestinationType.BankAccount)
        {
            return (mapping.BillPaymentsAchClearingAccountNumber, AchBankAccountSetting);
        }

        return IsCardPayment(paymentRequest) ? (cardRail.AccountNumber, cardRail.CheckedSettingName) : (0m, null);
    }

    private sealed record CardRail(decimal AccountNumber, string CheckedSettingName, bool TransferOwed, string Error);

    // Aplos pays a payable only from an asset Register account (spikes S10b, S12). A credit business's PEX funds account
    // is a liability, so its card bills are paid from the clearing account and the charge moved by a transfer (153880).
    private async Task<CardRail> GetCardRail(ILogger logger, Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
    {
        var pexFundsAccountNumber = mapping.AplosRegisterAccountNumber;

        AplosApiAccountDetail pexFundsAccount;
        try
        {
            pexFundsAccount = (await MakeAplosApiClient(mapping).GetAccount(pexFundsAccountNumber, cancellationToken))?.Data?.Account;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to read the PEX funds account {pexFundsAccountNumber} from Aplos for business {mapping.PEXBusinessAcctId}.");
            return new CardRail(pexFundsAccountNumber, null, false, $"the account that represents your PEX funds ({pexFundsAccountNumber}) could not be read from Aplos; retried on the next sync.");
        }

        if (pexFundsAccount is null)
        {
            return new CardRail(pexFundsAccountNumber, null, false, $"the account that represents your PEX funds ({pexFundsAccountNumber}) could not be found in Aplos. Choose it again in the connector settings.");
        }

        if (!pexFundsAccount.IsEnabled)
        {
            return new CardRail(pexFundsAccountNumber, null, false, $"the account that represents your PEX funds ({pexFundsAccountNumber}, {pexFundsAccount.Name}) is disabled in Aplos. Enable it in Aplos or choose another account in the connector settings.");
        }

        if (IsCashAccount(pexFundsAccount))
        {
            return new CardRail(pexFundsAccountNumber, null, false, null);
        }

        // The clearing account is a credit-only setting: a prepaid business can't see it, so one left saved from
        // before is not used.
        if (mapping.PEXFundingSource == FundingSource.Prepaid)
        {
            return new CardRail(pexFundsAccountNumber, null, false, $"Aplos can't mark bills paid from your PEX funds account {pexFundsAccountNumber} ({pexFundsAccount.Name}). Choose a bank account to represent your PEX funds in the connector settings.");
        }

        if (mapping.BillPaymentsCardClearingAccountNumber <= 0m)
        {
            return new CardRail(pexFundsAccountNumber, null, false, $"Aplos can't mark bills paid from your PEX funds account {pexFundsAccountNumber} ({pexFundsAccount.Name}). Set the Aplos vendor card clearing account for bill payments in the connector settings.");
        }

        return new CardRail(mapping.BillPaymentsCardClearingAccountNumber, CardClearingAccountSetting, true, null);
    }

    // Null when the account can take the payment, otherwise what the customer has to fix.
    private async Task<string> CheckClearingAccount(Pex2AplosMappingModel mapping, decimal accountNumber, string settingName, CancellationToken cancellationToken)
    {
        // Aplos answers a missing account with 200 and empty data, so any error here is a failed read, not a missing account.
        var account = (await MakeAplosApiClient(mapping).GetAccount(accountNumber, cancellationToken))?.Data?.Account;

        if (account is null)
        {
            return $"the {settingName} {accountNumber} could not be found in Aplos. If it was deleted, merged or renumbered, choose the {settingName} again in the connector settings.";
        }

        if (!account.IsEnabled)
        {
            return $"the {settingName} {accountNumber} ({account.Name}) is disabled in Aplos. Enable it in Aplos or choose another {settingName} in the connector settings.";
        }

        // Aplos's pay call refuses everything but an asset Register account: liabilities, and other assets such as
        // Buildings or Equipment (spikes S10b, S12).
        if (!IsCashAccount(account))
        {
            return $"the {settingName} {accountNumber} ({account.Name}) is not a bank or cash register account in Aplos. Choose a bank or cash account as the {settingName} in the connector settings.";
        }

        return null;
    }

    internal static bool IsCashAccount(AplosApiAccountDetail account)
        => string.Equals(account.Category, AplosApiClient.APLOS_ACCOUNT_CATEGORY_ASSET, StringComparison.OrdinalIgnoreCase)
            && string.Equals(account.Type, AplosApiClient.APLOS_ACCOUNT_TYPE_REGISTER, StringComparison.OrdinalIgnoreCase)
            && string.Equals(account.Activity, AplosApiClient.APLOS_ACCOUNT_ACTIVITY_CASH, StringComparison.OrdinalIgnoreCase);

    private static string DescribeBills(IReadOnlyCollection<AplosBillMappingModel> bills)
    {
        var references = string.Join(", ", bills.Take(5).Select(row => row.AplosReferenceNumber ?? row.AplosPayableId));
        return bills.Count > 5 ? $"{references} and {bills.Count - 5} more" : references;
    }

    internal static string GetPaymentTypeNote(BillPaymentRequestModel paymentRequest) => paymentRequest.PayeeFundsDestinationType switch
    {
        PayeeFundsDestinationType.BankAccount => "Paid via ACH",
        PayeeFundsDestinationType.NonPlatform => "Paid via Non Platform",
        _ => "Paid via Card"
    };

    internal static string GetSyncedAsBillPaymentNote(BillPaymentRequestModel paymentRequest)
        => $"{SyncedAsBillPaymentNote} #{paymentRequest.PaymentRequestId}. {GetPaymentTypeNote(paymentRequest)}";

    private static bool HasNote(TransactionResultModel transaction, string noteText)
        => transaction?.Notes?.Any(note => note.Content?.Contains(noteText, StringComparison.OrdinalIgnoreCase) == true) == true;

    // Card charges are purchases; the by-id transaction endpoint covers only funding and fees. The purchases index can
    // trail a settled charge, and reading nothing must not pass for "not booked as a purchase".
    private async Task<TransactionResultModel> GetCardCharge(Pex2AplosMappingModel mapping, long cardTransactionId, CancellationToken cancellationToken)
        => await _pexApiClient.GetCardholderPurchase(mapping.PEXExternalAPIToken, cardTransactionId, cancellationToken)
            ?? throw new InvalidOperationException($"its card charge (PEX transaction {cardTransactionId}) could not be read from PEX yet, so it could not be checked for an earlier purchase sync and the payable was not marked paid.");

    private Task AddCardChargeMarker(Pex2AplosMappingModel mapping, BillPaymentRequestModel paymentRequest, long cardTransactionId, CancellationToken cancellationToken)
        => _pexApiClient.AddTransactionNote(
            mapping.PEXExternalAPIToken,
            new TransactionModel { TransactionId = cardTransactionId, IsPending = false },
            GetSyncedAsBillPaymentNote(paymentRequest),
            cancelToken: cancellationToken);

    // The row is what the transaction sync trusts, so it is written first; the note marks the charge in PEX too. An
    // unreadable charge is noted anyway: an earlier purchase sync is still caught at pay time.
    // Returns a note per charge that ended up with neither the row claim nor the note.
    private async Task<List<string>> ClaimCardCharges(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        IEnumerable<(BillPaymentRequestModel PaymentRequest, AplosBillMappingModel BillMapping)> bills,
        CancellationToken cancellationToken)
    {
        List<string> unclaimed = [];

        foreach (var (paymentRequest, billMapping) in bills)
        {
            var cardTransactionId = IsCardPayment(paymentRequest) ? GetSettlementTransactionId(paymentRequest) : null;
            if (cardTransactionId is null || billMapping.CardTransactionId == cardTransactionId) continue;

            var rowClaimed = true;
            try
            {
                await _billMappingStorage.MarkCardChargeClaimedAsync(billMapping, cardTransactionId.Value, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                rowClaimed = false;
                logger.LogWarning(ex, $"Failed to record card charge {cardTransactionId} on the bill mapping for payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}.");
            }

            var noteClaimed = true;
            try
            {
                var cardCharge = await _pexApiClient.GetCardholderPurchase(mapping.PEXExternalAPIToken, cardTransactionId.Value, cancellationToken);
                if (!HasNote(cardCharge, SyncedAsBillPaymentNote))
                {
                    await AddCardChargeMarker(mapping, paymentRequest, cardTransactionId.Value, cancellationToken);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                noteClaimed = false;
                logger.LogWarning(ex, $"Failed to write the bill payment note on card charge {cardTransactionId} for payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}.");
            }

            if (!rowClaimed && !noteClaimed)
            {
                unclaimed.Add($"Bill {billMapping.AplosReferenceNumber ?? billMapping.AplosPayableId}: its card charge could not be held back, so it may be booked in Aplos as a purchase.");
            }
        }

        return unclaimed;
    }

    // Closing the row ends every later look at the bill, so a card charge must carry its marker first.
    private async Task CloseHealedBill(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        BillPaymentRequestModel paymentRequest,
        AplosApiPayableDetail payable,
        long? cardTransactionId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        // Aplos still shows the reversed payment until the customer deletes it; healing the row here would undo the reversal.
        if (billMapping.ReversedUtc is not null) return;

        if (IsCardPayment(paymentRequest))
        {
            if (cardTransactionId is null)
            {
                if (utcNow - paymentRequest.PayoutDate.Value.UtcDateTime <= UnlinkedCardPaymentGracePeriod)
                {
                    throw UnlinkedCardPayment(logger, mapping, paymentRequest, utcNow);
                }

                // Past the grace period the customer was told to mark it paid in Aplos. The row stays open, so a link
                // that arrives later still marks the charge before the transaction sync can book it.
                logger.LogInformation($"Skipping Aplos payable {billMapping.AplosPayableId} for PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}. Aplos shows it paid, and PEX has not linked its card charge.");
                return;
            }

            try
            {
                var cardCharge = await GetCardCharge(mapping, cardTransactionId.Value, cancellationToken);
                if (HasNote(cardCharge, GetSyncedNote(cardTransactionId.Value)))
                {
                    logger.LogWarning($"Closing Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}, but its card charge (PEX transaction {cardTransactionId}) was also synced to Aplos as a purchase.");
                }

                if (!HasNote(cardCharge, SyncedAsBillPaymentNote))
                {
                    await AddCardChargeMarker(mapping, paymentRequest, cardTransactionId.Value, cancellationToken);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException($"Aplos shows this bill paid, but the bill payment note could not be written on its card charge (PEX transaction {cardTransactionId}). It is retried on the next sync.", ex);
            }
        }

        if (billMapping.PaidFromClearingAccountNumber is not null)
        {
            await PostCardChargeTransfer(logger, mapping, billMapping, paymentRequest, payable, cancellationToken);
        }

        await TryMarkBillMappingPaid(logger, mapping, billMapping, utcNow, cancellationToken);
    }

    private static InvalidOperationException UnlinkedCardPayment(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        BillPaymentRequestModel paymentRequest,
        DateTime utcNow,
        string byHand = null)
    {
        // Some links never arrive (split charges, reversals, a lost settlement message).
        if (utcNow - paymentRequest.PayoutDate.Value.UtcDateTime > UnlinkedCardPaymentGracePeriod)
        {
            logger.LogWarning($"PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId} has had no SettlementTransactionId since its payout on {paymentRequest.PayoutDate:O}.");
            return new ManualFixRequiredException($"PEX has not linked its card charge to this payment (PEX payment {paymentRequest.PaymentRequestId}) {UnlinkedCardPaymentGracePeriod.Days} days after payout, so it cannot be marked paid automatically. {Capitalize(byHand ?? MarkPaidByHand(mapping, paymentRequest.Amount, null))}, and if the card charge was synced to Aplos as an expense, delete that expense.");
        }

        return new InvalidOperationException("PEX has not linked the card charge to this payment yet, so it was not marked paid. It is retried on the next sync.");
    }

    // A credit business pays an imported card bill from the vendor card clearing account and owes the transfer (153880).
    private static string MarkPaidByHand(Pex2AplosMappingModel mapping, decimal amount, decimal? clearingAccountNumber, string subject = "it")
        => clearingAccountNumber is { } clearing
            ? $"mark {subject} paid in Aplos by hand from clearing account {clearing} and post {amount:0.00} as debit {clearing} / credit {mapping.AplosRegisterAccountNumber}"
            : $"mark {subject} paid in Aplos by hand";

    private static string Capitalize(string text) => string.Concat(char.ToUpperInvariant(text[0]).ToString(), text[1..]);

    // Retrying cannot fix it, so the message carries the whole fix and no retry date.
    private sealed class ManualFixRequiredException(string message) : InvalidOperationException(message);

    private static DateTime GetBillPaymentsFetchStart(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        DateTime startDate,
        IEnumerable<AplosBillMappingModel> billMappings,
        DateTime utcNow)
    {
        var unpaidImports = billMappings.Where(row => row.PaidSyncedUtc is null && !HasStoppedRetrying(row, utcNow)).ToList();
        if (unpaidImports.Count == 0) return startDate;

        // The UTC instant of EST midnight, like GetEstDayWindow's startDate (148052).
        var oldestImport = unpaidImports.Min(row => row.CreatedUtc).ToStartOfDay(TimeZones.EST);
        if (oldestImport >= startDate) return startDate;

        var oneYearAgo = utcNow.AddMonths(-12).ToStartOfDay(TimeZones.EST);
        if (oldestImport < oneYearAgo)
        {
            logger.LogWarning($"Business {mapping.PEXBusinessAcctId} has unpaid Aplos imports from {oldestImport:yyyy-MM-dd}; bill payments are only looked up back to {oneYearAgo:yyyy-MM-dd}.");
            return oneYearAgo;
        }

        return oldestImport;
    }

    // Read by both stages: importing a bill the payment stage can't mark paid double-counts it in Aplos. Both rails
    // need an account, because PEX picks the rail only when the bill is paid. Whether card bills also need the clearing
    // account depends on the PEX funds account's type in Aplos, so that is checked at pay time (153880).
    internal static bool BillPayReady(Pex2AplosMappingModel mapping)
        => mapping.UseBillPayEnabled && mapping.BillPaymentsAchClearingAccountNumber > 0m && mapping.AplosRegisterAccountNumber > 0m;

    // Aplos reads paid_date as a local calendar day.
    internal static DateOnly GetBillPaymentPaidDate(BillPaymentRequestModel paymentRequest)
        => paymentRequest.PayoutDate.Value.UtcDateTime.ToEstCalendarDate();

    internal static string GetAplosBillPaidNote(string payableId) => $"Marked Aplos bill #{payableId} paid";

    internal static string GetBillPaymentPayerName(BillPaymentRequestModel paymentRequest)
    {
        var payerName = $"{paymentRequest?.UserFirstName} {paymentRequest?.UserLastName}".Trim();
        return string.IsNullOrEmpty(payerName) ? "PEX" : payerName;
    }

    private async Task<Dictionary<int, PaymentStatusTrigger>> GetAchPaymentTriggers(
        Pex2AplosMappingModel mapping,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var request = new PaymentListRequestModel
        {
            OutboundAchCreationStartDate = startDate,
            OutboundAchCreationEndDate = endDate,
        };

        List<PaymentModel> payments = [];
        var page = 1;
        long totalItems;

        do
        {
            var response = await _pexApiClient.GetPayments(mapping.PEXExternalAPIToken, request, page, BillPaymentsPageSize, cancellationToken);
            if (response?.Items is not { Count: > 0 }) break;

            payments.AddRange(response.Items);
            totalItems = response.PageInfo?.TotalItems ?? payments.Count;
            page++;
        } while (payments.Count < totalItems);

        return payments
            .DistinctBy(payment => payment.PaymentId)
            .ToDictionary(payment => payment.PaymentId, payment => payment.PaymentStatusTrigger);
    }

    private async Task<(List<BillPaymentRequestModel> Requests, List<string> Unreadable)> GetBillPaymentRequests(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var request = new BillPaymentListRequestModel
        {
            CreatedDateFrom = startDate,
            CreatedDateTo = endDate,
            PaymentRequestStatuses = [PaymentRequestStatus.Closed, PaymentRequestStatus.PaymentException]
        };

        var page = 1;
        var fetched = 0;
        List<BillPaymentModel> bills = [];

        while (true)
        {
            var response = await _pexApiClient.GetBillPayments(mapping.PEXExternalAPIToken, request, page, BillPaymentsPageSize, cancellationToken);
            if (response?.Items is not { Count: > 0 }) break;

            bills.AddRange(response.Items);

            fetched += response.Items.Count;
            if (response.Items.Count < BillPaymentsPageSize || response.PageInfo is null || fetched >= response.PageInfo.TotalItems) break;

            page++;
        }

        List<BillPaymentRequestModel> requests = [];
        List<string> unreadable = [];

        foreach (var bill in bills.DistinctBy(item => item.BillId))
        {
            BillPaymentRequestModel paymentRequest;
            try
            {
                paymentRequest = await _pexApiClient.GetBillPaymentRequest(mapping.PEXExternalAPIToken, bill.BillId, false, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, $"Failed to read PEX bill payment request {bill.BillId} for business {mapping.PEXBusinessAcctId}.");
                unreadable.Add($"Bill {bill.BillRefNo ?? bill.BillId.ToString()}: PEX could not return its payment, so it was not checked. It is retried on the next sync.");
                continue;
            }

            if (paymentRequest is null) continue;

            // PayoutDate is the one paid signal both rails set; the status trigger differs between them. A payment that
            // failed after payout keeps its payout date, so a reversal is recognised from the status instead.
            if (paymentRequest.PayoutDate is null && !IsBillPaymentReversed(paymentRequest) && !IsAwaitingCardCharge(paymentRequest)) continue;

            requests.Add(paymentRequest);
        }

        return (requests, unreadable);
    }

    private async Task WriteBillPaymentsHistory(
        Pex2AplosMappingModel mapping,
        int syncCount,
        int failureCount,
        List<string> failureNotes,
        bool stageFailed,
        CancellationToken cancellationToken)
    {
        SyncStatus syncStatus;
        if (failureCount == 0 && !stageFailed)
        {
            syncStatus = SyncStatus.Success;
        }
        else if (syncCount != 0)
        {
            syncStatus = SyncStatus.Partial;
        }
        else
        {
            syncStatus = SyncStatus.Failed;
        }

        var result = new SyncResultModel
        {
            PEXBusinessAcctId = mapping.PEXBusinessAcctId,
            SyncType = SyncTypes.BillPayments,
            SyncStatus = syncStatus.ToString(),
            SyncedRecords = syncCount,
            SyncNotes = JoinFailureNotes(failureCount, "1 bill payment needs attention.", $"{failureCount} bill payments need attention.", failureNotes)
        };
        await _historyStorage.CreateAsync(result, cancellationToken);
    }

    private static string JoinFailureNotes(int failureCount, string oneFailed, string manyFailed, List<string> failureNotes)
        => failureCount == 0
            ? string.Join(" ", failureNotes)
            : string.Join(" ", [failureCount == 1 ? oneFailed : manyFailed, .. failureNotes]);
}
