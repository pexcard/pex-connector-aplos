using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Const;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
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

namespace AplosConnector.Common.Tests;

public abstract class BillPaymentsTestBase
{
    protected const int PexBusinessAcctId = 6118231;
    protected const string AplosPayableId = "9001";
    protected const int PexBillId = 4242;
    protected const int AchPaymentId = 5001;
    protected const long MetadataRelationId = 100;
    protected const decimal AchClearingAccountNumber = 1010m;
    protected const decimal CardClearingAccountNumber = 1020m;
    protected const decimal BillAmount = 125.50m;
    protected const long VendorCardTransactionId = 987654;
    protected static readonly DateTime UtcNow = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
    protected static readonly DateTimeOffset PayoutDate = new(2026, 9, 10, 15, 0, 0, TimeSpan.Zero);

    protected readonly Mock<IAplosApiClient> _mockAplosApiClient = new();
    protected readonly Mock<IAplosApiClientFactory> _mockAplosApiClientFactory = new();
    protected readonly Mock<IAplosIntegrationMappingService> _mockAplosIntegrationMappingService = new();
    protected readonly Mock<IPexApiClient> _mockPexApiClient = new();
    protected readonly Mock<IOptions<AppSettingsModel>> _mockOptions = new();
    protected readonly Mock<SyncHistoryStorage> _mockHistoryStorage = new(MockBehavior.Loose, (TableClient)null);
    protected readonly Mock<Pex2AplosMappingStorage> _mockMappingStorage =
        new(MockBehavior.Loose, (TableClient)null, (IStorageMappingService)null, (ILogger)null);

    protected readonly FakeBillMappingStorage _billMappingStorage = new();

    protected readonly List<SyncResultModel> _historyRows = [];
    protected readonly List<string> _relationshipNotes = [];
    protected readonly List<AplosApiPayablePaymentModel> _payments = [];
    protected readonly List<string> _cardTransactionNotes = [];

    protected BillPaymentsTestBase()
    {
        SetupDefaults();
    }

    protected async Task<Pex2AplosMappingModel> RunSync(
        bool useBillPay,
        decimal achClearingAccountNumber = AchClearingAccountNumber,
        decimal cardClearingAccountNumber = CardClearingAccountNumber,
        CancellationToken cancellationToken = default)
    {
        var mapping = new Pex2AplosMappingModel
        {
            PEXBusinessAcctId = PexBusinessAcctId,
            PEXExternalAPIToken = "token",
            AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication,
            AplosAccountId = "accountId",
            AplosClientId = "clientId",
            AplosPrivateKey = "privateKey",
            IsManualSync = true,
            EarliestTransactionDateToSync = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            BillPaymentsAchClearingAccountNumber = achClearingAccountNumber,
            BillPaymentsCardClearingAccountNumber = cardClearingAccountNumber,
            // Set by Sync's run-level settings refresh.
            UseBillPayEnabled = useBillPay
        };

        _mockPexApiClient
            .Setup(client => client.GetBusinessSettings(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessSettingsModel { UseBillPay = useBillPay });

        await GetAplosIntegrationService().SyncBillPayments(NullLogger.Instance, mapping, UtcNow, cancellationToken);
        return mapping;
    }

    protected void SeedPaidMapping() => SeedUnpaidMapping(paidSyncedUtc: new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));

