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

            await GetAplosIntegrationService().SyncInvoices(NullLogger.Instance, NewMapping(), [], new DateTime(2026, 7, 1), default);

            Assert.Empty(_createdTransactions);
            _mockPexApiClient.Verify(client => client.GetInvoiceAllocations(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
            Assert.Equal(0, row.SyncedRecords);
        }

        [Fact]
        public async Task SyncInvoices_Fails_WhenRejectedPaymentsDoNotExplainTheShortfall()
        {
            SetupInvoice(NewInvoice(98764, 100.00m, isPastReturnWindow: true));
            SetupPayments(NewPayment(20.00m), NewPayment(5.00m, rejectedByBank: true));

            await GetAplosIntegrationService().SyncInvoices(NullLogger.Instance, NewMapping(), [], new DateTime(2026, 7, 1), default);

            Assert.Empty(_createdTransactions);
            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Failed.ToString(), row.SyncStatus);
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

        private static InvoiceModel NewInvoice(int invoiceId, decimal amount, bool isPastReturnWindow, InvoiceStatus status = InvoiceStatus.Closed) => new()
        {
            InvoiceId = invoiceId,
            InvoiceAmount = amount,
            Status = status,
            DueDate = new DateTime(2026, 7, 1),
            IsPastReturnWindow = isPastReturnWindow,
        };

        private static InvoicePaymentModel NewPayment(decimal amount, bool rejectedByBank = false) => new()
        {
            Type = PaymentType.PEXTransfer,
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
                null);
        }
    }
}
