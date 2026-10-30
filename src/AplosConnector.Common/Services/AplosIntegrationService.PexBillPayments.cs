using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Const;
using AplosConnector.Common.Extensions;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using Microsoft.Extensions.Logging;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Extensions;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Services;

public partial class AplosIntegrationService
{
    private const string MissingAchBankAccountNote = "Set the Aplos ACH bank account for bill payments in the connector settings.";

    private const string MissingPexFundsAccountNote = "Set the Aplos account that represents your PEX funds in the connector settings.";

    private const string BillPaymentSyncedNotePrefix = "Synced bill payment #";

    internal static string GetBillPaymentSyncedNote(int paymentRequestId) => $"{BillPaymentSyncedNotePrefix}{paymentRequestId} to Aplos";

    private static bool IsBillPaymentSyncedNote(string noteText)
    {
        var start = noteText?.IndexOf(BillPaymentSyncedNotePrefix, StringComparison.OrdinalIgnoreCase) ?? -1;
        return start >= 0 && noteText.IndexOf(" to Aplos", start, StringComparison.OrdinalIgnoreCase) > start;
    }

    internal const string SyncedAsPurchaseNote = "Synced to Aplos as a purchase";

    // The Aplos note's last segment. Kept apart from the bare ids purchases and reimbursements end with, which share
    // no number space with bill payments.
    internal static string GetBillPaymentAplosNoteId(int paymentRequestId) => $"Bill payment #{paymentRequestId}";

    private enum PexBillPaymentOutcome
    {
        Posted,
        Skipped,
        Waiting
    }

    private sealed class PexBillPaymentContext
    {
        public required DateTime StartDate { get; init; }
        public required Func<Task<Dictionary<int, PaymentStatusTrigger>>> GetAchPaymentTriggers { get; init; }
        public required IReadOnlyDictionary<int, string> PayeeNames { get; init; }
        public required IReadOnlyDictionary<int, AplosBillMappingModel> PexClaims { get; init; }
        public bool UseTags { get; set; }
        public List<PexAplosApiObject> Funds { get; set; }
        public List<PexAplosApiObject> ExpenseAccounts { get; set; }
        public List<PexAplosApiObject> Tags { get; set; }
        public List<TagDropdownDetailsModel> DropdownTags { get; set; }
        public List<TagDetailsModel> TagDefinitions { get; set; }
        public List<AplosApiTransactionDetail> AplosTransactions { get; set; }
    }

