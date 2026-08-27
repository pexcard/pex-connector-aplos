using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services;
using AplosConnector.Common.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests
{
    public class AplosInvoiceOverpaymentTests
    {
        private const decimal RegisterAccount = 2000m;
        private const decimal CheckingAccount = 1000m;
        private const decimal RebateIncomeAccount = 4000m;

        private const string MissionsFundId = "60";
        private const string GeneralFundId = "30";
        private const string YouthFundId = "20";

        private readonly Mock<IAplosApiClient> _mockAplosApiClient = new();
        private readonly Mock<IAplosApiClientFactory> _mockAplosApiClientFactory = new();
        private readonly Mock<IAplosIntegrationMappingService> _mockAplosIntegrationMappingService = new();
        private readonly Mock<IPexApiClient> _mockPexApiClient = new();
        private readonly Mock<IOptions<AppSettingsModel>> _mockOptions = new();

        private AplosApiTransactionDetail _createdTransaction;

        [Fact]
        public async Task RebateDistribute_WritesNoRebateIncomeLine_WhenBankTransferCoveredTheWholeInvoice()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 110.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(110.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task RebateDistribute_WritesNoRebateIncomeLine_WhenSingleFundInvoiceIsOverpaid()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 110.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 110.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(110.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 110.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 110.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task RebateDistribute_SyncsWithoutRebateSettings_WhenTheRebateWasNotNeeded()
        {
            var mapping = NewMapping();
            mapping.PexRebatesAplosTransactionAccountNumber = decimal.Zero;
            mapping.PexRebatesAplosTaxTagId = null;

            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 110.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var result = await SyncRebateDistribute(mapping, invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(110.00m, _createdTransaction.Amount);
            AssertBankCredits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
        }

        [Fact]
        public async Task RebateDistribute_RecordsOnlyTheRebateTheInvoiceNeeded_WhenAFeePostedAfterThePayment()
        {
            var invoice = NewInvoice(3002.39m);
            var allocations = ThreeFundAllocations(1800.00m, 900.00m, 302.39m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 3002.00m),
                NewPayment(PaymentType.RebateCredit, 59.78m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(3002.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 1800.00m), (GeneralFundId, 900.00m), (YouthFundId, 302.39m) });
            AssertBankCredits(new[] { (MissionsFundId, 1799.77m), (GeneralFundId, 899.88m), (YouthFundId, 302.35m) });
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 0.23m), (GeneralFundId, 0.12m), (YouthFundId, 0.04m) });
        }

        [Fact]
        public async Task RebateDistribute_Fails_WhenRebateIsNeededAndRebateSettingsAreMissing()
        {
            var mapping = NewMapping();
            mapping.PexRebatesAplosTransactionAccountNumber = decimal.Zero;

            var invoice = NewInvoice(3002.39m);
            var allocations = ThreeFundAllocations(1800.00m, 900.00m, 302.39m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 3002.00m),
                NewPayment(PaymentType.RebateCredit, 59.78m),
            };

            var result = await SyncRebateDistribute(mapping, invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public async Task RebateDistribute_WritesNoBankLine_WhenTheRebateCreditExceedsTheWholeInvoice()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.RebateCredit, 150.00m) };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(0m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            Assert.Empty(LinesFor(CheckingAccount));
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
        }

        [Fact]
        public async Task RebateDistribute_KeepsExistingBehaviour_WhenACarryOverCreditExactlyPaysPartOfTheInvoice()
        {
            var invoice = NewInvoice(500.00m);
            var allocations = ThreeFundAllocations(100.00m, 100.00m, 300.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.CarryOverCredit, 10.00m),
                NewPayment(PaymentType.PEXTransfer, 490.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(490.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 100.00m), (GeneralFundId, 100.00m), (YouthFundId, 300.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 98.00m), (GeneralFundId, 98.00m), (YouthFundId, 294.00m) });
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 2.00m), (GeneralFundId, 2.00m), (YouthFundId, 6.00m) });
        }

        [Fact]
        public async Task RebateDistribute_KeepsExistingBehaviour_WhenTheInvoiceIsExactlyPaidByCash()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.PEXTransfer, 110.00m) };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(110.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task RebateDistribute_Fails_WhenTheInvoiceIsUnderpaid()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.PEXTransfer, 100.00m) };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public async Task RebateDistribute_Fails_WhenTheSurplusIsCashRatherThanCredits()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.PEXTransfer, 120.00m) };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public async Task RebateDistribute_Fails_WhenCashExceedsTheInvoiceAmountAlongsideARebateCredit()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 115.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Theory]
        [InlineData(110.00, 110.00, true)]
        [InlineData(110.00, 100.00, true)]
        [InlineData(110.00, 0.00, true)]
        [InlineData(110.00, 110.01, false)]
        [InlineData(110.00, 120.00, false)]
        public void IsInvoiceSurplusBackedByCredits_RejectsCashAboveTheInvoiceAmount(
            decimal invoiceAmount, decimal cashPaymentsAmount, bool expected)
        {
            Assert.Equal(expected, AplosIntegrationService.IsInvoiceSurplusBackedByCredits(invoiceAmount, cashPaymentsAmount));
        }

        [Fact]
        public void DistributeInvoiceRebateIncome_DoesNotDivideByZero_WhenAllocationsAreEmptyOfValue()
        {
            var allocations = new[] { (1, 0m), (2, 0m) };

            var splits = AplosIntegrationService.DistributeInvoiceRebateIncome(allocations, 0m, 10.00m);

            Assert.All(splits, split => Assert.Equal(0m, split.RebateIncomeAmount));
            Assert.All(splits, split => Assert.Equal(0m, split.BankAmount));
        }

        [Fact]
        public async Task RebateDistribute_Fails_WhenAllocationsDoNotAddUpToTheInvoiceAmount()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = new[]
            {
                NewAllocation(MissionsFundId, 60.00m),
                NewAllocation("999", 50.00m),
            };
            var payments = new[] { NewPayment(PaymentType.PEXTransfer, 110.00m) };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public async Task Simple_StillSkipsOverpaidInvoices()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 110.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var service = GetAplosIntegrationService();
