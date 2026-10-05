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
using Azure.Data.Tables;
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
    public class AplosInvoiceReturnWindowTests
    {
        private const string MissionsFundId = "101";

        private readonly Mock<IAplosApiClient> _mockAplosApiClient = new();
        private readonly Mock<IAplosApiClientFactory> _mockAplosApiClientFactory = new();
        private readonly Mock<IAplosIntegrationMappingService> _mockAplosIntegrationMappingService = new();
        private readonly Mock<IPexApiClient> _mockPexApiClient = new();
        private readonly Mock<IOptions<AppSettingsModel>> _mockOptions = new();
        private readonly Mock<SyncHistoryStorage> _mockHistoryStorage = new(MockBehavior.Loose, (TableClient)null);

        private readonly List<SyncResultModel> _historyRows = [];
        private readonly List<AplosApiTransactionDetail> _createdTransactions = [];

        [Fact]
        public async Task SyncInvoices_ExcludesBankRejectedPayments()
        {
            SetupInvoice(NewInvoice(98761, 49.90m, isPastReturnWindow: true));
            SetupPayments(NewPayment(49.90m), NewPayment(79.20m, rejectedByBank: true), NewPayment(79.20m, rejectedByBank: true));
            SetupAllocations(new InvoiceAllocationModel { InvoiceId = 98761, TagValue = MissionsFundId, TotalAmount = 49.90m });

            await GetAplosIntegrationService().SyncInvoices(NullLogger.Instance, NewMapping(), [], new DateTime(2026, 7, 1), default);

            var transaction = Assert.Single(_createdTransactions);
            Assert.Equal(49.90m, transaction.Amount);
            Assert.DoesNotContain(transaction.Lines, line => Math.Abs(line.Amount) == 79.20m);
            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
            Assert.Equal(1, row.SyncedRecords);
        }

        [Fact]
        public async Task SyncInvoices_SkipsWithoutFailing_WhenTheShortfallIsFromRejectedPayments()
        {
            SetupInvoice(NewInvoice(98762, 79.20m, isPastReturnWindow: true));
            SetupPayments(NewPayment(79.20m, rejectedByBank: true));
            var logger = new ListLogger();

            await GetAplosIntegrationService().SyncInvoices(logger, NewMapping(), [], new DateTime(2026, 7, 1), default);

            Assert.Empty(_createdTransactions);
            Assert.Contains(logger.Messages, m => m.Level == LogLevel.Information && m.Text.Contains("the shortfall is re-billed on a later invoice. Skipping invoice 98762"));
            Assert.DoesNotContain(logger.Messages, m => m.Level >= LogLevel.Warning);
            _mockPexApiClient.Verify(client => client.GetInvoiceAllocations(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
            Assert.Equal(0, row.SyncedRecords);
        }

        [Fact]
        public async Task SyncInvoices_WarnsWithoutFailing_WhenRejectedPaymentsDoNotExplainTheShortfall()
        {
            SetupInvoice(NewInvoice(98764, 100.00m, isPastReturnWindow: true));
            SetupPayments(NewPayment(20.00m), NewPayment(5.00m, rejectedByBank: true));
            var logger = new ListLogger();

            await GetAplosIntegrationService().SyncInvoices(logger, NewMapping(), [], new DateTime(2026, 7, 1), default);

            Assert.Empty(_createdTransactions);
            Assert.Contains(logger.Messages, m => m.Level == LogLevel.Warning && m.Text.Contains("Invoice 98764 is not fully paid") && m.Text.Contains("shortfall (80.00)"));
            _mockPexApiClient.Verify(client => client.GetInvoiceAllocations(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
            Assert.Equal(0, row.SyncedRecords);
            Assert.Equal(string.Empty, row.SyncNotes);
        }

        [Fact]
        public async Task SyncInvoices_Warns_WhenARejectedReversalHidesTheShortfall()
        {
            SetupInvoice(NewInvoice(98766, 49.90m, isPastReturnWindow: true));
            SetupPayments(NewPayment(49.90m, type: PaymentType.SameDayACH), NewPayment(79.20m, type: PaymentType.Reversal), NewPayment(79.20m, rejectedByBank: true, type: PaymentType.Reversal));
            var logger = new ListLogger();

            await GetAplosIntegrationService().SyncInvoices(logger, NewMapping(), [], new DateTime(2026, 7, 1), default);

            Assert.Empty(_createdTransactions);
            Assert.Contains(logger.Messages, m => m.Level == LogLevel.Warning && m.Text.Contains("Invoice 98766 is not fully paid"));
            Assert.DoesNotContain(logger.Messages, m => m.Text.Contains("re-billed on a later invoice"));
        }

        [Fact]
        public async Task SyncInvoices_WarnsWithoutFailing_WhenTheInvoiceIsUnderpaid()
        {
            SetupInvoice(NewInvoice(98769, 100.00m, isPastReturnWindow: true));
            SetupPayments(NewPayment(60.00m));
            var logger = new ListLogger();

            await GetAplosIntegrationService().SyncInvoices(logger, NewMapping(), [], new DateTime(2026, 7, 1), default);

            Assert.Empty(_createdTransactions);
            Assert.Contains(logger.Messages, m => m.Level == LogLevel.Warning && m.Text.Contains("Invoice 98769 is not fully paid") && m.Text.Contains("shortfall (40.00)"));
            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
            Assert.Equal(string.Empty, row.SyncNotes);
        }

        [Fact]
        public async Task SyncInvoices_SkipsInvoicesInsideTheReturnWindow()
        {
            SetupInvoice(NewInvoice(98763, 49.90m, isPastReturnWindow: false));
            var logger = new ListLogger();

            await GetAplosIntegrationService().SyncInvoices(logger, NewMapping(), [], new DateTime(2026, 7, 1), default);

            Assert.Empty(_createdTransactions);
            Assert.Contains(logger.Messages, m => m.Level == LogLevel.Information && m.Text.Contains("Skipping invoice 98763: still inside the ACH return window"));
            _mockPexApiClient.Verify(client => client.GetInvoicePayments(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
        }

        [Fact]
        public async Task SyncInvoices_LogsWhyAnInvoiceIsSkipped()
        {
            _mockPexApiClient
                .Setup(client => client.GetInvoices(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([NewInvoice(1, 10m, isPastReturnWindow: true, status: InvoiceStatus.Open), NewInvoice(2, 0m, isPastReturnWindow: true), NewInvoice(3, 10m, isPastReturnWindow: true)]);
            var logger = new ListLogger();

            await GetAplosIntegrationService().SyncInvoices(logger, NewMapping(), [new AplosApiTransactionDetail { Note = "3" }], new DateTime(2026, 7, 1), default);

            Assert.Contains(logger.Messages, m => m.Text.Contains("Skipping invoice 1: status is Open, not Closed"));
            Assert.Contains(logger.Messages, m => m.Text.Contains("Skipping invoice 2: amount is 0"));
            Assert.Contains(logger.Messages, m => m.Text.Contains("Skipping invoice 3: already synced to Aplos"));
            Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(_historyRows).SyncStatus);
        }

        [Fact]
        public async Task SyncInvoices_SubtractsReversalsFromCash()
        {
            SetupInvoice(NewInvoice(98765, 100.00m, isPastReturnWindow: true));
            SetupPayments(NewPayment(100.00m), NewPayment(40.00m, type: PaymentType.Reversal), NewPayment(40.00m));
            SetupAllocations(new InvoiceAllocationModel { InvoiceId = 98765, TagValue = MissionsFundId, TotalAmount = 100.00m });

            await GetAplosIntegrationService().SyncInvoices(NullLogger.Instance, NewMapping(), [], new DateTime(2026, 7, 1), default);

            var transaction = Assert.Single(_createdTransactions);
            Assert.Equal(100.00m, transaction.Amount);
            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
            Assert.Equal(1, row.SyncedRecords);
        }

        [Fact]
        public async Task SyncInvoices_Fails_WhenAPaymentTypeIsUnknown()
        {
            SetupInvoice(NewInvoice(98766, 100.00m, isPastReturnWindow: true));
            SetupPayments(NewPayment(100.00m, type: (PaymentType)99));
            var logger = new ListLogger();

            await GetAplosIntegrationService().SyncInvoices(logger, NewMapping(), [], new DateTime(2026, 7, 1), default);

            Assert.Empty(_createdTransactions);
            Assert.Contains(logger.Messages, m => m.Level == LogLevel.Warning && m.Text.Contains("unknown payment type 99"));
            _mockPexApiClient.Verify(client => client.GetInvoiceAllocations(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal(SyncStatus.Failed.ToString(), Assert.Single(_historyRows).SyncStatus);
        }

        [Fact]
        public async Task SyncInvoices_Fails_AndAsksForTheRebateAccount_WhenCreditsNeedOne()
        {
            SetupInvoice(NewInvoice(98767, 110.00m, isPastReturnWindow: true));
            SetupPayments(NewPayment(100.00m), NewPayment(10.00m, type: PaymentType.RebateCredit));
            SetupAllocations(new InvoiceAllocationModel { InvoiceId = 98767, TagValue = MissionsFundId, TotalAmount = 110.00m });
            var logger = new ListLogger();

            await GetAplosIntegrationService().SyncInvoices(logger, NewMapping(), [], new DateTime(2026, 7, 1), default);

            Assert.Empty(_createdTransactions);
            Assert.Contains(logger.Messages, m => m.Level == LogLevel.Warning && m.Text.Contains("Set the rebate account in Aplos connector settings"));
            Assert.Equal(SyncStatus.Failed.ToString(), Assert.Single(_historyRows).SyncStatus);
        }

        [Fact]
        public async Task SyncRebates_Skips_ForCreditBusinessesWithInvoiceSyncOn()
        {
            var logger = new ListLogger();

            await GetAplosIntegrationService().SyncRebates(logger, NewRebateMapping(FundingSource.Credit, syncInvoices: true), RebateCreditTransactions(), [], default);

            Assert.Empty(_createdTransactions);
            Assert.Empty(_historyRows);
            Assert.Contains(logger.Messages, m => m.Level == LogLevel.Information && m.Text.Contains("Skipping rebates sync for credit business"));
        }

        [Theory]
        [InlineData(FundingSource.Credit, false)]
        [InlineData(FundingSource.Prepaid, false)]
        [InlineData(FundingSource.Prepaid, true)]
        public async Task SyncRebates_PostsRebates_UnlessACreditBusinessSyncsInvoices(FundingSource fundingSource, bool syncInvoices)
        {
            await GetAplosIntegrationService().SyncRebates(NullLogger.Instance, NewRebateMapping(fundingSource, syncInvoices), RebateCreditTransactions(), [], default);

            var transaction = Assert.Single(_createdTransactions);
            Assert.Contains(transaction.Lines, line => line.Account.AccountNumber == 4000 && Math.Abs(line.Amount) == 25.00m);
            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
            Assert.Equal(1, row.SyncedRecords);
        }

        [Fact]
        public async Task SyncInvoices_PostsOnlyTheCredit_WhenNetCashIsNegative()
        {
            SetupInvoice(NewInvoice(98768, 100.00m, isPastReturnWindow: true));
            SetupPayments(NewPayment(10.00m, type: PaymentType.Reversal), NewPayment(110.00m, type: PaymentType.RebateCredit));
            SetupAllocations(new InvoiceAllocationModel { InvoiceId = 98768, TagValue = MissionsFundId, TotalAmount = 100.00m });
            var mapping = NewMapping();
            mapping.PexRebatesAplosTransactionAccountNumber = 4000;
            var logger = new ListLogger();

            await GetAplosIntegrationService().SyncInvoices(logger, mapping, [], new DateTime(2026, 7, 1), default);

            var transaction = Assert.Single(_createdTransactions);
            Assert.Equal(0m, transaction.Amount);
            Assert.DoesNotContain(transaction.Lines, line => line.Account.AccountNumber == 1000);
            Assert.Equal(new[] { -100.00m }, transaction.Lines.Where(line => line.Account.AccountNumber == 4000).Select(line => line.Amount));
            Assert.Equal(new[] { 100.00m }, transaction.Lines.Where(line => line.Account.AccountNumber == 2000).Select(line => line.Amount));
            Assert.DoesNotContain(logger.Messages, m => m.Level >= LogLevel.Warning);
            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
            Assert.Equal(1, row.SyncedRecords);
        }

        private Pex2AplosMappingModel NewRebateMapping(FundingSource fundingSource, bool syncInvoices)
        {
            var mapping = NewMapping();
            mapping.PEXFundingSource = fundingSource;
            mapping.SyncTransactions = true;
            mapping.SyncRebates = true;
            mapping.SyncInvoices = syncInvoices;
            mapping.PexRebatesAplosContactId = 778;
            mapping.PexRebatesAplosFundId = int.Parse(MissionsFundId);
            mapping.PexRebatesAplosTransactionAccountNumber = 4000;
            mapping.PEXExternalAPIToken = "token";
            return mapping;
        }

        private static BusinessAccountTransactions RebateCreditTransactions() =>
            new([new TransactionModel { TransactionId = 555, Description = "Rebate Credit", TransactionAmount = 25.00m, TransactionTime = new DateTime(2026, 7, 1) }]);

        private static InvoiceModel NewInvoice(int invoiceId, decimal amount, bool isPastReturnWindow, InvoiceStatus status = InvoiceStatus.Closed) => new()
        {
            InvoiceId = invoiceId,
            InvoiceAmount = amount,
            Status = status,
            DueDate = new DateTime(2026, 7, 1),
            IsPastReturnWindow = isPastReturnWindow,
        };

        private static InvoicePaymentModel NewPayment(decimal amount, bool rejectedByBank = false, PaymentType type = PaymentType.PEXTransfer) => new()
        {
            Type = type,
            Amount = amount,
            DatePaid = new DateTime(2026, 7, 1),
            RejectedByBank = rejectedByBank,
        };

        private void SetupInvoice(InvoiceModel invoice)
        {
            _mockPexApiClient
                .Setup(client => client.GetInvoices(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([invoice]);
        }

        private void SetupPayments(params InvoicePaymentModel[] payments)
        {
            _mockPexApiClient
                .Setup(client => client.GetInvoicePayments(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(payments.ToList());
        }

        private void SetupAllocations(params InvoiceAllocationModel[] allocations)
        {
            _mockPexApiClient
                .Setup(client => client.GetInvoiceAllocations(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(allocations.ToList());
        }

        private static Pex2AplosMappingModel NewMapping() => new()
        {
            PEXBusinessAcctId = 5331803,
            AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication,
            AplosAccountId = "accountId",
            AplosClientId = "clientId",
            AplosPrivateKey = "privateKey",
            SyncInvoices = true,
            SyncInvoicesMethod = "rebate-distribute",
            AplosRegisterAccountNumber = 2000,
            TransfersAplosTransactionAccountNumber = 1000,
            TransfersAplosContactId = 777,
        };

        private sealed class ListLogger : ILogger
        {
            public List<(LogLevel Level, string Text)> Messages { get; } = [];

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => Messages.Add((logLevel, formatter(state, exception)));
        }

        private AplosIntegrationService GetAplosIntegrationService()
        {
            _mockOptions.Setup(options => options.Value).Returns(new AppSettingsModel());

            _mockHistoryStorage
                .Setup(storage => storage.CreateAsync(It.IsAny<SyncResultModel>(), It.IsAny<CancellationToken>()))
                .Callback<SyncResultModel, CancellationToken>((result, _) => _historyRows.Add(result))
                .Returns(Task.CompletedTask);

            _mockAplosApiClient
                .Setup(client => client.GetFunds(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            _mockAplosApiClient
                .Setup(client => client.CreateTransaction(It.IsAny<AplosApiTransactionDetail>(), It.IsAny<CancellationToken>()))
                .Callback<AplosApiTransactionDetail, CancellationToken>((transaction, _) => _createdTransactions.Add(transaction))
                .Returns(Task.FromResult(new AplosApiTransactionResponse()));

            _mockAplosIntegrationMappingService
                .Setup(service => service.Map(It.IsAny<IEnumerable<AplosApiFundDetail>>()))
                .Returns([new PexAplosApiObject { Id = MissionsFundId, Name = "Missions" }]);

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
                _mockHistoryStorage.Object,
                null,
                new SyncSettingsModel(),
                null,
                Mock.Of<IAplosBillMappingStorage>());
        }
    }
}
