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

public class CardChargeTransferTests : CardChargeTransferTestBase
{
    [Fact]
    public async Task ALiabilityPexFundsAccountPaysFromTheClearingAccountThenMovesTheCharge()
    {
        SetupCardBill(CreditCardAccount);

        await RunCardSync();

        Assert.Equal(CardClearingAccountNumber, Assert.Single(_payments).CashAccountNumber);
        var transfer = Assert.Single(_aplosTransactions);
        Assert.Equal($"PEX card charge for Aplos bill {AplosPayableId}, PEX payment request #{PexBillId}", transfer.Note);
        Assert.Equal((BillAmount, new DateTime(2026, 9, 10), ContactId), (transfer.Amount, transfer.Date, transfer.Contact.Id));
        Assert.Equal(
            [(CardClearingAccountNumber, BillAmount, FundId), (PexFundsAccountNumber, -BillAmount, FundId)],
            transfer.Lines.Select(line => (line.Account.AccountNumber, line.Amount, line.Fund.Id)));

        var row = Assert.Single(_billMappingStorage.Rows.Values);
        Assert.NotNull(row.PaidFromClearingAccountNumber);
        Assert.NotNull(row.PaidSyncedUtc);
        Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(_historyRows).SyncStatus);
    }

    // 153880's acceptance test: the bill as Aplos already holds it, the pay call, the transfer and the PEX statement
    // payment together leave the clearing and PEX funds accounts at zero per fund, and book the expense once.
    [Fact]
    public async Task PerFundTheClearingAndPexFundsAccountsNetToZeroAfterTheStatementPayment()
    {
        const decimal ExpenseAccountNumber = 5003m;
        const decimal AccountsPayableNumber = 2100m;
        const decimal BankAccountNumber = 1000m;
        (decimal Amount, int FundId)[] billLines = [(100.50m, FundId), (25m, OtherFundId)];
        SetupCardBill(CreditCardAccount, lines: billLines);

        var mapping = await RunCardSync();
        var transfer = Assert.Single(_aplosTransactions);

        mapping.TransfersAplosTransactionAccountNumber = BankAccountNumber;
        mapping.TransfersAplosContactId = ContactId;
        var statementResult = await GetAplosIntegrationService().SyncInvoiceRebateDistribute(
            mapping,
            new InvoiceModel { InvoiceId = 94460, InvoiceAmount = BillAmount, Status = InvoiceStatus.Closed, DueDate = new DateTime(2026, 10, 1) },
            billLines.Select(line => new InvoiceAllocationModel { InvoiceId = 94460, TagValue = line.FundId.ToString(), TotalAmount = line.Amount }).ToList(),
            [new InvoicePaymentModel { Type = PaymentType.PEXTransfer, Amount = BillAmount, DatePaid = new DateTime(2026, 10, 1) }],
            [new PexAplosApiObject { Id = FundId.ToString() }, new PexAplosApiObject { Id = OtherFundId.ToString() }],
            NullLogger.Instance,
            default);
        Assert.Equal(TransactionSyncResult.Success, statementResult);
        var statementPayment = Assert.Single(_aplosTransactions, transaction => transaction != transfer);

        // Debits positive. The bill and its payment are booked by Aplos itself, from the payable's lines.
        var cashAccountNumber = Assert.Single(_payments).CashAccountNumber;
        var ledger = billLines
            .SelectMany(line => new[]
            {
                (Account: ExpenseAccountNumber, line.FundId, Amount: line.Amount),
                (Account: AccountsPayableNumber, line.FundId, Amount: -line.Amount),
                (Account: AccountsPayableNumber, line.FundId, Amount: line.Amount),
                (Account: cashAccountNumber, line.FundId, Amount: -line.Amount)
            })
            .Concat(transfer.Lines.Concat(statementPayment.Lines).Select(line => (Account: line.Account.AccountNumber, FundId: line.Fund.Id, line.Amount)))
            .ToList();

        decimal Balance(decimal account, int fundId) => ledger.Where(entry => entry.Account == account && entry.FundId == fundId).Sum(entry => entry.Amount);
        foreach (var fundId in new[] { FundId, OtherFundId })
        {
            Assert.Equal(0m, Balance(CardClearingAccountNumber, fundId));
            Assert.Equal(0m, Balance(PexFundsAccountNumber, fundId));
            Assert.Equal(0m, Balance(AccountsPayableNumber, fundId));
        }

        Assert.Equal(BillAmount, ledger.Where(entry => entry.Account == ExpenseAccountNumber).Sum(entry => entry.Amount));
        Assert.Equal(-BillAmount, ledger.Where(entry => entry.Account == BankAccountNumber).Sum(entry => entry.Amount));
    }

    [Fact]
    public async Task EachFundOfThePayableGetsItsOwnTransferPair()
    {
        SetupCardBill(CreditCardAccount, lines: [(100.50m, FundId), (25m, OtherFundId)]);

        await RunCardSync();

        var transfer = Assert.Single(_aplosTransactions);
        Assert.Equal(4, transfer.Lines.Length);
        Assert.Equal(-25m, Assert.Single(transfer.Lines, line => line.Account.AccountNumber == PexFundsAccountNumber && line.Fund.Id == OtherFundId).Amount);
        Assert.Equal(0m, transfer.Lines.Sum(line => line.Amount));
    }

    [Fact]
    public async Task ACreditLineLowersItsFundsShareOfTheTransfer()
    {
        SetupCardBill(CreditCardAccount, lines: [(BillAmount + 20m, FundId), (-20m, OtherFundId)]);

        await RunCardSync();

        var transfer = Assert.Single(_aplosTransactions);
        Assert.Equal(
            [(CardClearingAccountNumber, BillAmount + 20m, FundId), (PexFundsAccountNumber, -(BillAmount + 20m), FundId),
             (CardClearingAccountNumber, -20m, OtherFundId), (PexFundsAccountNumber, 20m, OtherFundId)],
            transfer.Lines.Select(line => (line.Account.AccountNumber, line.Amount, line.Fund.Id)));
    }

    [Fact]
    public async Task ABillWhoseFundsDontAddUpIsNotPaidFromTheClearingAccount()
    {
        SetupCardBill(CreditCardAccount);
        _mockAplosApiClient
            .Setup(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AplosApiPayableResponse { Data = new AplosApiPayableData { Payable = Payable(BillAmount, 0m, [(BillAmount - 1m, FundId)]) } });

        await RunCardSync();

        VerifyNoPayCall();
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidFromClearingAccountNumber);
        Assert.Contains("don't add up to the bill amount of 125.50 by fund", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ADisabledPexFundsAccountWaitsWithNoPayCall()
    {
        SetupCardBill(new AplosApiAccountDetail { Name = "PEX Account", Category = "asset", Type = "Register", Activity = "cash", IsEnabled = false });

        await RunCardSync();

        VerifyNoPayCall();
        Assert.Contains(", PEX Account) is disabled in Aplos", Assert.Single(_historyRows).SyncNotes);
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).FirstFailedUtc);
    }

    [Fact]
    public async Task AnAssetRegisterPexFundsAccountIsPaidFromDirectlyWithNoTransferOrClearingAccount()
    {
        SetupCardBill(PrepaidAccount);

        await RunCardSync(cardClearingAccountNumber: 0m);

        Assert.Equal(PexFundsAccountNumber, Assert.Single(_payments).CashAccountNumber);
        Assert.Empty(_aplosTransactions);
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidFromClearingAccountNumber);
    }

    // Spike S12: Aplos refused Equipment (asset, cash activity, no Register type) as the pay account.
    [Fact]
    public async Task AnAssetPexFundsAccountThatIsNotARegisterGoesThroughTheClearingAccount()
    {
        SetupCardBill(EquipmentAccount);

        await RunCardSync();

        Assert.Equal(CardClearingAccountNumber, Assert.Single(_payments).CashAccountNumber);
        Assert.Single(_aplosTransactions);
    }

    [Fact]
    public async Task AnAchBillIsPaidFromTheAchBankAccountWithNoTransfer()
    {
        SetupCardBill(CreditCardAccount, PayeeFundsDestinationType.BankAccount);

        await RunCardSync();

        Assert.Equal(AchClearingAccountNumber, Assert.Single(_payments).CashAccountNumber);
        Assert.Empty(_aplosTransactions);
    }

    [Fact]
    public async Task WithNoClearingAccountACreditBusinessCardBillWaitsAndSaysWhatToSet()
    {
        SetupCardBill(CreditCardAccount);

        await RunCardSync(cardClearingAccountNumber: 0m);

        VerifyNoPayCall();
        Assert.Contains("Set the Aplos vendor card clearing account for bill payments in the connector settings.", Assert.Single(_historyRows).SyncNotes);
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).FirstFailedUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1020)]
    public async Task APrepaidBusinessOnANonRegisterAccountIsToldToChooseABankAccountWhateverClearingAccountIsSaved(int cardClearingAccountNumber)
    {
        SetupCardBill(EquipmentAccount);

        await RunSync(useBillPay: true, cardClearingAccountNumber: cardClearingAccountNumber, configure: mapping => mapping.PEXFundingSource = FundingSource.Prepaid);

        VerifyNoPayCall();
        Assert.Empty(_aplosTransactions);
        var notes = Assert.Single(_historyRows).SyncNotes;
        Assert.Contains("Choose a bank account to represent your PEX funds in the connector settings.", notes);
        Assert.DoesNotContain("clearing account", notes);
    }

    [Fact]
    public async Task AFailedTransferFailsTheBillAndTheNextRunPostsOnlyTheTransfer()
    {
        SetupCardBill(CreditCardAccount);
        _mockAplosApiClient
            .SetupSequence(client => client.CreateTransaction(It.IsAny<AplosApiTransactionDetail>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AplosApiException(new AplosApiErrorResponse { Status = 503 }))
            .ReturnsAsync(() =>
            {
                _aplosTransactions.Add(new AplosApiTransactionDetail { Note = AplosIntegrationService.GetCardChargeTransferNote(AplosPayableId, PexBillId) });
                return new AplosApiTransactionResponse();
            });

        await RunCardSync();

        var row = Assert.Single(_billMappingStorage.Rows.Values);
        Assert.Null(row.PaidSyncedUtc);
        var history = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), history.SyncStatus);
        Assert.Contains($"paid in Aplos from clearing account {CardClearingAccountNumber}, but 125.50 debit {CardClearingAccountNumber} / credit {PexFundsAccountNumber} could not be posted (", history.SyncNotes);
        Assert.Contains("). Retried on the next sync.", history.SyncNotes);
        Assert.DoesNotContain("by hand", history.SyncNotes);

        SetupLivePayableWithLines(paid: BillAmount, lines: [(BillAmount, FundId)]);
        await RunCardSync();

        Assert.Single(_payments);
        Assert.Single(_aplosTransactions);
        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }

    [Fact]
    public async Task AFailingCardBillSaysWhichAccountsToUseByHand()
    {
        SetupCardBill(CreditCardAccount);
        SetupPayCall().ThrowsAsync(new AplosApiException(new AplosApiErrorResponse { Status = 503 }));

        await RunCardSync();

        Assert.Contains(
            $"then mark it paid in Aplos by hand from clearing account {CardClearingAccountNumber} and post 125.50 as debit {CardClearingAccountNumber} / credit {PexFundsAccountNumber}.",
            Assert.Single(_historyRows).SyncNotes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ACardChargeAlreadyBookedAsAPurchaseGivesTheFixForTheBusinessRail(bool credit)
    {
        SetupCardBill(credit ? CreditCardAccount : PrepaidAccount);
        _cardTransactionNotes.Add($"Synced transaction #{VendorCardTransactionId} to Aplos on 2026-09-10T00:00:00.0000000Z");

        await RunCardSync();

        VerifyNoPayCall();
        var notes = Assert.Single(_historyRows).SyncNotes;
        Assert.Contains($"(PEX transaction {VendorCardTransactionId})", notes);
        Assert.Contains(credit
            ? $"Delete that expense in Aplos and mark it paid in Aplos by hand from clearing account {CardClearingAccountNumber} and post 125.50 as debit {CardClearingAccountNumber} / credit {PexFundsAccountNumber}."
            : "Delete that expense in Aplos and mark it paid in Aplos by hand.", notes);
        Assert.DoesNotContain("Retried until", notes);
    }

    [Fact]
    public async Task ACreditCardBillNeverLinkedGivesTheClearingAccountFix()
    {
        SetupCardBill(CreditCardAccount);
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, linkedCardCharge: false, payoutDate: PayoutDate.AddDays(-3));

        await RunCardSync();

        Assert.Contains(
            $"Mark it paid in Aplos by hand from clearing account {CardClearingAccountNumber} and post 125.50 as debit {CardClearingAccountNumber} / credit {PexFundsAccountNumber}, and if the card charge was synced",
            Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ATransferAlreadyInAplosIsNotPostedAgain()
    {
        SetupCardBill(CreditCardAccount, paid: BillAmount);
        _billMappingStorage.Rows.Values.Single().PaidFromClearingAccountNumber = CardClearingAccountNumber;
        _aplosTransactions.Add(new AplosApiTransactionDetail { Note = AplosIntegrationService.GetCardChargeTransferNote(AplosPayableId, PexBillId) });

        await RunCardSync();

        VerifyNoPayCall();
        Assert.Single(_aplosTransactions);
        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }

    [Fact]
    public async Task TheTransferOfAnotherBillWhoseIdStartsTheSameDoesNotCount()
    {
        SetupCardBill(CreditCardAccount, paid: BillAmount);
        _billMappingStorage.Rows.Values.Single().PaidFromClearingAccountNumber = CardClearingAccountNumber;
        _aplosTransactions.Add(new AplosApiTransactionDetail { Note = AplosIntegrationService.GetCardChargeTransferNote($"{AplosPayableId}7", 1) });

        await RunCardSync();

        Assert.Equal(2, _aplosTransactions.Count);
    }
}