#pragma warning disable CS0618
            var result = await service.SyncInvoiceSimple(
                NewMapping(), invoice, allocations, payments, AplosFunds(), NullLogger.Instance, default);
#pragma warning restore CS0618

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public async Task RebateDeposit_StillSkipsOverpaidInvoices()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 110.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var service = GetAplosIntegrationService();
            var result = await service.SyncInvoiceRebateDeposit(
                NewMapping(), invoice, allocations, payments, AplosFunds(), NullLogger.Instance, default);

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public void OverpaidInvoiceAlreadyInAplos_IsNotSyncedASecondTime()
        {
            var service = GetAplosIntegrationService();
            var aplosTransactions = new[] { new AplosApiTransactionDetail { Note = "94460" } };

            Assert.True(service.WasPexTransactionSyncedToAplos(aplosTransactions, "94460"));
            Assert.False(service.WasPexTransactionSyncedToAplos(aplosTransactions, "94461"));
        }

        [Theory]
        [InlineData(110.00, 110.00, true)]
        [InlineData(110.00, 120.00, true)]
        [InlineData(110.00, 109.99, false)]
        [InlineData(110.00, 0.00, false)]
        public void IsInvoiceFullyPaid_AcceptsPaymentsAtOrAboveTheInvoiceAmount(
            decimal invoiceAmount, decimal totalPaymentsAmount, bool expected)
        {
            Assert.Equal(expected, AplosIntegrationService.IsInvoiceFullyPaid(invoiceAmount, totalPaymentsAmount));
        }

        [Theory]
        [InlineData(110.00, 110.00, 120.00, 110.00, 0.00, 10.00)]
        [InlineData(3002.39, 3002.00, 3061.78, 3002.00, 0.39, 59.39)]
        [InlineData(110.00, 0.00, 150.00, 0.00, 110.00, 40.00)]
        [InlineData(500.00, 490.00, 500.00, 490.00, 10.00, 0.00)]
        public void SplitInvoicePaymentTotals_TakesOnlyWhatTheInvoiceNeeded(
            decimal invoiceAmount,
            decimal cashPaymentsAmount,
            decimal totalPaymentsAmount,
            decimal expectedBankAmount,
            decimal expectedRebateIncomeAmount,
            decimal expectedSurplusAmount)
        {
            var (bankAmount, rebateIncomeAmount, surplusAmount) =
                AplosIntegrationService.SplitInvoicePaymentTotals(invoiceAmount, cashPaymentsAmount, totalPaymentsAmount);

            Assert.Equal(expectedBankAmount, bankAmount);
            Assert.Equal(expectedRebateIncomeAmount, rebateIncomeAmount);
            Assert.Equal(expectedSurplusAmount, surplusAmount);
        }

        [Fact]
        public void DistributeInvoiceRebateIncome_PutsTheRoundingRemainderOnTheLastFund()
        {
            var allocations = new[] { (1, 1800.00m), (2, 900.00m), (3, 302.39m) };

            var splits = AplosIntegrationService.DistributeInvoiceRebateIncome(allocations, 3002.39m, 0.39m);

            Assert.Equal(new[] { 0.23m, 0.12m, 0.04m }, splits.Select(s => s.RebateIncomeAmount));
            Assert.Equal(new[] { 1799.77m, 899.88m, 302.35m }, splits.Select(s => s.BankAmount));
            Assert.Equal(0.39m, splits.Sum(s => s.RebateIncomeAmount));
        }

        [Fact]
        public void DistributeInvoiceRebateIncome_LeavesEveryFundOnCash_WhenNoRebateWasNeeded()
        {
            var allocations = new[] { (1, 60.00m), (2, 30.00m), (3, 20.00m) };

            var splits = AplosIntegrationService.DistributeInvoiceRebateIncome(allocations, 110.00m, 0m);

            Assert.All(splits, split => Assert.Equal(0m, split.RebateIncomeAmount));
            Assert.Equal(new[] { 60.00m, 30.00m, 20.00m }, splits.Select(s => s.BankAmount));
        }

        private async Task<TransactionSyncResult> SyncRebateDistribute(
            Pex2AplosMappingModel mapping,
            InvoiceModel invoice,
            IReadOnlyList<InvoiceAllocationModel> allocations,
            IReadOnlyList<InvoicePaymentModel> payments)
        {
            var service = GetAplosIntegrationService();

            return await service.SyncInvoiceRebateDistribute(
                mapping, invoice, allocations, payments, AplosFunds(), NullLogger.Instance, default);
        }

        private void AssertRegisterDebits((string fundId, decimal amount)[] expected) =>
            AssertLines(RegisterAccount, expected.Select(e => (e.fundId, e.amount)).ToArray());

        private void AssertBankCredits((string fundId, decimal amount)[] expected) =>
            AssertLines(CheckingAccount, expected.Select(e => (e.fundId, -e.amount)).ToArray());

        private void AssertRebateIncomeCredits((string fundId, decimal amount)[] expected) =>
            AssertLines(RebateIncomeAccount, expected.Select(e => (e.fundId, -e.amount)).ToArray());

        private void AssertLines(decimal accountNumber, (string fundId, decimal amount)[] expected)
        {
            var actual = LinesFor(accountNumber)
                .Select(line => (fundId: line.Fund.Id.ToString(), amount: line.Amount))
                .ToArray();

            Assert.Equal(expected, actual);
        }

        private AplosApiTransactionLineDetail[] LinesFor(decimal accountNumber)
        {
            Assert.NotNull(_createdTransaction);

            return _createdTransaction.Lines
                .Where(line => line.Account.AccountNumber == accountNumber)
                .ToArray();
        }

        private static InvoiceModel NewInvoice(decimal invoiceAmount) => new()
        {
            InvoiceId = 94460,
            InvoiceAmount = invoiceAmount,
            Status = InvoiceStatus.Closed,
            DueDate = new DateTime(2026, 8, 1),
        };

        private static InvoicePaymentModel NewPayment(PaymentType type, decimal amount) => new()
        {
            Type = type,
            Amount = amount,
            DatePaid = new DateTime(2026, 8, 1),
        };

        private static InvoiceAllocationModel NewAllocation(string aplosFundId, decimal totalAmount) => new()
        {
            InvoiceId = 94460,
            TagValue = aplosFundId,
            TotalAmount = totalAmount,
        };

        private static InvoiceAllocationModel[] ThreeFundAllocations(
            decimal missionsAmount, decimal generalAmount, decimal youthAmount) =>
        [
            NewAllocation(MissionsFundId, missionsAmount),
            NewAllocation(GeneralFundId, generalAmount),
            NewAllocation(YouthFundId, youthAmount),
        ];

        private static List<PexAplosApiObject> AplosFunds() =>
        [
            new PexAplosApiObject { Id = MissionsFundId, Name = "Missions" },
            new PexAplosApiObject { Id = GeneralFundId, Name = "General" },
            new PexAplosApiObject { Id = YouthFundId, Name = "Youth" },
        ];

        private static Pex2AplosMappingModel NewMapping() => new()
        {
            PEXBusinessAcctId = 6118231,
            AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication,
            AplosAccountId = "accountId",
            AplosClientId = "clientId",
            AplosPrivateKey = "privateKey",
            SyncInvoices = true,
            SyncInvoicesMethod = "rebate-distribute",
            SyncInvoiceAggregated = false,
            AplosRegisterAccountNumber = RegisterAccount,
            TransfersAplosTransactionAccountNumber = CheckingAccount,
            TransfersAplosContactId = 777,
            PexRebatesAplosTransactionAccountNumber = RebateIncomeAccount,
            PexRebatesAplosFundId = 60,
            PexRebatesAplosTaxTagId = "tax-1",
        };

        private AplosIntegrationService GetAplosIntegrationService()
        {
            _mockOptions.Setup(options => options.Value).Returns(new AppSettingsModel());

            _mockAplosApiClient
                .Setup(client => client.CreateTransaction(It.IsAny<AplosApiTransactionDetail>(), It.IsAny<CancellationToken>()))
                .Callback<AplosApiTransactionDetail, CancellationToken>((transaction, _) => _createdTransaction = transaction)
                .Returns(Task.FromResult(new AplosApiTransactionResponse()));

            _mockAplosApiClientFactory
                .Setup(factory => factory.CreateClient(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Uri>(),
                    It.IsAny<Func<ILogger, AplosAuthModel>>(),
                    It.IsAny<Func<AplosAuthModel, ILogger, CancellationToken, Task>>()))
                .Returns(_mockAplosApiClient.Object);

            return new AplosIntegrationService(
                new NullLogger<AplosIntegrationService>(),
                _mockOptions.Object,
                _mockAplosApiClientFactory.Object,
                _mockAplosIntegrationMappingService.Object,
                _mockPexApiClient.Object,
                null,
                null,
                new SyncSettingsModel(),
                null);
        }
    }
}