    protected void SeedUnpaidMapping(
        DateTime? createdUtc = null,
        DateTime? firstFailedUtc = null,
        DateTime? paidSyncedUtc = null,
        DateTime? awaitingChargeSinceUtc = null)
    {
        _billMappingStorage.Seed(new AplosBillMappingModel
        {
            CreatedUtc = createdUtc ?? new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc),
            PEXBusinessAcctId = PexBusinessAcctId,
            AplosPayableId = AplosPayableId,
            AplosReferenceNumber = "INV-9001",
            PexBillInboxId = (int)MetadataRelationId,
            MetadataRelationId = MetadataRelationId,
            Amount = 125.50m,
            FirstFailedUtc = firstFailedUtc,
            PaidSyncedUtc = paidSyncedUtc,
            AwaitingChargeSinceUtc = awaitingChargeSinceUtc
        });
    }

    protected void SetupPaidPexBill(
        PayeeFundsDestinationType fundsDestinationType = PayeeFundsDestinationType.BankAccount,
        bool hasPayoutDate = true,
        long? metadataRelationId = MetadataRelationId,
        decimal amount = 125.50m,
        bool linkedCardCharge = true,
        DateTimeOffset? payoutDate = null,
        PaymentRequestMetadataModel metadata = null,
        PaymentRequestStatusTrigger statusTrigger = PaymentRequestStatusTrigger.Paid)
    {
        _mockPexApiClient
            .Setup(client => client.GetBillPayments(It.IsAny<string>(), It.IsAny<BillPaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillPaymentListResponseModel
            {
                Items = [new BillPaymentModel
                {
                    BillId = PexBillId,
                    BillRefNo = "INV-9001",
                    Amount = 125.50m,
                    PaymentRequestStatus = PaymentRequestStatus.Closed,
                    PaymentRequestStatusTrigger = PaymentRequestStatusTrigger.Paid
                }],
                PageInfo = new PageInfoModel { Page = 1, PageSize = 50, TotalItems = 1 }
            });

        _mockPexApiClient
            .Setup(client => client.GetBillPaymentRequest(It.IsAny<string>(), PexBillId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillPaymentRequestModel
            {
                PaymentRequestId = PexBillId,
                PaymentRequestType = PaymentRequestType.BillPay,
                PayeeFundsDestinationType = fundsDestinationType,
                SettlementTransactionId = fundsDestinationType == PayeeFundsDestinationType.BankAccount || !linkedCardCharge ? null : VendorCardTransactionId.ToString(),
                MetadataRelationId = metadataRelationId,
                Amount = amount,
                BillRefNo = "INV-9001",
                UserFirstName = "Dana",
                UserLastName = "Ross",
                PaymentRequestStatus = PaymentRequestStatus.Closed,
                PaymentRequestStatusTrigger = statusTrigger,
                PayoutDate = hasPayoutDate ? payoutDate ?? PayoutDate : null,
                PaymentId = AchPaymentId,
                Metadata = metadata
            });
    }

    protected void SetupAchPayment(PaymentStatusTrigger trigger)
    {
        _mockPexApiClient
            .Setup(client => client.GetPayments(It.IsAny<string>(), It.IsAny<PaymentListRequestModel>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentListResponseModel
            {
                Items = [new PaymentModel { PaymentId = AchPaymentId, PaymentStatus = PaymentStatus.Closed, PaymentStatusTrigger = trigger, Amount = 125.50m }],
                PageInfo = new PageInfoModel { Page = 1, PageSize = 50, TotalItems = 1 }
            });
    }

    protected void SetupLivePayable(decimal amount, decimal paid)
    {
        _mockAplosApiClient
            .Setup(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AplosApiPayableResponse
            {
                Data = new AplosApiPayableData
                {
                    Payable = new AplosApiPayableDetail
                    {
                        Id = AplosPayableId,
                        ReferenceNumber = "INV-9001",
                        Amount = amount,
                        PaidAmount = paid
                    }
                }
            });
    }

    protected void SetupLivePayableSequence(params (decimal amount, decimal paid)[] reads)
    {
        var sequence = _mockAplosApiClient.SetupSequence(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()));
        foreach (var (amount, paid) in reads)
        {
            sequence = sequence.ReturnsAsync(new AplosApiPayableResponse
            {
                Data = new AplosApiPayableData
                {
                    Payable = new AplosApiPayableDetail { Id = AplosPayableId, ReferenceNumber = "INV-9001", Amount = amount, PaidAmount = paid }
                }
            });
        }
    }

    // A null account is Aplos not returning one.
    protected void SetupClearingAccount(AplosApiAccountDetail account)
    {
        _mockAplosApiClient
            .Setup(client => client.GetAccount(It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal accountNumber, CancellationToken _) => new AplosApiAccountResponse
            {
                Data = new AplosApiAccountData
                {
                    Account = account is null ? null : new AplosApiAccountDetail
                    {
                        AccountNumber = accountNumber,
                        Name = account.Name,
                        Category = account.Category,
                        IsEnabled = account.IsEnabled
                    }
                }
            });
    }

    protected void SetupPayCallAnswers405()
    {
        _mockAplosApiClient
            .Setup(client => client.PayPayable(It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AplosApiException(new AplosApiErrorResponse
            {
                Status = 405,
                Exception = new AplosApiErrorDetail { Code = 3001, Message = "Caller requested a resource that is not available." }
            }));
    }

    protected void VerifyNoPayCall()
    {
        Assert.Empty(_payments);
        _mockAplosApiClient.Verify(client => client.PayPayable(
            It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    protected void SetupDefaults()
    {
        _mockOptions.Setup(options => options.Value).Returns(new AppSettingsModel());

        _mockHistoryStorage
            .Setup(storage => storage.CreateAsync(It.IsAny<SyncResultModel>(), It.IsAny<CancellationToken>()))
            .Callback<SyncResultModel, CancellationToken>((result, _) => _historyRows.Add(result))
            .Returns(Task.CompletedTask);

        _mockMappingStorage
            .Setup(storage => storage.UpdateAsync(It.IsAny<Pex2AplosMappingModel>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockPexApiClient
            .Setup(client => client.GetBillPayments(It.IsAny<string>(), It.IsAny<BillPaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillPaymentListResponseModel { Items = [], PageInfo = new PageInfoModel() });

        _mockPexApiClient
            .Setup(client => client.AddTransactionRelationshipNote(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, long, string, CancellationToken>((_, _, noteText, _) => _relationshipNotes.Add(noteText))
            .Returns(Task.CompletedTask);

        _mockAplosApiClient
            .Setup(client => client.PayPayable(It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, AplosApiPayablePaymentModel, CancellationToken>((_, payment, _) => _payments.Add(payment))
            .ReturnsAsync(() => new AplosApiPayableResponse
            {
                Data = new AplosApiPayableData { Payable = new AplosApiPayableDetail { Id = AplosPayableId, Amount = 125.50m, PaidAmount = 125.50m } }
            });

        _mockPexApiClient
            .Setup(client => client.AddTransactionNote(It.IsAny<string>(), It.IsAny<TransactionModel>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransactionModel, string, bool, bool, CancellationToken>((_, transaction, noteText, _, _, _) =>
            {
                if (transaction.TransactionId == VendorCardTransactionId) _cardTransactionNotes.Add(noteText);
            })
            .Returns(Task.CompletedTask);

        _mockPexApiClient
            .Setup(client => client.GetCardholderTransaction(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, long transactionId, CancellationToken _) => new TransactionResultModel
            {
                TransactionId = transactionId,
                Notes = transactionId == VendorCardTransactionId
                    ? _cardTransactionNotes.Select(note => new TransactionResultNoteModel { Content = note }).ToList()
                    : []
            });

        // Every clearing account is found, enabled and an asset account unless a test says otherwise.
        SetupClearingAccount(new AplosApiAccountDetail { Name = "Clearing", Category = "asset", IsEnabled = true });

        _mockAplosApiClientFactory
            .Setup(factory => factory.CreateClient(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Uri>(),
                It.IsAny<Func<ILogger, AplosAuthModel>>(),
                It.IsAny<Func<AplosAuthModel, ILogger, CancellationToken, Task>>()))
            .Returns(_mockAplosApiClient.Object);
    }

    protected AplosIntegrationService GetAplosIntegrationService()
    {
        return new AplosIntegrationService(
            new NullLogger<AplosIntegrationService>(),
            _mockOptions.Object,
            _mockAplosApiClientFactory.Object,
            _mockAplosIntegrationMappingService.Object,
            _mockPexApiClient.Object,
            _mockHistoryStorage.Object,
            _mockMappingStorage.Object,
            new SyncSettingsModel(),
            null,
            _billMappingStorage,
            Mock.Of<IAplosVendorCardOrderStorage>());
    }

    protected sealed class FakeBillMappingStorage : IAplosBillMappingStorage
    {
        public readonly Dictionary<string, AplosBillMappingModel> Rows = new(StringComparer.OrdinalIgnoreCase);

        public bool FailNextWrite { get; set; }

        public void Seed(AplosBillMappingModel model) => Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)] = Copy(model);

        // Copies in and out, like a table: a model the service mutates is not the stored row until it is written.
        public Task<List<AplosBillMappingModel>> GetByBusinessAsync(int pexBusinessAcctId, CancellationToken cancellationToken)
            => Task.FromResult(Rows.Values.Where(row => row.PEXBusinessAcctId == pexBusinessAcctId).Select(Copy).ToList());

        public Task AddAsync(AplosBillMappingModel model, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)] = Copy(model);
            return Task.CompletedTask;
        }

        // Mirrors AplosBillMappingStorage: update-only, and only the paid column changes.
        public Task MarkPaidAsync(AplosBillMappingModel model, DateTime paidUtc, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.PaidSyncedUtc = paidUtc;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)].PaidSyncedUtc = paidUtc;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(AplosBillMappingModel model, DateTime failedUtc, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.FirstFailedUtc = failedUtc;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)].FirstFailedUtc = failedUtc;
            return Task.CompletedTask;
        }


        public Task MarkAwaitingChargeAsync(AplosBillMappingModel model, DateTime awaitingSinceUtc, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.AwaitingChargeSinceUtc = awaitingSinceUtc;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)].AwaitingChargeSinceUtc = awaitingSinceUtc;
            return Task.CompletedTask;
        }

        private void ThrowIfFailing()
        {
            if (!FailNextWrite) return;

            FailNextWrite = false;
            throw new InvalidOperationException("Table storage is unavailable.");
        }

        private static string Key(int pexBusinessAcctId, string aplosPayableId) => $"{pexBusinessAcctId}|{aplosPayableId}";

        private static AplosBillMappingModel Copy(AplosBillMappingModel model) => new()
        {
            PEXBusinessAcctId = model.PEXBusinessAcctId,
            AplosPayableId = model.AplosPayableId,
            AplosReferenceNumber = model.AplosReferenceNumber,
            PexBillInboxId = model.PexBillInboxId,
            MetadataRelationId = model.MetadataRelationId,
            Amount = model.Amount,
            PaidSyncedUtc = model.PaidSyncedUtc,
            FirstFailedUtc = model.FirstFailedUtc,
            AwaitingChargeSinceUtc = model.AwaitingChargeSinceUtc,
            CreatedUtc = model.CreatedUtc
        };
    }
}
