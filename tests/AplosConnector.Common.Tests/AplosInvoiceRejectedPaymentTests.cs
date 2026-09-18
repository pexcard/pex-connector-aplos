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
using AplosConnector.Common.Storage;
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
    public class AplosInvoiceRejectedPaymentTests
    {
        private const decimal RegisterAccount = 2000m;
        private const decimal CheckingAccount = 1000m;
        private const decimal RebateIncomeAccount = 4000m;

        private const string MissionsFundId = "60";
        private const string GeneralFundId = "30";

        private readonly Mock<IAplosApiClient> _mockAplosApiClient = new();
        private readonly Mock<IAplosApiClientFactory> _mockAplosApiClientFactory = new();
        private readonly Mock<IAplosIntegrationMappingService> _mockAplosIntegrationMappingService = new();
        private readonly Mock<IPexApiClient> _mockPexApiClient = new();
        private readonly Mock<IOptions<AppSettingsModel>> _mockOptions = new();

        private AplosApiTransactionDetail _createdTransaction;

        [Fact]
        public void GetCollectedInvoicePayments_DropsThePaymentsTheBankRejected()
        {
            var payments = PaymentsWithTwoRejectedRepayments();

            var collectedPayments = AplosIntegrationService.GetCollectedInvoicePayments(payments);

            Assert.Equal(new[] { 1, 2 }, collectedPayments.Select(payment => payment.PaymentId));
            Assert.Equal(49.90m, collectedPayments.Sum(payment => payment.Amount));
        }

        [Fact]
        public void GetCollectedInvoicePayments_KeepsEveryPayment_WhenTheBankRejectedNone()
        {
            var payments = new[]
            {
                NewPayment(1, PaymentType.PEXTransfer, 49.40m),
                NewPayment(2, PaymentType.SameDayACH, 0.50m),
            };

            var collectedPayments = AplosIntegrationService.GetCollectedInvoicePayments(payments);

            Assert.Equal(payments, collectedPayments);
        }

        [Fact]
        public async Task RebateDistribute_PostsOnlyTheCollectedAmounts_WhenRepaymentsWereRejectedByBank()
        {
            var invoice = NewInvoice(49.90m);
            var allocations = TwoFundAllocations(29.90m, 20.00m);
            var collectedPayments = AplosIntegrationService.GetCollectedInvoicePayments(PaymentsWithTwoRejectedRepayments());

            var service = GetAplosIntegrationService();
            var result = await service.SyncInvoiceRebateDistribute(
                NewMapping(), invoice, allocations, collectedPayments, AplosFunds(), NullLogger.Instance, default);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(49.90m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 29.90m), (GeneralFundId, 20.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 29.90m), (GeneralFundId, 20.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task RebateDistribute_SkipsTheInvoice_WhenTheRejectedRepaymentsAreStillCounted()
        {
            var invoice = NewInvoice(49.90m);
            var allocations = TwoFundAllocations(29.90m, 20.00m);

            var service = GetAplosIntegrationService();
            var result = await service.SyncInvoiceRebateDistribute(
                NewMapping(), invoice, allocations, PaymentsWithTwoRejectedRepayments(), AplosFunds(), NullLogger.Instance, default);

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public async Task RebateDeposit_PostsOnlyTheCollectedAmounts_WhenRepaymentsWereRejectedByBank()
        {
            var invoice = NewInvoice(49.90m);
            var allocations = TwoFundAllocations(29.90m, 20.00m);
            var collectedPayments = AplosIntegrationService.GetCollectedInvoicePayments(PaymentsWithTwoRejectedRepayments());

            var service = GetAplosIntegrationService();
            var result = await service.SyncInvoiceRebateDeposit(
                NewMapping(), invoice, allocations, collectedPayments, AplosFunds(), NullLogger.Instance, default);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(49.90m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 29.90m), (GeneralFundId, 20.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 29.90m), (GeneralFundId, 20.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task Simple_PostsOnlyTheCollectedAmounts_WhenRepaymentsWereRejectedByBank()
        {
            var invoice = NewInvoice(49.90m);
            var allocations = TwoFundAllocations(29.90m, 20.00m);
            var collectedPayments = AplosIntegrationService.GetCollectedInvoicePayments(PaymentsWithTwoRejectedRepayments());

            var service = GetAplosIntegrationService();
            var result = await service.SyncInvoiceSimple(
                NewMapping(), invoice, allocations, collectedPayments, AplosFunds(), NullLogger.Instance, default);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(49.90m, _createdTransaction.Amount);
        }

        [Fact]
        public async Task Simple_SkipsTheInvoice_WhenTheRejectedRepaymentsAreStillCounted()
        {
            var invoice = NewInvoice(49.90m);
            var allocations = TwoFundAllocations(29.90m, 20.00m);

            var service = GetAplosIntegrationService();
            var result = await service.SyncInvoiceSimple(
                NewMapping(), invoice, allocations, PaymentsWithTwoRejectedRepayments(), AplosFunds(), NullLogger.Instance, default);

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public void Eligibility_IsUnderpaidAfterBankRejection_WhenTheOnlyPaymentWasRejected()
        {
            var payments = new[] { NewPayment(1, PaymentType.PEXTransfer, 100.00m, rejectedByBank: true) };
            var collected = AplosIntegrationService.GetCollectedInvoicePayments(payments);

            var eligibility = AplosIntegrationService.GetInvoiceSyncEligibility(
                NewInvoice(100.00m), collected, payments.Length - collected.Count, new DateTime(2026, 9, 1), 3, out _);

            Assert.Equal(InvoiceSyncEligibility.UnderpaidAfterBankRejection, eligibility);
        }

        [Fact]
        public void Eligibility_IsUnderpaid_WhenShortWithNoRejectedPayment()
        {
            var payments = new[] { NewPayment(1, PaymentType.PEXTransfer, 60.00m) };

            var eligibility = AplosIntegrationService.GetInvoiceSyncEligibility(
                NewInvoice(100.00m), payments, 0, new DateTime(2026, 9, 1), 3, out _);

            Assert.Equal(InvoiceSyncEligibility.Underpaid, eligibility);
        }

        [Fact]
        public void Eligibility_IsEligible_WhenRejectedPaymentsAreExcludedAndTheRestCoversTheInvoice()
        {
            var payments = PaymentsWithTwoRejectedRepayments();
            var collected = AplosIntegrationService.GetCollectedInvoicePayments(payments);

            var eligibility = AplosIntegrationService.GetInvoiceSyncEligibility(
                NewInvoice(49.90m), collected, payments.Length - collected.Count, new DateTime(2026, 9, 1), 3, out _);

            Assert.Equal(InvoiceSyncEligibility.Eligible, eligibility);
        }

        [Theory]
        [InlineData("2026-08-03", "2026-08-05", false)] // Mon paid, Wed = 2 business days
        [InlineData("2026-08-03", "2026-08-06", true)]  // Mon paid, Thu = 3 business days
        [InlineData("2026-08-06", "2026-08-10", false)] // Thu paid, Mon = 2 business days (weekend skipped)
        [InlineData("2026-08-06", "2026-08-11", true)]  // Thu paid, Tue = 3 business days
        [InlineData("2026-08-08", "2026-08-12", true)]  // Sat paid, Wed = 3 business days
        public void Eligibility_HonoursTheSettleGuard(string datePaid, string today, bool expectedEligible)
        {
            var payments = new[] { NewPayment(1, PaymentType.PEXTransfer, 100.00m, datePaid: DateTime.Parse(datePaid)) };

            var eligibility = AplosIntegrationService.GetInvoiceSyncEligibility(
                NewInvoice(100.00m), payments, 0, DateTime.Parse(today).AddHours(15), 3, out _);

            Assert.Equal(expectedEligible ? InvoiceSyncEligibility.Eligible : InvoiceSyncEligibility.NotSettled, eligibility);
        }

        [Fact]
        public void Eligibility_SettleGuardUsesTheLatestCollectedPayment_NotTheRejectedOne()
        {
            var payments = new[]
            {
                NewPayment(1, PaymentType.PEXTransfer, 100.00m, datePaid: new DateTime(2026, 8, 3)),
                NewPayment(2, PaymentType.PEXTransfer, 50.00m, rejectedByBank: true, datePaid: new DateTime(2026, 8, 10)),
            };
            var collected = AplosIntegrationService.GetCollectedInvoicePayments(payments);

            var eligibility = AplosIntegrationService.GetInvoiceSyncEligibility(
                NewInvoice(100.00m), collected, 1, new DateTime(2026, 8, 6), 3, out var settledOn);

            Assert.Equal(InvoiceSyncEligibility.Eligible, eligibility);
            Assert.Equal(new DateTime(2026, 8, 6), settledOn);
        }

        [Fact]
        public void Eligibility_SettleGuardOfZeroDays_IsEligibleTheSameDay()
        {
            var payments = new[] { NewPayment(1, PaymentType.PEXTransfer, 100.00m, datePaid: new DateTime(2026, 8, 3)) };

            var eligibility = AplosIntegrationService.GetInvoiceSyncEligibility(
                NewInvoice(100.00m), payments, 0, new DateTime(2026, 8, 3), 0, out _);

            Assert.Equal(InvoiceSyncEligibility.Eligible, eligibility);
        }

        [Fact]
        public async Task SyncInvoices_PostsOnlyTheCollectedAmount_WhenTwoRepaymentsWereRejectedByBank()
        {
            var invoice = NewInvoice(49.90m);
            var service = GetAplosIntegrationServiceForSyncInvoices(
                invoice, PaymentsWithTwoRejectedRepayments(), TwoFundAllocations(29.90m, 20.00m), out var syncResults);

            await service.SyncInvoices(NullLogger.Instance, NewMapping(), [], new DateTime(2026, 8, 1), default);

            Assert.NotNull(_createdTransaction);
            Assert.Equal(49.90m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 29.90m), (GeneralFundId, 20.00m) });
            var syncResult = Assert.Single(syncResults);
            Assert.Equal(SyncStatus.Success.ToString(), syncResult.SyncStatus);
            Assert.Equal(1, syncResult.SyncedRecords);
        }

        [Fact]
        public async Task SyncInvoices_SoftSkipsTheInvoice_WhenItsOnlyPaymentWasRejectedByBank()
        {
            var invoice = NewInvoice(100.00m);
            var payments = new[] { NewPayment(1, PaymentType.PEXTransfer, 100.00m, rejectedByBank: true) };
            var service = GetAplosIntegrationServiceForSyncInvoices(
                invoice, payments, TwoFundAllocations(60.00m, 40.00m), out var syncResults);

            await service.SyncInvoices(NullLogger.Instance, NewMapping(), [], new DateTime(2026, 8, 1), default);

            Assert.Null(_createdTransaction);
            var syncResult = Assert.Single(syncResults);
            Assert.Equal(SyncStatus.Success.ToString(), syncResult.SyncStatus);
            Assert.Equal(0, syncResult.SyncedRecords);
            Assert.Equal(string.Empty, syncResult.SyncNotes);
        }

        [Fact]
        public async Task SyncInvoices_FailsTheInvoice_WhenItIsUnderpaidWithoutAnyRejectedPayment()
        {
            var invoice = NewInvoice(100.00m);
            var payments = new[] { NewPayment(1, PaymentType.PEXTransfer, 60.00m) };
            var service = GetAplosIntegrationServiceForSyncInvoices(
                invoice, payments, TwoFundAllocations(60.00m, 40.00m), out var syncResults);

            await service.SyncInvoices(NullLogger.Instance, NewMapping(), [], new DateTime(2026, 8, 1), default);

            Assert.Null(_createdTransaction);
            var syncResult = Assert.Single(syncResults);
            Assert.Equal(SyncStatus.Failed.ToString(), syncResult.SyncStatus);
            Assert.Equal(0, syncResult.SyncedRecords);
        }

        private AplosIntegrationService GetAplosIntegrationServiceForSyncInvoices(
            InvoiceModel invoice,
            InvoicePaymentModel[] payments,
            InvoiceAllocationModel[] allocations,
            out List<SyncResultModel> syncResults)
        {
            _mockPexApiClient
                .Setup(client => client.GetInvoices(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<InvoiceModel> { invoice });
            _mockPexApiClient
                .Setup(client => client.GetInvoicePayments(It.IsAny<string>(), invoice.InvoiceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(payments.ToList());
            _mockPexApiClient
                .Setup(client => client.GetInvoiceAllocations(It.IsAny<string>(), invoice.InvoiceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(allocations.ToList());

            var aplosFunds = new List<AplosApiFundDetail>();
            _mockAplosApiClient
                .Setup(client => client.GetFunds(It.IsAny<CancellationToken>()))
                .ReturnsAsync(aplosFunds);
            _mockAplosIntegrationMappingService
                .Setup(mappingService => mappingService.Map(aplosFunds))
                .Returns(AplosFunds());

            var results = new List<SyncResultModel>();
            var mockHistoryStorage = new Mock<SyncHistoryStorage>(null);
            mockHistoryStorage
                .Setup(storage => storage.CreateAsync(It.IsAny<SyncResultModel>(), It.IsAny<CancellationToken>()))
                .Callback<SyncResultModel, CancellationToken>((result, _) => results.Add(result))
                .Returns(Task.CompletedTask);
            syncResults = results;

            return GetAplosIntegrationService(mockHistoryStorage.Object);
        }

        private void AssertRegisterDebits((string fundId, decimal amount)[] expected) =>
            AssertLines(RegisterAccount, expected.Select(e => (e.fundId, e.amount)).ToArray());

        private void AssertBankCredits((string fundId, decimal amount)[] expected) =>
            AssertLines(CheckingAccount, expected.Select(e => (e.fundId, -e.amount)).ToArray());

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

        private static InvoicePaymentModel[] PaymentsWithTwoRejectedRepayments() =>
        [
            NewPayment(1, PaymentType.PEXTransfer, 49.40m),
            NewPayment(2, PaymentType.SameDayACH, 0.50m),
            NewPayment(3, PaymentType.PEXTransfer, 79.20m, rejectedByBank: true),
            NewPayment(4, PaymentType.PEXTransfer, 79.20m, rejectedByBank: true),
        ];

        private static InvoiceModel NewInvoice(decimal invoiceAmount) => new()
        {
            InvoiceId = 94460,
            InvoiceAmount = invoiceAmount,
            Status = InvoiceStatus.Closed,
            DueDate = new DateTime(2026, 8, 1),
        };

        private static InvoicePaymentModel NewPayment(
            int paymentId, PaymentType type, decimal amount, bool rejectedByBank = false, DateTime? datePaid = null) => new()
        {
            PaymentId = paymentId,
            Type = type,
            Amount = amount,
            RejectedByBank = rejectedByBank,
            DatePaid = datePaid ?? new DateTime(2026, 8, 1),
        };

        private static InvoiceAllocationModel NewAllocation(string aplosFundId, decimal totalAmount) => new()
        {
            InvoiceId = 94460,
            TagValue = aplosFundId,
            TotalAmount = totalAmount,
        };

        private static InvoiceAllocationModel[] TwoFundAllocations(decimal missionsAmount, decimal generalAmount) =>
        [
            NewAllocation(MissionsFundId, missionsAmount),
            NewAllocation(GeneralFundId, generalAmount),
        ];

        private static List<PexAplosApiObject> AplosFunds() =>
        [
            new PexAplosApiObject { Id = MissionsFundId, Name = "Missions" },
            new PexAplosApiObject { Id = GeneralFundId, Name = "General" },
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

        private AplosIntegrationService GetAplosIntegrationService(SyncHistoryStorage historyStorage = null)
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
                historyStorage,
                null,
                new SyncSettingsModel(),
                null);
        }
    }
}