    // Bills created and paid in PEX: one Aplos transaction each. Imported bills take the mark-paid path instead.
    private async Task<(int Synced, int Failed, List<string> Notes)> SyncPexCreatedBillPayments(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        List<BillPaymentRequestModel> paymentRequests,
        PexBillPaymentContext context,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var syncCount = 0;
        var failureCount = 0;
        List<string> notes = [];

        var startDay = DateOnly.FromDateTime(context.StartDate);
        List<BillPaymentRequestModel> candidates = [];
        var paidOutsidePex = 0;

        foreach (var paymentRequest in paymentRequests)
        {
            // Bills paid before the start date stay out, like purchases before it.
            if (GetBillPaymentPaidDate(paymentRequest) < startDay) continue;

            if (paymentRequest.PayeeFundsDestinationType == PayeeFundsDestinationType.NonPlatform)
            {
                paidOutsidePex++;
                continue;
            }

            if (HasBillNote(paymentRequest, GetBillPaymentSyncedNote(paymentRequest.PaymentRequestId)) || HasBillNote(paymentRequest, SyncedAsPurchaseNote)) continue;

            candidates.Add(paymentRequest);
        }

        if (paidOutsidePex > 0)
        {
            notes.Add($"Skipped {paidOutsidePex} {(paidOutsidePex == 1 ? "bill" : "bills")} paid outside PEX.");
        }

        if (candidates.Count == 0) return (syncCount, failureCount, notes);

        logger.LogInformation($"Business {mapping.PEXBusinessAcctId} has {candidates.Count} paid PEX bills to post to Aplos.");

        // An ACH bill credits the ACH bank account, which only the customer can set; card bills don't wait on it.
        string achAccountError = null;
        if (candidates.Any(IsAchPayment))
        {
            if (mapping.BillPaymentsAchClearingAccountNumber <= 0m)
            {
                achAccountError = MissingAchBankAccountNote;
            }
            else
            {
                try
                {
                    var accountError = await CheckClearingAccount(mapping, mapping.BillPaymentsAchClearingAccountNumber, AchBankAccountSetting, cancellationToken);
                    if (accountError is not null)
                    {
                        achAccountError = Capitalize(accountError);
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, $"Failed to read the {AchBankAccountSetting} {mapping.BillPaymentsAchClearingAccountNumber} from Aplos for business {mapping.PEXBusinessAcctId}.");
                    achAccountError = $"The {AchBankAccountSetting} {mapping.BillPaymentsAchClearingAccountNumber} could not be read from Aplos; retried on the next sync.";
                }
            }
        }

        if (achAccountError is not null)
        {
            var achBills = candidates.Where(IsAchPayment).ToList();
            candidates.RemoveAll(IsAchPayment);

            failureCount += achBills.Count;
            notes.Add($"{(achBills.Count == 1 ? "Bill" : "Bills")} {DescribePaymentRequests(achBills)} not posted: {achAccountError}");
            logger.LogWarning($"Business {mapping.PEXBusinessAcctId} has {achBills.Count} ACH bill payments not posted to Aplos: {achAccountError}");
        }

        if (candidates.Any(IsCardPayment) && mapping.AplosRegisterAccountNumber <= 0m)
        {
            var cardBills = candidates.Where(IsCardPayment).ToList();
            candidates.RemoveAll(IsCardPayment);

            failureCount += cardBills.Count;
            notes.Add($"{(cardBills.Count == 1 ? "Bill" : "Bills")} {DescribePaymentRequests(cardBills)} not posted: {MissingPexFundsAccountNote}");
        }

        if (candidates.Count == 0) return (syncCount, failureCount, notes);

        await LoadBillPaymentLookups(logger, mapping, context, cancellationToken);

        foreach (var paymentRequest in candidates)
        {
            try
            {
                var (outcome, note) = await PostPexBillPayment(logger, mapping, paymentRequest, context, utcNow, cancellationToken);
                switch (outcome)
                {
                    case PexBillPaymentOutcome.Posted:
                        syncCount++;
                        break;
                    case PexBillPaymentOutcome.Waiting when note is not null:
                        failureCount++;
                        notes.Add(note);
                        break;
                    case PexBillPaymentOutcome.Skipped when note is not null:
                        notes.Add(note);
                        break;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failureCount++;
                notes.Add($"Bill {DescribePaymentRequest(paymentRequest)}: {DescribeFailure(ex)}");
                logger.LogError(ex, $"Failed to post PEX bill payment {paymentRequest.PaymentRequestId} to Aplos for business {mapping.PEXBusinessAcctId}.");
            }
        }

        return (syncCount, failureCount, notes);
    }

    private async Task<(PexBillPaymentOutcome Outcome, string Note)> PostPexBillPayment(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        BillPaymentRequestModel paymentRequest,
        PexBillPaymentContext context,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        context.PexClaims.TryGetValue(paymentRequest.PaymentRequestId, out var claim);

        // Aplos is the record of what was posted: a bill note lost after an earlier post is written again, not posted twice.
        if (WasPexTransactionSyncedToAplos(context.AplosTransactions, GetBillPaymentAplosNoteId(paymentRequest.PaymentRequestId)))
        {
            logger.LogInformation($"Skipping PEX bill payment {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}. It is already in Aplos.");
            if (claim is { PaidSyncedUtc: null })
            {
                await TryMarkPexClaimPosted(logger, mapping, paymentRequest, utcNow, cancellationToken);
            }

            await TryAddBillNote(logger, mapping, paymentRequest, $"{GetBillPaymentSyncedNote(paymentRequest.PaymentRequestId)} on {utcNow:O}.", cancellationToken);
            return (PexBillPaymentOutcome.Skipped, null);
        }

        decimal creditAccountNumber;
        long? cardTransactionId = null;
        TransactionResultModel cardCharge = null;
        if (IsAchPayment(paymentRequest))
        {
            // A Closed request never reflects an ACH return, but its payment record can, and Aplos has no void.
            if (paymentRequest.PaymentId.HasValue)
            {
                var triggers = await context.GetAchPaymentTriggers();
                if (triggers.TryGetValue(paymentRequest.PaymentId.Value, out var trigger) && trigger != PaymentStatusTrigger.Settled)
                {
                    logger.LogInformation($"Skipping PEX bill payment {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}. Its ACH payment {paymentRequest.PaymentId} is {trigger}, not Settled.");
                    return (PexBillPaymentOutcome.Waiting, null);
                }
            }

            creditAccountNumber = mapping.BillPaymentsAchClearingAccountNumber;
        }
        else
        {
            cardTransactionId = GetSettlementTransactionId(paymentRequest);

            // Turning Sync bill payments off brings the charge back into the transaction sync, so it is claimed before it is
            // posted. Held until PEX links it.
            if (cardTransactionId is null)
            {
                logger.LogWarning($"PEX bill payment {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId} has no SettlementTransactionId since its payout on {paymentRequest.PayoutDate:O}.");
                if (utcNow - paymentRequest.PayoutDate.Value.UtcDateTime <= UnlinkedCardPaymentGracePeriod) return (PexBillPaymentOutcome.Waiting, null);

                return (PexBillPaymentOutcome.Waiting, $"Bill {DescribePaymentRequest(paymentRequest)}: PEX has not linked its card charge to this payment, so it was not posted to Aplos. It is retried on the next sync.");
            }

            // The purchase index can trail the link, and reading nothing must not pass for "not booked as a purchase".
            cardCharge = await _pexApiClient.GetCardholderPurchase(mapping.PEXExternalAPIToken, cardTransactionId.Value, cancellationToken);
            if (cardCharge is null)
            {
                logger.LogWarning($"PEX bill payment {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}: its card charge {cardTransactionId} could not be read from PEX.");
                if (utcNow - paymentRequest.PayoutDate.Value.UtcDateTime <= UnlinkedCardPaymentGracePeriod) return (PexBillPaymentOutcome.Waiting, null);

                return (PexBillPaymentOutcome.Waiting, $"Bill {DescribePaymentRequest(paymentRequest)}: its card charge (PEX transaction {cardTransactionId}) could not be read from PEX, so it was not posted to Aplos. It is retried on the next sync.");
            }

            // Booked as a purchase before the toggle was on: the bill is that purchase, once. Aplos is checked too, as the
            // transaction sync does, since its note on the charge can be lost or deleted.
            if (HasNote(cardCharge, GetSyncedNote(cardTransactionId.Value))
                || HasNote(cardCharge, PexCardConst.OLD_SyncedWithAplosNote)
                || WasPexTransactionSyncedToAplos(context.AplosTransactions, cardTransactionId.Value.ToString()))
            {
                await TryAddBillNote(logger, mapping, paymentRequest, $"{SyncedAsPurchaseNote} #{cardTransactionId}", cancellationToken);
                return (PexBillPaymentOutcome.Skipped, $"Bill {DescribePaymentRequest(paymentRequest)}: Skipped: already synced to Aplos as purchase #{cardTransactionId}.");
            }

            creditAccountNumber = mapping.AplosRegisterAccountNumber;
        }

        // Built before the charge is claimed, so a bill its settings can't post never holds the charge back.
        var aplosTransaction = await BuildBillPaymentTransaction(logger, mapping, paymentRequest, context, creditAccountNumber);

        if (cardTransactionId is not null)
        {
            // The row is what the transaction sync trusts, whatever happens to the PEX note.
            if (claim?.CardTransactionId != cardTransactionId)
            {
                try
                {
                    await _billMappingStorage.ClaimPexCreatedCardChargeAsync(mapping.PEXBusinessAcctId, paymentRequest.PaymentRequestId, cardTransactionId.Value, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new InvalidOperationException($"the connector could not record its card charge (PEX transaction {cardTransactionId}), so it was not posted to Aplos. It is retried on the next sync.", ex);
                }
            }

            // Marked before posting: a marker lost after the post would let the transaction sync book the charge too.
            if (!HasNote(cardCharge, SyncedAsBillPaymentNote))
            {
                try
                {
                    await AddCardChargeMarker(mapping, paymentRequest, cardTransactionId.Value, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new InvalidOperationException($"the bill payment note could not be written on its card charge (PEX transaction {cardTransactionId}), so it was not posted to Aplos. It is retried on the next sync.", ex);
                }
            }
        }

        var response = await MakeAplosApiClient(mapping).CreateTransaction(aplosTransaction, cancellationToken);
        var aplosTransactionId = response?.Data?.Transaction?.Id;

        logger.LogInformation($"Posted PEX bill payment {paymentRequest.PaymentRequestId} to Aplos as transaction {aplosTransactionId} for business {mapping.PEXBusinessAcctId}.");

        // Safe to lose: the next run finds the Aplos transaction and records it again.
        if (cardTransactionId is not null)
        {
            await TryMarkPexClaimPosted(logger, mapping, paymentRequest, utcNow, cancellationToken);
        }

        // Safe to lose: the next run finds the Aplos transaction by its note.
        await TryAddBillNote(logger, mapping, paymentRequest, $"{GetBillPaymentSyncedNote(paymentRequest.PaymentRequestId)} on {utcNow:O}. Aplos transaction {aplosTransactionId}.", cancellationToken);

        return (PexBillPaymentOutcome.Posted, null);
    }

    private async Task<AplosApiTransactionDetail> BuildBillPaymentTransaction(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        BillPaymentRequestModel paymentRequest,
        PexBillPaymentContext context,
        decimal creditAccountNumber)
    {
        // One unknown tag answer throws for every bill passed in, so each bill is read on its own.
        var allocations = await _pexApiClient.GetTagAllocations(mapping.PEXExternalAPIToken, [paymentRequest], context.TagDefinitions ?? []);
        if (!allocations.TryGetValue(paymentRequest.PaymentRequestId, out var billAllocations) || billAllocations.Count == 0)
        {
            throw new InvalidOperationException("PEX returned no tag allocation for this bill.");
        }

        List<AplosApiTransactionLineDetail> lines = [];
        foreach (var allocation in billAllocations)
        {
            var values = ResolveBillPaymentAllocation(logger, mapping, context, allocation, paymentRequest.PaymentRequestId);

            // PEX amounts are unsigned; a bill payment is always an outflow.
            var amount = Math.Abs(allocation.Amount);
            lines.Add(new AplosApiTransactionLineDetail
            {
                Account = new AplosApiAccountDetail { AccountNumber = creditAccountNumber },
                Amount = -amount,
                Fund = new AplosApiFundDetail { Id = values.AplosFundId }
            });

            var expenseLine = new AplosApiTransactionLineDetail
            {
                Account = new AplosApiAccountDetail { AccountNumber = values.AplosTransactionAccountNumber },
                Amount = amount,
                Fund = new AplosApiFundDetail { Id = values.AplosFundId }
            };

            ApplyTagsToLine(expenseLine, values);
            lines.Add(expenseLine);
        }

        var payeeName = GetBillPayeeName(paymentRequest, context);
        if (string.IsNullOrEmpty(payeeName))
        {
            throw new InvalidOperationException("PEX has no payee name for this bill, so no Aplos contact could be set.");
        }

        return new AplosApiTransactionDetail
        {
            // Matched by name in Aplos, created when missing (spike S11).
            Contact = new AplosApiContactDetail { CompanyName = payeeName, Type = "company" },
            Amount = Math.Abs(paymentRequest.Amount),
            Date = GetBillPaymentPaidDate(paymentRequest).ToDateTime(TimeOnly.MinValue),
            Note = GetBillPaymentAplosNote(paymentRequest, payeeName),
            Lines = lines.ToArray()
        };
    }

    // Bill payments share the purchases mapping, as reimbursements do (153827); the fund in particular must match purchases
    // so the PEX funds account nets per fund against the statement payment.
    private PexTagValuesModel ResolveBillPaymentAllocation(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        PexBillPaymentContext context,
        AllocationTagValue allocation,
        int paymentRequestId)
    {
        var values = new PexTagValuesModel();
        var expenseAccountMappings = mapping.ExpenseAccountMappings ?? [];
        var defaultAccountNumber = expenseAccountMappings.FirstOrDefault(row => row.DefaultAplosTransactionAccountNumber > 0m)?.DefaultAplosTransactionAccountNumber ?? 0m;
        if (defaultAccountNumber <= 0m)
        {
            defaultAccountNumber = mapping.DefaultAplosTransactionAccountNumber;
        }

        if (!context.UseTags)
        {
            values.AplosFundId = mapping.DefaultAplosFundId;
            values.AplosTransactionAccountNumber = defaultAccountNumber;
        }
        else
        {
            var fundTag = allocation.GetTagValue(mapping.PexFundsTagId);
            if (fundTag is not null)
            {
                var fund = MatchTagOption(logger, context.Funds, context, fundTag)
                    ?? throw new InvalidOperationException($"its PEX fund tag option '{fundTag.Value}' matches no Aplos fund.");
                values.AplosFundId = int.Parse(fund.Id);
            }
            else
            {
                values.AplosFundId = mapping.DefaultAplosFundId;
            }

            var accountTag = expenseAccountMappings
                .Select(row => allocation.GetTagValue(row.ExpenseAccountsPexTagId))
                .FirstOrDefault(tag => tag is not null);
            if (accountTag is not null)
            {
                var account = MatchTagOption(logger, context.ExpenseAccounts, context, accountTag)
                    ?? throw new InvalidOperationException($"its PEX expense account tag option '{accountTag.Value}' matches no Aplos expense account.");
                values.AplosTransactionAccountNumber = decimal.Parse(account.Id);
            }
            else
            {
                values.AplosTransactionAccountNumber = defaultAccountNumber;
            }

            ResolveBillPaymentTags(logger, mapping, context, allocation, values);
        }

        if (values.AplosFundId <= 0 || context.Funds.All(fund => fund.Id != values.AplosFundId.ToString()))
        {
            throw new InvalidOperationException($"no Aplos fund could be set (fund '{values.AplosFundId}'). Tag the bill's fund in PEX or set a default fund.");
        }

        if (values.AplosTransactionAccountNumber <= 0m || context.ExpenseAccounts.All(account => !decimal.TryParse(account.Id, out var number) || number != values.AplosTransactionAccountNumber))
        {
            throw new InvalidOperationException($"no Aplos expense account could be set (account '{values.AplosTransactionAccountNumber}'). Tag the bill's expense account in PEX or set a default expense account on the purchases mapping.");
        }

        logger.LogDebug($"Resolved PEX bill payment {paymentRequestId} allocation {allocation.Amount:C} to fund {values.AplosFundId} and account {values.AplosTransactionAccountNumber}.");
        return values;
    }

    private static void ResolveBillPaymentTags(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        PexBillPaymentContext context,
        AllocationTagValue allocation,
        PexTagValuesModel values)
    {
        values.AplosTagIds = mapping.TagMappings?.Length > 0 ? [] : null;

        foreach (var tagMapping in mapping.TagMappings ?? [])
        {
            if (tagMapping.AplosTagId == "990")
            {
                if (mapping.SyncTaxTagToPex)
                {
                    values.AplosTaxTagId = tagMapping.DefaultAplosTagId;
                }
                continue;
            }

            var tag = allocation.GetTagValue(tagMapping.PexTagId);
            if (tag is not null && tag.TagId != mapping.PexTaxTagId)
            {
                if (MatchTagOption(logger, context.Tags, context, tag) is { } aplosTag)
                {
                    values.AplosTagIds.Add(aplosTag.Id);
                }
            }
            else if (!string.IsNullOrEmpty(tagMapping.DefaultAplosTagId))
            {
                values.AplosTagIds.Add(tagMapping.DefaultAplosTagId);
            }
        }

        if (mapping.SyncTaxTagToPex && allocation.GetTagValue(mapping.PexTaxTagId)?.Value is { } taxTagValue)
        {
            values.AplosTaxTagId = taxTagValue.ToString();
        }
    }

    private static PexAplosApiObject MatchTagOption(ILogger logger, List<PexAplosApiObject> entities, PexBillPaymentContext context, TagValueItem tag)
    {
        var definition = context.DropdownTags?.FirstOrDefault(dropdown => dropdown.Id.Equals(tag.TagId, StringComparison.InvariantCultureIgnoreCase));
        return entities?.FindMatchingEntity(tag.Value?.ToString(), tag.GetTagOptionName(definition?.Options), ':', logger);
    }

    private async Task LoadBillPaymentLookups(ILogger logger, Pex2AplosMappingModel mapping, PexBillPaymentContext context, CancellationToken cancellationToken)
    {
        context.Funds = (await GetAplosFunds(mapping, cancellationToken)).ToList();
        context.ExpenseAccounts = (await GetAplosAccounts(mapping, GetAplosAccountCategory(), cancellationToken)).ToList();
        context.Tags = mapping.TagMappings?.Length > 0 ?(await GetFlattenedAplosTagValues(mapping, cancellationToken)).ToList() : [];
        context.AplosTransactions = await GetTransactions(mapping, context.StartDate, cancellationToken);

        context.UseTags = await _pexApiClient.IsTagsAvailable(mapping.PEXExternalAPIToken, CustomFieldType.Dropdown, cancellationToken);
        if (!context.UseTags) return;

        context.TagDefinitions = await _pexApiClient.GetTags(mapping.PEXExternalAPIToken, cancellationToken);

        var tagIds = new[] { mapping.PexFundsTagId }
            .Concat(mapping.ExpenseAccountMappings?.Select(row => row.ExpenseAccountsPexTagId) ?? [])
            .Concat(mapping.TagMappings?.Where(row => row.AplosTagId != "990").Select(row => row.PexTagId) ?? [])
            .Where(tagId => !string.IsNullOrEmpty(tagId))
            .Distinct(StringComparer.InvariantCultureIgnoreCase);

        context.DropdownTags = [];
        foreach (var tagId in tagIds)
        {
            try
            {
                context.DropdownTags.Add(await _pexApiClient.GetDropdownTag(mapping.PEXExternalAPIToken, tagId, true, cancellationToken));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, $"Exception getting dropdown tag {tagId} for business {mapping.PEXBusinessAcctId}.");
            }
        }
    }

    internal static string GetBillPaymentAplosNote(BillPaymentRequestModel paymentRequest, string payeeName)
    {
        const int noteMaxLength = 1000;
        var idSuffix = $" | {GetBillPaymentAplosNoteId(paymentRequest.PaymentRequestId)}";

        var content = string.Join(" | ", new[] { paymentRequest.BillRefNo, payeeName, GetPaymentTypeNote(paymentRequest) }.Where(part => !string.IsNullOrEmpty(part)));
        if (content.Length > noteMaxLength - idSuffix.Length)
        {
            content = content[..(noteMaxLength - idSuffix.Length)];
        }

        return content + idSuffix;
    }

    private static string GetBillPayeeName(BillPaymentRequestModel paymentRequest, PexBillPaymentContext context)
        => context.PayeeNames.TryGetValue(paymentRequest.PaymentRequestId, out var payeeName) && !string.IsNullOrWhiteSpace(payeeName)
            ? payeeName.Trim()
            : paymentRequest.MerchantName?.Trim();

    private async Task TryMarkPexClaimPosted(ILogger logger, Pex2AplosMappingModel mapping, BillPaymentRequestModel paymentRequest, DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            await _billMappingStorage.MarkPexCreatedClaimPostedAsync(mapping.PEXBusinessAcctId, paymentRequest.PaymentRequestId, utcNow, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to record that PEX bill payment {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId} was posted to Aplos.");
        }
    }

    private async Task TryAddBillNote(ILogger logger, Pex2AplosMappingModel mapping, BillPaymentRequestModel paymentRequest, string noteText, CancellationToken cancellationToken)
    {
        if (!paymentRequest.MetadataRelationId.HasValue) return;

        try
        {
            await _pexApiClient.AddTransactionRelationshipNote(mapping.PEXExternalAPIToken, paymentRequest.MetadataRelationId.Value, noteText, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, $"Failed to write a note on PEX bill payment {paymentRequest.PaymentRequestId} for business {mapping.PEXBusinessAcctId}.");
        }
    }

    private static bool HasBillNote(BillPaymentRequestModel paymentRequest, string noteText)
        => paymentRequest.Metadata?.Notes?.Any(note => note.NoteText?.Contains(noteText, StringComparison.OrdinalIgnoreCase) == true) == true;

    private static bool IsAchPayment(BillPaymentRequestModel paymentRequest)
        => paymentRequest.PayeeFundsDestinationType == PayeeFundsDestinationType.BankAccount;

    private static string DescribePaymentRequest(BillPaymentRequestModel paymentRequest)
        => paymentRequest.BillRefNo ?? paymentRequest.PaymentRequestId.ToString();

    private static string DescribePaymentRequests(IReadOnlyCollection<BillPaymentRequestModel> paymentRequests)
    {
        var references = string.Join(", ", paymentRequests.Take(5).Select(DescribePaymentRequest));
        return paymentRequests.Count > 5 ? $"{references} and {paymentRequests.Count - 5} more" : references;
    }
}
