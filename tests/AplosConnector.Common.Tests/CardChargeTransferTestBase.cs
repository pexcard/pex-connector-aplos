using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Language.Flow;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

// An imported bill paid by vendor card when Aplos can't pay from the PEX funds account: pay from the clearing account,
// then move the charge to the PEX funds account (153880).
public abstract class CardChargeTransferTestBase : BillPaymentsTestBase
{
    protected const int FundId = 225755;
    protected const int OtherFundId = 225756;
    protected const int ContactId = 26335504;

    protected static readonly AplosApiAccountDetail CreditCardAccount = new() { Name = "PEX Charge Card", Category = "liability", Type = "Register", Activity = "operating", IsEnabled = true };
    protected static readonly AplosApiAccountDetail PrepaidAccount = new() { Name = "PEX Account", Category = "asset", Type = "Register", Activity = "cash", IsEnabled = true };
    protected static readonly AplosApiAccountDetail EquipmentAccount = new() { Name = "Equipment", Category = "asset", Activity = "cash", IsEnabled = true };
    protected static readonly AplosApiAccountDetail ClearingAccount = new() { Name = "Card Clearing", Category = "asset", Type = "Register", Activity = "cash", IsEnabled = true };

    protected readonly List<AplosApiTransactionDetail> _aplosTransactions = [];
    protected readonly Dictionary<decimal, AplosApiAccountDetail> _accounts = [];

    protected CardChargeTransferTestBase()
    {
        _mockAplosApiClient
            .Setup(client => client.GetAccount(It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal accountNumber, CancellationToken _) => new AplosApiAccountResponse
            {
                Data = new AplosApiAccountData { Account = _accounts.TryGetValue(accountNumber, out var account) ? account : null }
            });
        _mockAplosApiClient
            .Setup(client => client.GetTransactions(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [.. _aplosTransactions]);
        _mockAplosApiClient
            .Setup(client => client.CreateTransaction(It.IsAny<AplosApiTransactionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<AplosApiTransactionDetail, CancellationToken>((transaction, _) => _aplosTransactions.Add(transaction))
            .ReturnsAsync(new AplosApiTransactionResponse());

        _accounts[AchClearingAccountNumber] = ClearingAccount;
        _accounts[CardClearingAccountNumber] = ClearingAccount;
    }

    protected void SetupPayableReads(params AplosApiPayableDetail[] reads)
    {
        var sequence = _mockAplosApiClient.SetupSequence(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()));
        foreach (var read in reads)
        {
            sequence = sequence.ReturnsAsync(new AplosApiPayableResponse { Data = new AplosApiPayableData { Payable = read } });
        }
    }

    protected ISetup<IAplosApiClient, Task<AplosApiPayableResponse>> SetupPayCall()
        => _mockAplosApiClient.Setup(client => client.PayPayable(It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()));

    protected void SetupCardBill(
        AplosApiAccountDetail pexFundsAccount,
        PayeeFundsDestinationType fundsDestinationType = PayeeFundsDestinationType.VendorVirtualCard,
        decimal paid = 0m,
        (decimal Amount, int FundId)[] lines = null)
    {
        _accounts[PexFundsAccountNumber] = pexFundsAccount;
        SeedUnpaidMapping();
        SetupPaidPexBill(fundsDestinationType);
        SetupLivePayableWithLines(paid, lines ?? [(BillAmount, FundId)]);
    }

    protected void SetupLivePayableWithLines(decimal paid, (decimal Amount, int FundId)[] lines)
    {
        var amount = lines.Sum(line => line.Amount);
        _mockAplosApiClient
            .Setup(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AplosApiPayableResponse
            {
                Data = new AplosApiPayableData { Payable = Payable(amount, paid, lines) }
            });
        _mockAplosApiClient
            .Setup(client => client.PayPayable(It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, AplosApiPayablePaymentModel, CancellationToken>((_, payment, _) => _payments.Add(payment))
            .ReturnsAsync(() => new AplosApiPayableResponse
            {
                Data = new AplosApiPayableData { Payable = Payable(amount, amount, lines) }
            });
    }

    protected static AplosApiPayableDetail Payable(decimal amount, decimal paid, (decimal Amount, int FundId)[] lines) => new()
    {
        Id = AplosPayableId,
        ReferenceNumber = "INV-9001",
        Amount = amount,
        PaidAmount = paid,
        Contact = new AplosApiContactDetail { Id = ContactId },
        Lines = lines.Select(line => new AplosApiTransactionLineDetail
        {
            Amount = line.Amount,
            Account = new AplosApiAccountDetail { AccountNumber = 5003m },
            Fund = new AplosApiFundDetail { Id = line.FundId }
        }).ToArray()
    };

    protected Task<Pex2AplosMappingModel> RunCardSync(decimal cardClearingAccountNumber = CardClearingAccountNumber)
        => RunSync(useBillPay: true, configure: mapping => mapping.BillPaymentsCardClearingAccountNumber = cardClearingAccountNumber);
}
