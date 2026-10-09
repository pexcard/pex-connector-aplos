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

    private const string AchClearingAccountSetting = "ACH clearing account";

    private const string CardClearingAccountSetting = "vendor card clearing account";

    private const string MissingClearingAccountsNote = "Set the Aplos ACH clearing account and vendor card clearing account for bill payments in the connector settings; until then imported bills cannot be marked paid in Aplos.";

    internal async Task SyncBillPayments(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        // Runs whatever the import toggle says now: an imported bill's payment is always written back. Sync refreshed the
        // business settings before any stage.
        if (!mapping.UseBillPayEnabled) return;

        var billMappings = await _billMappingStorage.GetByBusinessAsync(mapping.PEXBusinessAcctId, cancellationToken);
        if (billMappings.Count == 0) return;

        if (!BillPayReady(mapping))
        {
            var waiting = billMappings.Where(row => row.PaidSyncedUtc is null).ToList();
            if (waiting.Count == 0) return;

            logger.LogWarning($"Business {mapping.PEXBusinessAcctId} has {waiting.Count} imported Aplos bills waiting to be marked paid, but its bill payment clearing accounts are not configured.");
            await WriteBillPaymentsHistory(mapping, 0, 1, [$"{MissingClearingAccountsNote} Waiting: {DescribeBills(waiting)}."], cancellationToken);
            return;
        }

        var startDateUtc = GetStartDateUtc(mapping, utcNow, _syncSettings);
        var endDateUtc = GetEndDateUtc(mapping.EndDateUtc, utcNow);
        var (startDate, endDate) = GetEstDayWindow(startDateUtc, endDateUtc);

        if (startDate.Date >= endDate.Date)
        {
            logger.LogInformation($"Skipping sync bill payments for business {mapping.PEXBusinessAcctId}. Empty sync window {startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd}.");
            return;
        }

        var syncCount = 0;
        var failureCount = 0;
        List<string> failureNotes = [];

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

            List<(BillPaymentRequestModel PaymentRequest, AplosBillMappingModel BillMapping)> aplosOriginated = [];

            foreach (var paymentRequest in requests)
            {
                AplosBillMappingModel billMapping = null;
                if (paymentRequest.MetadataRelationId.HasValue)
                {
                    billMappingsByMetadataId.TryGetValue(paymentRequest.MetadataRelationId.Value, out billMapping);
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

            logger.LogInformation($"Business {mapping.PEXBusinessAcctId} has {aplosOriginated.Count} paid Aplos-originated bills in {fetchFrom:yyyy-MM-dd}..{endDate:yyyy-MM-dd}.");

            Dictionary<int, PaymentStatusTrigger> achPaymentTriggers = null;
            List<(BillPaymentRequestModel PaymentRequest, AplosBillMappingModel BillMapping)> readyToPay = [];

            foreach (var (paymentRequest, billMapping) in aplosOriginated)
            {
                if (billMapping.PaidSyncedUtc is not null)
                {
                    logger.LogInformation($"Skipping Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}. It was already marked paid on {billMapping.PaidSyncedUtc:O}.");
                    continue;
                }

                if (paymentRequest.PayoutDate is null)
                {
                    var awaitingNote = await CheckAwaitingCardCharge(logger, mapping, billMapping, paymentRequest, utcNow, cancellationToken);
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

            // Checked once per account per run, before any bill is paid. Aplos posts against whatever the number means
            // now, so an account deleted, merged or disabled since it was chosen fails its bills by name, with no
            // fallback. A settings problem, so it does not start a bill's retry window.
            List<(BillPaymentRequestModel PaymentRequest, AplosBillMappingModel BillMapping, decimal ClearingAccountNumber)> toPay = [];
            foreach (var rail in readyToPay.GroupBy(bill => GetBillPaymentClearingAccount(mapping, bill.PaymentRequest)))
            {
                var (clearingAccountNumber, settingName) = rail.Key;
                string railError;
                try
                {
                    railError = settingName is null
                        ? "not marked paid, because the PEX payment method has no Aplos clearing account. Mark paid in Aplos by hand."
                        : await CheckClearingAccount(mapping, clearingAccountNumber, settingName, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, $"Failed to read the {settingName} {clearingAccountNumber} from Aplos for business {mapping.PEXBusinessAcctId}.");
                    railError = $"the {settingName} {clearingAccountNumber} could not be read from Aplos; retried on the next sync.";
                }

                if (railError is null)
                {
                    toPay.AddRange(rail.Select(bill => (bill.PaymentRequest, bill.BillMapping, clearingAccountNumber)));
                    continue;
                }

                var bills = rail.Select(bill => bill.BillMapping).ToList();
                failureCount += bills.Count;
                failureNotes.Add($"{(bills.Count == 1 ? "Bill" : "Bills")} {DescribeBills(bills)}: {railError}");
                logger.LogWarning($"Business {mapping.PEXBusinessAcctId} has {bills.Count} Aplos bills not marked paid: {railError}");
            }

            foreach (var (paymentRequest, billMapping, clearingAccountNumber) in toPay)
            {
                try
                {
                    if (await MarkAplosPayablePaid(logger, mapping, billMapping, paymentRequest, clearingAccountNumber, utcNow, cancellationToken))
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
                    failureNotes.Add($"Bill {billMapping.AplosReferenceNumber ?? billMapping.AplosPayableId}: {DescribeFailure(ex)} Retried until {retryUntil:yyyy-MM-dd}, then it must be marked paid in Aplos by hand.");
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            failureCount++;
            failureNotes.Add(DescribeFailure(ex));
            logger.LogError(ex, $"Failed to sync bill payments for business {mapping.PEXBusinessAcctId}.");
        }

        await WriteBillPaymentsHistory(mapping, syncCount, failureCount, failureNotes, cancellationToken);
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
            // Also heals a paid marker lost after an earlier pay call.
            await CloseHealedBill(logger, mapping, billMapping, paymentRequest, cardTransactionId, utcNow, cancellationToken);

            return false;
        }

        if (action == AplosPayableAction.SkipPartiallyPaid)
        {
            throw new InvalidOperationException($"Aplos shows this bill partly paid ({AplosPayableFilter.NormalizeAmount(payable.PaidAmount):0.00} of {AplosPayableFilter.NormalizeAmount(payable.Amount):0.00}), so the PEX payment of {paymentRequest.Amount:0.00} was not recorded. Record it on the bill in Aplos.");
        }

        // Aplos's pay call ignores any amount and settles the whole balance, so a partial PEX amount would understate A/P.
        var outstanding = AplosPayableFilter.NormalizeAmount(payable.Amount) - AplosPayableFilter.NormalizeAmount(payable.PaidAmount);
        if (paymentRequest.Amount != outstanding)
        {
            throw new InvalidOperationException($"PEX paid {paymentRequest.Amount:0.00} but the Aplos balance is {outstanding:0.00}. Aplos can only settle a bill in full, so record this payment on the bill in Aplos.");
        }

        if (IsCardPayment(paymentRequest))
        {
            if (cardTransactionId is null)
            {
                throw UnlinkedCardPayment(logger, mapping, paymentRequest, utcNow);
            }

            var cardCharge = await _pexApiClient.GetCardholderTransaction(mapping.PEXExternalAPIToken, cardTransactionId.Value, cancellationToken);

            if (HasNote(cardCharge, GetSyncedNote(cardTransactionId.Value)))
            {
                throw new InvalidOperationException($"its card charge is already booked in Aplos as a card purchase (PEX transaction {cardTransactionId}), so the payable was not marked paid. Mark it paid in Aplos and delete that expense.");
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

        var payerName = GetBillPaymentPayerName(paymentRequest);
        var paidDate = GetBillPaymentPaidDate(paymentRequest);
        var aplosApiClient = MakeAplosApiClient(mapping);

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
                throw new InvalidOperationException($"Aplos refused to mark payable {billMapping.AplosPayableId} paid ({DescribeFailure(ex)}). Mark it paid in Aplos.", ex);
            }

            logger.LogInformation(ex, $"Skipping Aplos payable {billMapping.AplosPayableId} for PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}. Aplos reports it is no longer payable.");
            await CloseHealedBill(logger, mapping, billMapping, paymentRequest, cardTransactionId, utcNow, cancellationToken);

            return false;
        }

        // paid == amount is the only confirmation Aplos gives.
        var paidPayable = response?.Data?.Payable;
        if (paidPayable is null || AplosPayableFilter.NormalizeAmount(paidPayable.PaidAmount) != AplosPayableFilter.NormalizeAmount(paidPayable.Amount))
        {
            throw new InvalidOperationException($"Aplos payable {billMapping.AplosPayableId} did not come back fully paid after the pay call.");
        }

        logger.LogInformation($"Marked Aplos payable {billMapping.AplosPayableId} paid for PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}.");

        // Safe to lose: the next run's live re-read sees the payable paid and records it then.
        await TryMarkBillMappingPaid(logger, mapping, billMapping, utcNow, cancellationToken);

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

        return true;
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
        try
        {
            var payable = (await GetAplosPayable(mapping, billMapping.AplosPayableId, cancellationToken))?.Data?.Payable;
            if (payable is null || AplosPayableFilter.DetermineAction(payable) != AplosPayableAction.SkipAlreadyPaid)
            {
                logger.LogWarning($"Skipping Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}. It has failed since {billMapping.FirstFailedUtc:O} and is no longer retried.");
                return;
            }

            var cardTransactionId = IsCardPayment(paymentRequest) ? GetSettlementTransactionId(paymentRequest) : null;
            await CloseHealedBill(logger, mapping, billMapping, paymentRequest, cardTransactionId, utcNow, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to check Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}, which is no longer retried.");
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
        return $"Bill {billMapping.AplosReferenceNumber ?? billMapping.AplosPayableId}: PEX sent the vendor a card for this bill at least {waited.Days} days ago and has no charge on it yet. If the vendor has charged it, mark the bill paid in Aplos and delete the expense if the sync booked one.";
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

    // Aplos's pay call has no payment-method field, so the account it credits is what records the rail. A null setting
    // name means the rail has no clearing account.
    private static (decimal AccountNumber, string SettingName) GetBillPaymentClearingAccount(Pex2AplosMappingModel mapping, BillPaymentRequestModel paymentRequest)
    {
        if (paymentRequest.PayeeFundsDestinationType == PayeeFundsDestinationType.BankAccount)
        {
            return (mapping.BillPaymentsAchClearingAccountNumber, AchClearingAccountSetting);
        }

        return IsCardPayment(paymentRequest) ? (mapping.BillPaymentsCardClearingAccountNumber, CardClearingAccountSetting) : (0m, null);
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

        if (!string.Equals(account.Category, AplosApiClient.APLOS_ACCOUNT_CATEGORY_ASSET, StringComparison.OrdinalIgnoreCase))
        {
            return $"the {settingName} {accountNumber} ({account.Name}) is no longer an asset account in Aplos. Choose an asset account as the {settingName} in the connector settings.";
        }

        return null;
    }

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

    private Task AddCardChargeMarker(Pex2AplosMappingModel mapping, BillPaymentRequestModel paymentRequest, long cardTransactionId, CancellationToken cancellationToken)
        => _pexApiClient.AddTransactionNote(
            mapping.PEXExternalAPIToken,
            new TransactionModel { TransactionId = cardTransactionId, IsPending = false },
            GetSyncedAsBillPaymentNote(paymentRequest),
            cancelToken: cancellationToken);

    // Closing the row ends every later look at the bill, so a card charge must carry its marker first.
    private async Task CloseHealedBill(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        BillPaymentRequestModel paymentRequest,
        long? cardTransactionId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
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
                var cardCharge = await _pexApiClient.GetCardholderTransaction(mapping.PEXExternalAPIToken, cardTransactionId.Value, cancellationToken);
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

        await TryMarkBillMappingPaid(logger, mapping, billMapping, utcNow, cancellationToken);
    }

    private static InvalidOperationException UnlinkedCardPayment(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        BillPaymentRequestModel paymentRequest,
        DateTime utcNow)
    {
        // Some links never arrive (split charges, reversals, a lost settlement message).
        if (utcNow - paymentRequest.PayoutDate.Value.UtcDateTime > UnlinkedCardPaymentGracePeriod)
        {
            logger.LogWarning($"PEX payment request {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId} has had no SettlementTransactionId since its payout on {paymentRequest.PayoutDate:O}.");
            return new InvalidOperationException($"PEX has not linked its card charge to this payment {UnlinkedCardPaymentGracePeriod.Days} days after payout, so it cannot be marked paid automatically. Mark it paid in Aplos, and if the card charge was synced to Aplos as an expense, delete that expense.");
        }

        return new InvalidOperationException("PEX has not linked the card charge to this payment yet, so it was not marked paid. It is retried on the next sync.");
    }

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
    // need an account, because PEX picks the rail only when the bill is paid.
    internal static bool BillPayReady(Pex2AplosMappingModel mapping)
        => mapping.UseBillPayEnabled && mapping.BillPaymentsAchClearingAccountNumber > 0m && mapping.BillPaymentsCardClearingAccountNumber > 0m;

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
            PaymentRequestStatuses = [PaymentRequestStatus.Closed]
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

            // PayoutDate is the one paid signal both rails set; the status trigger differs between them.
            if (paymentRequest.PayoutDate is null && !IsAwaitingCardCharge(paymentRequest)) continue;

            requests.Add(paymentRequest);
        }

        return (requests, unreadable);
    }

    private async Task WriteBillPaymentsHistory(
        Pex2AplosMappingModel mapping,
        int syncCount,
        int failureCount,
        List<string> failureNotes,
        CancellationToken cancellationToken)
    {
        SyncStatus syncStatus;
        if (failureCount == 0)
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
            SyncNotes = string.Join(" ", failureNotes)
        };
        await _historyStorage.CreateAsync(result, cancellationToken);
    }
}
