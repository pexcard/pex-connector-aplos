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
            int paymentId, PaymentType type, decimal amount, bool rejectedByBank = false) => new()
        {
            PaymentId = paymentId,
            Type = type,
            Amount = amount,
            RejectedByBank = rejectedByBank,
            DatePaid = new DateTime(2026, 8, 1),
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
