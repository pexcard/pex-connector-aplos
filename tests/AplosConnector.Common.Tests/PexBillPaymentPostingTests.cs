using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Const;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Services;
using Moq;
using Newtonsoft.Json;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class PexBillPaymentPostingTests : PexBillPaymentsTestBase
{
    [Fact]
    public async Task AnAchBillPostsOneTransactionCreditingTheAchBankAccount()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount);

        await RunPexBillSync();

        var posted = Assert.Single(_postedTransactions);
        Assert.Equal(BillAmount, posted.Amount);
        Assert.Equal(new DateTime(2026, 9, 10), posted.Date);
        Assert.Equal(PayeeName, posted.Contact.CompanyName);
        Assert.Equal($"Bill PEX-{PexBillId} | Payee {PayeeName} | Paid via ACH | {AplosNoteId(PexBillId)}", posted.Note);
        AssertLines(posted, AchClearingAccountNumber, PurchasesAccountNumber, FundId);

        Assert.Contains($"Synced bill payment #{PexBillId} to Aplos on", Assert.Single(_relationshipNotes));
        Assert.Contains("Aplos transaction 77000", _relationshipNotes.Single());
        var history = Assert.Single(_historyRows);
        Assert.Equal((SyncTypes.BillPayments, SyncStatus.Success.ToString(), 1), (history.SyncType, history.SyncStatus, history.SyncedRecords));
    }

    [Fact]
    public async Task AnAchPaymentToAVendorThatPrefersCardCreditsTheAchBankAccount()
    {
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, linkedCardCharge: false, transferType: PaymentTransferType.BillPayAch);

        await RunPexBillSync();

        AssertLines(Assert.Single(_postedTransactions), AchClearingAccountNumber, PurchasesAccountNumber, FundId);
    }

    [Theory]
    [InlineData(PayeeFundsDestinationType.SingleUseVendorVirtualCard)]
    [InlineData(PayeeFundsDestinationType.VendorVirtualCard)]
    public async Task ACardBillCreditsThePexFundsAccountAndMarksItsChargeFirst(PayeeFundsDestinationType fundsDestinationType)
    {
        AddPexBill(fundsDestinationType);

        await RunPexBillSync();

        var posted = Assert.Single(_postedTransactions);
        AssertLines(posted, PexFundsAccountNumber, PurchasesAccountNumber, FundId);
        Assert.EndsWith($"Paid via Card | {AplosNoteId(PexBillId)}", posted.Note);
        Assert.Contains(Assert.Single(_cardNotesAtPost), note => note.Contains(AplosIntegrationService.SyncedAsBillPaymentNote));
    }

    [Fact]
    public async Task WithNoAchBankAccountOnlyAchBillsWaitAndTheCustomerIsToldWhatToSet()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount);
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, billId: 4343);

        await RunPexBillSync(achBankAccountNumber: 0m);

        var posted = Assert.Single(_postedTransactions);
        Assert.EndsWith(AplosNoteId(4343), posted.Note);
        var history = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Partial.ToString(), history.SyncStatus);
        Assert.Contains($"PEX-{PexBillId} not posted: Set the Aplos ACH bank account for bill payments in the connector settings.", history.SyncNotes);
    }

    [Fact]
    public async Task APartialRunSaysHowManyBillPaymentsNeedAttention()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount);
        AddPexBill(PayeeFundsDestinationType.BankAccount, billId: 4344);
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, billId: 4343);

        await RunPexBillSync(achBankAccountNumber: 0m);

        var history = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Partial.ToString(), history.SyncStatus);
        Assert.StartsWith("Failed to sync 2 bill payments to Aplos.\n", history.SyncNotes);
    }

    [Fact]
    public async Task BillTagsResolveThroughThePurchasesMapping()
    {
        UseTags(FundTagId, PurchasesAccountTagId);
        AddPexBill(PayeeFundsDestinationType.BankAccount, tagAnswers:
        [
            new TagAnswerModel { FieldId = FundTagId, Value = OtherFundId.ToString() },
            new TagAnswerModel { FieldId = PurchasesAccountTagId, Value = TaggedAccountNumber.ToString() }
        ]);

        await RunPexBillSync();

        AssertLines(Assert.Single(_postedTransactions), AchClearingAccountNumber, TaggedAccountNumber, OtherFundId);
    }

    [Fact]
    public async Task AnUntaggedBillTakesThePurchasesDefaultAccountAndFund()
    {
        UseTags(FundTagId, PurchasesAccountTagId);
        AddPexBill(PayeeFundsDestinationType.BankAccount);

        await RunPexBillSync();

        AssertLines(Assert.Single(_postedTransactions), AchClearingAccountNumber, PurchasesAccountNumber, FundId);
    }

    [Fact]
    public async Task WithNoDefaultOnThePurchasesTagAnUntaggedBillTakesTheDefaultTransactionAccount()
    {
        UseTags(FundTagId, PurchasesAccountTagId);
        AddPexBill(PayeeFundsDestinationType.BankAccount);

        await RunPexBillSync(configure: mapping =>
        {
            mapping.ExpenseAccountMappings = [new ExpenseAccountMappingModel { ExpenseAccountsPexTagId = PurchasesAccountTagId }];
            mapping.DefaultAplosTransactionAccountNumber = NoTagsAccountNumber;
        });

        AssertLines(Assert.Single(_postedTransactions), AchClearingAccountNumber, NoTagsAccountNumber, FundId);
    }

    [Fact]
    public async Task WithoutTagsABillTakesTheDefaultAccountShownInSettingsNotAStaleTagDefault()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount);

        await RunPexBillSync(configure: mapping => mapping.DefaultAplosTransactionAccountNumber = NoTagsAccountNumber);

        AssertLines(Assert.Single(_postedTransactions), AchClearingAccountNumber, NoTagsAccountNumber, FundId);
    }

    [Fact]
    public async Task ABillsTaxTagIsReadFromThePurchasesTaxTag()
    {
        UseTags(FundTagId, PurchasesAccountTagId, "tax-tag");
        AddPexBill(PayeeFundsDestinationType.BankAccount, tagAnswers: [new TagAnswerModel { FieldId = "tax-tag", Value = "990-7" }]);

        await RunPexBillSync(configure: mapping =>
        {
            mapping.SyncTaxTagToPex = true;
            mapping.PexTaxTagId = "tax-tag";
        });

        var expense = Assert.Single(Assert.Single(_postedTransactions).Lines, line => line.Account.AccountNumber == PurchasesAccountNumber);
        Assert.Equal("990-7", expense.TaxTag?.Id);
    }

    [Fact]
    public async Task EachTagAllocationOfASplitBillGetsItsOwnLines()
    {
        AddSplitPexBill(PayeeFundsDestinationType.BankAccount, 100m, 60m, 40m);

        await RunPexBillSync();

        var posted = Assert.Single(_postedTransactions);
        Assert.Equal(100m, posted.Amount);
        Assert.Equal(4, posted.Lines.Length);
        Assert.Equal(-40m, Assert.Single(posted.Lines, line => line.Account.AccountNumber == AchClearingAccountNumber && line.Fund.Id == OtherFundId).Amount);
        Assert.Equal(60m, Assert.Single(posted.Lines, line => line.Account.AccountNumber == PurchasesAccountNumber && line.Fund.Id == FundId).Amount);
    }

    [Theory]
    [InlineData(60, 30, "The bill's split tag amounts total 90.00, which does not match the bill total of 100.00.")]
    [InlineData(110, -10, "The bill's split has a negative amount (-10.00).")]
    public async Task ACardBillWhoseSplitDoesNotAddUpFailsWithoutClaimingItsCharge(decimal first, decimal second, string reason)
    {
        AddSplitPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, 100m, first, second);

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.Empty(_billMappingStorage.Rows);
        Assert.Empty(_cardTransactionNotes);
        var history = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), history.SyncStatus);
        Assert.Contains($"Bill PEX-{PexBillId}: {reason} Correct the split in PEX and sync again.", history.SyncNotes);
    }

    [Fact]
    public async Task ABillWithNoExpenseAccountFailsNamingTheBillAndWhatToSet()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount);

        await RunPexBillSync(configure: mapping =>
        {
            mapping.ExpenseAccountMappings = null;
            mapping.DefaultAplosTransactionAccountNumber = 0m;
        });

        Assert.Empty(_postedTransactions);
        var history = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), history.SyncStatus);
        Assert.Contains($"Bill PEX-{PexBillId}: no Aplos expense account could be set", history.SyncNotes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task ABillWithNoReferenceIsNamedByItsPayeeDateAndAmount(string billRefNo)
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount).BillRefNo = billRefNo;

        await RunPexBillSync(configure: mapping =>
        {
            mapping.ExpenseAccountMappings = null;
            mapping.DefaultAplosTransactionAccountNumber = 0m;
        });

        Assert.Contains("Bill Acme Supplies, 2026-09-10, $125.50: no Aplos expense account could be set", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task AnAchBillWhosePaymentHasNotSettledWaits()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount);
        SetupAchPayment(PaymentStatusTrigger.AwaitingOutboundCompletion);

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(_historyRows).SyncStatus);
    }

    [Fact]
    public async Task AnAchBillPaidBeforeTheWindowStillWaitsOnItsUnsettledPayment()
    {
        var payoutDate = new DateTimeOffset(2026, 7, 20, 15, 0, 0, TimeSpan.Zero);
        AddPexBill(PayeeFundsDestinationType.BankAccount, payoutDate: payoutDate);
        _mockPexApiClient
            .Setup(client => client.GetPayments(It.IsAny<string>(), It.IsAny<PaymentListRequestModel>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, PaymentListRequestModel request, int _, int _, CancellationToken _) => new PaymentListResponseModel
            {
                Items = request.OutboundAchCreationStartDate <= payoutDate.UtcDateTime
                    ? [new PaymentModel { PaymentId = AchPaymentId, PaymentStatusTrigger = PaymentStatusTrigger.Returned }]
                    : [],
                PageInfo = new PageInfoModel { Page = 1, PageSize = 50, TotalItems = 1 }
            });

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
    }

    [Fact]
    public async Task AnAchBillWaitsOnAReturnedPaymentCreatedDaysBeforeItsPayout()
    {
        var payoutDate = new DateTimeOffset(2026, 7, 20, 15, 0, 0, TimeSpan.Zero);
        AddPexBill(PayeeFundsDestinationType.BankAccount, payoutDate: payoutDate);
        var achCreated = payoutDate.UtcDateTime.AddDays(-2);
        _mockPexApiClient
            .Setup(client => client.GetPayments(It.IsAny<string>(), It.IsAny<PaymentListRequestModel>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, PaymentListRequestModel request, int _, int _, CancellationToken _) => new PaymentListResponseModel
            {
                Items = request.OutboundAchCreationStartDate <= achCreated
                    ? [new PaymentModel { PaymentId = AchPaymentId, PaymentStatusTrigger = PaymentStatusTrigger.Returned }]
                    : [],
                PageInfo = new PageInfoModel { Page = 1, PageSize = 50, TotalItems = 1 }
            });

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
    }

    [Fact]
    public async Task ABillPaidOutsidePexIsSkippedWithItsReason()
    {
        AddPexBill(PayeeFundsDestinationType.NonPlatform);

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        var history = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Success.ToString(), history.SyncStatus);
        Assert.Contains("Skipped 1 bill paid outside PEX.", history.SyncNotes);
    }

    [Fact]
    public async Task ACardBillWithNoLinkedChargeIsHeldNotPosted()
    {
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, linkedCardCharge: false, payoutDate: PayoutDate.AddDays(-5));

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        var history = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), history.SyncStatus);
        Assert.Contains($"PEX-{PexBillId}: PEX has not linked its card charge", history.SyncNotes);
    }

    [Fact]
    public async Task AFailedMarkerWriteLeavesTheCardBillUnposted()
    {
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        _mockPexApiClient
            .Setup(client => client.AddTransactionNote(It.IsAny<string>(), It.IsAny<TransactionModel>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PEX unavailable"));

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.Contains("could not be written on its card charge", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ACardBillsChargeIsClaimedInTheConnectorsTable()
    {
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);

        await RunPexBillSync();

        Assert.Single(_postedTransactions);
        var claim = Assert.Single(_billMappingStorage.Rows.Values);
        Assert.Equal((PexBillId, VendorCardTransactionId, (string)null), (claim.PexPaymentRequestId, claim.CardTransactionId, claim.AplosPayableId));
        Assert.NotNull(claim.PaidSyncedUtc);
    }

    [Fact]
    public async Task ACardBillItsSettingsCannotPostLeavesItsChargeUnclaimedAndUnmarked()
    {
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);

        await RunPexBillSync(configure: mapping =>
        {
            mapping.ExpenseAccountMappings = [];
            mapping.DefaultAplosTransactionAccountNumber = 0m;
        });

        Assert.Empty(_postedTransactions);
        Assert.Empty(_billMappingStorage.Rows);
        Assert.Empty(_cardTransactionNotes);
        Assert.Contains("no Aplos expense account could be set", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task AFailedClaimWriteLeavesTheCardBillUnmarkedAndUnposted()
    {
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        _billMappingStorage.FailNextWrite = true;

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.Empty(_cardTransactionNotes);
        Assert.Contains("could not record its card charge", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ACardBillWhoseChargePexCannotReturnIsHeldNotPosted()
    {
        AddPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, payoutDate: PayoutDate.AddDays(-5));
        _mockPexApiClient
            .Setup(client => client.GetCardholderPurchase(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionResultModel)null);

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        Assert.Contains($"PEX-{PexBillId}: its card charge (PEX transaction {VendorCardTransactionId}) could not be read from PEX", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task AClaimForABillCreatedInPexIsNotReportedAsAnImportedBillWaiting()
    {
        _billMappingStorage.Rows["claim"] = new AplosBillMappingModel { PEXBusinessAcctId = PexBusinessAcctId, PexPaymentRequestId = PexBillId, CardTransactionId = VendorCardTransactionId };

        await RunPexBillSync(achBankAccountNumber: 0m);

        Assert.DoesNotContain("Waiting:", Assert.Single(_historyRows).SyncNotes ?? "");
    }

    [Fact]
    public async Task AnImportedBillIsStillMarkedPaidAndNotPostedAsAPexBill()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.BankAccount);
        SetupLivePayable(BillAmount, paid: 0m);

        await RunPexBillSync();

        Assert.Single(_payments);
        Assert.Empty(_postedTransactions);
    }

    private void AddSplitPexBill(PayeeFundsDestinationType fundsDestinationType, decimal amount, decimal first, decimal second)
    {
        UseTags(FundTagId, PurchasesAccountTagId);
        AddPexBill(fundsDestinationType, amount: amount, tagAnswers:
        [
            new TagAnswerModel { FieldId = "split-tag", Value = Split(first, FundId) },
            new TagAnswerModel { FieldId = "split-tag-2", Value = Split(second, OtherFundId) }
        ]);
        _mockPexApiClient
            .Setup(client => client.GetTags(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TagDetailsModel { Id = "split-tag", Type = CustomFieldType.Allocation },
                new TagDetailsModel { Id = "split-tag-2", Type = CustomFieldType.Allocation }
            ]);

        static string Split(decimal amount, int fundId) => JsonConvert.SerializeObject(new AllocationTagValue
        {
            Amount = amount,
            Allocation = [new TagValueItem { TagId = FundTagId, Value = fundId.ToString() }]
        });
    }

    [Fact]
    public async Task AFailedAchStatusReadIsNotRepeatedForEachBill()
    {
        AddPexBill(PayeeFundsDestinationType.BankAccount);
        AddPexBill(PayeeFundsDestinationType.BankAccount, billId: 4343);
        _mockPexApiClient
            .Setup(client => client.GetPayments(It.IsAny<string>(), It.IsAny<PaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PEX is down"));

        await RunPexBillSync();

        Assert.Empty(_postedTransactions);
        _mockPexApiClient.Verify(client => client.GetPayments(It.IsAny<string>(), It.IsAny<PaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TurningPexBillPayOffClearsBothBillToggles()
    {
        var mapping = new Pex2AplosMappingModel { SyncBillPayments = true, SyncOutstandingBills = true };
        _mockPexApiClient
            .Setup(client => client.GetBusinessSettings(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessSettingsModel { UseBillPay = false });

        await GetAplosIntegrationService().RefreshBusinessSettings(mapping, persistChanges: false, CancellationToken.None);

        Assert.Equal((false, false), (mapping.SyncBillPayments, mapping.SyncOutstandingBills));
    }

    private static void AssertLines(AplosApiTransactionDetail posted, decimal creditAccountNumber, decimal expenseAccountNumber, int fundId)
    {
        Assert.Equal(2, posted.Lines.Length);
        var credit = Assert.Single(posted.Lines, line => line.Account.AccountNumber == creditAccountNumber);
        var expense = Assert.Single(posted.Lines, line => line.Account.AccountNumber == expenseAccountNumber);
        Assert.Equal((-posted.Amount, fundId), (credit.Amount, credit.Fund.Id));
        Assert.Equal((posted.Amount, fundId), (expense.Amount, expense.Fund.Id));
    }
}
