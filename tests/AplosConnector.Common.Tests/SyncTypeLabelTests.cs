using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Const;
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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests
{
    public class SyncTypeLabelTests
    {
        private readonly Mock<IAplosApiClient> _mockAplosApiClient = new();
        private readonly Mock<IAplosApiClientFactory> _mockAplosApiClientFactory = new();
        private readonly Mock<IAplosIntegrationMappingService> _mockAplosIntegrationMappingService = new();
        private readonly Mock<IPexApiClient> _mockPexApiClient = new();
        private readonly Mock<IOptions<AppSettingsModel>> _mockOptions = new();
        private readonly Mock<SyncHistoryStorage> _mockHistoryStorage = new(MockBehavior.Loose, (TableClient)null);

        private readonly List<SyncResultModel> _historyRows = [];

        [Fact]
        public async Task SyncInvoices_WritesPexStatementPaymentsHistoryRow_WhenThereIsNothingToSync()
        {
            SetupInvoices([]);

            await GetAplosIntegrationService().SyncInvoices(
                NullLogger.Instance, NewMapping(), [], new DateTime(2026, 8, 1), default);

            var row = Assert.Single(_historyRows);
            Assert.Equal("PEX Statement Payments", row.SyncType);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
        }

        [Fact]
        public async Task SyncInvoices_WritesPexStatementPaymentsHistoryRow_WhenAnInvoiceFails()
        {
            SetupInvoices([new InvoiceModel
            {
                InvoiceId = 94460,
                InvoiceAmount = 110.00m,
                Status = InvoiceStatus.Closed,
                DueDate = new DateTime(2026, 8, 1),
            }]);

            _mockPexApiClient
                .Setup(client => client.GetInvoicePayments(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            await GetAplosIntegrationService().SyncInvoices(
                NullLogger.Instance, NewMapping(), [], new DateTime(2026, 8, 1), default);

            var row = Assert.Single(_historyRows);
            Assert.Equal("PEX Statement Payments", row.SyncType);
            Assert.Equal(SyncStatus.Failed.ToString(), row.SyncStatus);
        }

        [Fact]
        public void BillPaymentsLabelIsReservedForTheApStage()
        {
            // Title case, deliberately not the sentence-case value three years of history holds.
            Assert.Equal("Bill Payments", SyncTypes.BillPayments);

            Assert.DoesNotContain(SyncTypes.BillPayments, new[]
            {
                SyncTypes.TagValuesFunds,
                SyncTypes.TagValuesAccounts,
                SyncTypes.Transactions,
                SyncTypes.Rebates,
                SyncTypes.PexStatementPayments,
                SyncTypes.Transfers,
                SyncTypes.PexAccountFees,
                SyncTypes.Reimbursements,
            });
        }

        private void SetupInvoices(List<InvoiceModel> invoices)
        {
            _mockPexApiClient
                .Setup(client => client.GetInvoices(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(invoices);
        }

        private static Pex2AplosMappingModel NewMapping() => new()
        {
            PEXBusinessAcctId = 6118231,
            AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication,
            AplosAccountId = "accountId",
            AplosClientId = "clientId",
            AplosPrivateKey = "privateKey",
            SyncInvoices = true,
            SyncInvoicesMethod = "rebate-distribute",
        };

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

            _mockAplosIntegrationMappingService
                .Setup(service => service.Map(It.IsAny<IEnumerable<AplosApiFundDetail>>()))
                .Returns([]);

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
