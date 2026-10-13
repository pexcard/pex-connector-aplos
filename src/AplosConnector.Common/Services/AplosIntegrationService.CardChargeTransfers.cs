using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Models;
using Microsoft.Extensions.Logging;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Services;

public partial class AplosIntegrationService
{
    // The comma ends the payable id, so bill 12 never matches the transfer of bill 123.
    internal static string GetCardChargeTransferNotePrefix(string aplosPayableId) => $"PEX card charge for Aplos bill {aplosPayableId},";

    internal static string GetCardChargeTransferNote(string aplosPayableId, int paymentRequestId)
        => $"{GetCardChargeTransferNotePrefix(aplosPayableId)} PEX payment request #{paymentRequestId}";

    // The payable was paid from the clearing account; this moves the charge to the PEX funds account, which the statement
    // payment debits, so both net to zero per fund. Two balance sheet lines per fund and no expense: the bill already
    // booked it (153880).
    private async Task PostCardChargeTransfer(
        ILogger logger,
        Pex2AplosMappingModel mapping,
        AplosBillMappingModel billMapping,
        BillPaymentRequestModel paymentRequest,
        AplosApiPayableDetail payable,
        CancellationToken cancellationToken)
    {
        var paidDate = GetBillPaymentPaidDate(paymentRequest);
        var notePrefix = GetCardChargeTransferNotePrefix(billMapping.AplosPayableId);
        var amount = Math.Abs(AplosPayableFilter.NormalizeAmount(payable?.Amount ?? 0m));
        var clearingAccountNumber = billMapping.PaidFromClearingAccountNumber.Value;

        try
        {
            var aplosTransactions = await GetTransactions(mapping, paidDate.ToDateTime(TimeOnly.MinValue), cancellationToken);
            if (aplosTransactions?.Any(transaction => transaction.Note?.StartsWith(notePrefix, StringComparison.Ordinal) == true) == true)
            {
                logger.LogInformation($"Skipping the card charge transfer for Aplos payable {billMapping.AplosPayableId} for business {mapping.PEXBusinessAcctId}. It is already in Aplos.");
                return;
            }

            var fundAmounts = GetCardChargeFundAmounts(payable)
                ?? throw new InvalidOperationException($"its lines in Aplos don't add up to the bill amount of {amount:0.00} by fund");

            var transfer = new AplosApiTransactionDetail
            {
                Contact = payable.Contact?.Id > 0 ? new AplosApiContactDetail { Id = payable.Contact.Id } : null,
                Amount = amount,
                Date = paidDate.ToDateTime(TimeOnly.MinValue),
                Note = GetCardChargeTransferNote(billMapping.AplosPayableId, paymentRequest.PaymentRequestId),
                Lines = fundAmounts
                    .SelectMany(fund => new[]
                    {
                        new AplosApiTransactionLineDetail
                        {
                            Account = new AplosApiAccountDetail { AccountNumber = clearingAccountNumber },
                            Amount = fund.Amount,
                            Fund = new AplosApiFundDetail { Id = fund.FundId }
                        },
                        new AplosApiTransactionLineDetail
                        {
                            Account = new AplosApiAccountDetail { AccountNumber = mapping.AplosRegisterAccountNumber },
                            Amount = -fund.Amount,
                            Fund = new AplosApiFundDetail { Id = fund.FundId }
                        }
                    })
                    .ToArray()
            };

            await MakeAplosApiClient(mapping).CreateTransaction(transfer, cancellationToken);
            logger.LogInformation($"Moved the card charge of Aplos payable {billMapping.AplosPayableId} from the clearing account to the PEX funds account for business {mapping.PEXBusinessAcctId}.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CardChargeTransferException($"paid in Aplos from clearing account {clearingAccountNumber}, but {amount:0.00} debit {clearingAccountNumber} / credit {mapping.AplosRegisterAccountNumber} could not be posted ({DescribeFailure(ex)}). Retried on the next sync.", ex);
        }
    }

    // Signed, so a credit line such as a discount lowers its fund's share. Null when the funds don't add up to the bill.
    internal static List<(int FundId, decimal Amount)> GetCardChargeFundAmounts(AplosApiPayableDetail payable)
    {
        var fundAmounts = (payable?.Lines ?? [])
            .Where(line => line.Fund is not null)
            .GroupBy(line => line.Fund.Id)
            .Select(fund => (FundId: fund.Key, Amount: fund.Sum(line => line.Amount)))
            .Where(fund => fund.Amount != 0m)
            .ToList();

        if (fundAmounts.Sum(fund => fund.Amount) < 0m)
        {
            fundAmounts = [.. fundAmounts.Select(fund => (fund.FundId, -fund.Amount))];
        }

        var amount = Math.Abs(AplosPayableFilter.NormalizeAmount(payable?.Amount ?? 0m));
        return fundAmounts.Count > 0 && fundAmounts.Sum(fund => fund.Amount) == amount ? fundAmounts : null;
    }

    private sealed class CardChargeTransferException(string message, Exception innerException) : InvalidOperationException(message, innerException);
}
