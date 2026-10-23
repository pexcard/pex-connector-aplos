using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
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

namespace AplosConnector.Common.Tests;

public abstract class BillPaymentReversalTestBase
{
    protected const int PexBusinessAcctId = 6118231;
    protected const string AplosPayableId = "4250949";
    protected const int PexBillId = 4242;
    protected const long MetadataRelationId = 100;
    protected const decimal CashAccountNumber = 1000m;
    protected const decimal LiabilityAccountNumber = 2100m;
    protected const decimal FirstLineAmount = 8.59m;
    protected const decimal SecondLineAmount = 8.58m;
    protected const decimal BillAmount = FirstLineAmount + SecondLineAmount;

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
    protected readonly List<AplosApiTransactionDetail> _createdAplosTransactions = [];
    protected readonly List<CreateBillInboxRequestModel> _createdBillInbox = [];

    protected decimal _payablePaidAmount;
    protected int _nextAplosTransactionId = 77001;

    protected BillPaymentReversalTestBase()
    {
        SetupDefaults();
    }

    protected AplosBillMappingModel Row() => Assert.Single(_billMappingStorage.Rows.Values);

    protected async Task RunBillPaymentsSync()
    {
        await GetAplosIntegrationService().SyncBillPayments(NullLogger.Instance, NewMapping(), UtcNow, default);
    }

    protected async Task RunOutstandingBillsSync()
    {
        var mapping = NewMapping();
        mapping.SyncOutstandingBills = true;
        await GetAplosIntegrationService().SyncOutstandingBills(NullLogger.Instance, mapping, UtcNow, default);
    }

    protected static Pex2AplosMappingModel NewMapping() => new()
    {
        PEXBusinessAcctId = PexBusinessAcctId,
        PEXExternalAPIToken = "token",
        AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication,
        AplosAccountId = "accountId",
        AplosClientId = "clientId",
        AplosPrivateKey = "privateKey",
        IsManualSync = true,
        EarliestTransactionDateToSync = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        // Set by Sync's run-level settings refresh.
        UseBillPayEnabled = true,
        SyncOutstandingBills = true,
        BillPaymentsAchClearingAccountNumber = CashAccountNumber,
        AplosRegisterAccountNumber = CashAccountNumber
    };

    protected void SeedUnpaidMapping() => SeedMapping(paidSyncedUtc: null);

    protected void SeedPaidMapping() => SeedMapping(paidSyncedUtc: UtcNow);

    protected void SeedMapping(DateTime? paidSyncedUtc)
    {
        _billMappingStorage.Seed(new AplosBillMappingModel
        {
            PEXBusinessAcctId = PexBusinessAcctId,
            AplosPayableId = AplosPayableId,
            AplosReferenceNumber = "INV-4250949",
            PexBillInboxId = (int)MetadataRelationId,
            MetadataRelationId = MetadataRelationId,
            Amount = BillAmount,
            PaidSyncedUtc = paidSyncedUtc
        });
    }

    protected static AplosApiPayableDetail NewPayable(decimal paid = 0m) => new()
    {
        Id = AplosPayableId,
        ReferenceNumber = "INV-4250949",
        BillDate = new DateTime(2026, 9, 1),
        DueDate = new DateTime(2026, 9, 11),
        Amount = BillAmount,
        PaidAmount = paid,
        Contact = new AplosApiContactDetail { Id = 16181656, CompanyName = "7 Eleven", Type = "company" },
        LiabilityAccount = new LiabilityAccountDetail { AccountNumber = (int)LiabilityAccountNumber, Name = "Accounts Payable" }
    };

    protected void SetupPexBill(PaymentRequestStatus status, PaymentRequestStatusTrigger trigger)
    {
        _mockPexApiClient
            .Setup(client => client.GetBillPayments(It.IsAny<string>(), It.IsAny<BillPaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillPaymentListResponseModel
            {
                Items = [new BillPaymentModel
                {
                    BillId = PexBillId,
                    BillRefNo = "INV-4250949",
                    Amount = BillAmount,
                    PaymentRequestStatus = status,
                    PaymentRequestStatusTrigger = trigger
                }],
                PageInfo = new PageInfoModel { Page = 1, PageSize = 50, TotalItems = 1 }
            });

        _mockPexApiClient
            .Setup(client => client.GetBillPaymentRequest(It.IsAny<string>(), PexBillId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillPaymentRequestModel
            {
                PaymentRequestId = PexBillId,
                PaymentRequestType = PaymentRequestType.BillPay,
                PayeeFundsDestinationType = PayeeFundsDestinationType.BankAccount,
                MetadataRelationId = MetadataRelationId,
                Amount = BillAmount,
                BillRefNo = "INV-4250949",
                MerchantName = "7 Eleven",
                UserFirstName = "Dana",
                UserLastName = "Ross",
                PaymentRequestStatus = status,
                PaymentRequestStatusTrigger = trigger,
                PayoutDate = PayoutDate
            });
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
            .Setup(client => client.GetBusinessSettings(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessSettingsModel { UseBillPay = true });

        _mockPexApiClient
            .Setup(client => client.GetBillPayments(It.IsAny<string>(), It.IsAny<BillPaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillPaymentListResponseModel { Items = [], PageInfo = new PageInfoModel() });

        _mockPexApiClient
            .Setup(client => client.AddTransactionRelationshipNote(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, long, string, CancellationToken>((_, _, noteText, _) => _relationshipNotes.Add(noteText))
            .Returns(Task.CompletedTask);

        _mockPexApiClient
            .Setup(client => client.GetVendors(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VendorListResponseModel
            {
                Vendors = [new VendorModel { VendorId = 7, VendorName = "7 Eleven", CustomId = "APLOS16181656" }]
            });

        _mockPexApiClient
            .Setup(client => client.GetBusinessDetails(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessDetailsModel { CHAccountList = [] });

        _mockPexApiClient
            .Setup(client => client.SearchBillInbox(It.IsAny<string>(), It.IsAny<SearchBillInboxRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchBillInboxResponseModel { Items = [], PageInfo = new PageInfoModel() });

        _mockPexApiClient
            .Setup(client => client.CreateBillInbox(It.IsAny<string>(), It.IsAny<CreateBillInboxRequestModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, CreateBillInboxRequestModel, CancellationToken>((_, request, _) => _createdBillInbox.Add(request))
            .ReturnsAsync(new BillInboxModel { BillInboxId = 500, MetadataId = 500 });

        _mockPexApiClient
            .Setup(client => client.IsTagsAvailable(It.IsAny<string>(), It.IsAny<CustomFieldType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockAplosApiClient
            .Setup(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AplosApiPayableResponse
            {
                Data = new AplosApiPayableData { Payable = NewPayable(_payablePaidAmount) }
            });

        _mockAplosApiClient
            .Setup(client => client.GetPayables(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [NewPayable(_payablePaidAmount)]);

        _mockAplosApiClient
            .Setup(client => client.CreateTransaction(It.IsAny<AplosApiTransactionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<AplosApiTransactionDetail, CancellationToken>((aplosTransaction, _) => _createdAplosTransactions.Add(aplosTransaction))
            .ReturnsAsync(() => new AplosApiTransactionResponse
            {
                Data = new AplosApiTransactionData { Transaction = new AplosApiTransactionDetail { Id = _nextAplosTransactionId++ } }
            });

        _mockAplosApiClient
            .Setup(client => client.PayPayable(It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, AplosApiPayablePaymentModel, CancellationToken>((_, payment, _) => _payments.Add(payment))
            .ReturnsAsync(() => new AplosApiPayableResponse
            {
                Data = new AplosApiPayableData { Payable = NewPayable(BillAmount) }
            });

        // The ACH bank account is found, enabled and an asset account unless a test says otherwise.
        _mockAplosApiClient
            .Setup(client => client.GetAccount(It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal accountNumber, CancellationToken _) => new AplosApiAccountResponse
            {
                Data = new AplosApiAccountData
                {
                    Account = new AplosApiAccountDetail { AccountNumber = accountNumber, Name = "Clearing", Category = "asset", Type = "Register", Activity = "cash", IsEnabled = true }
                }
            });

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

    private AplosIntegrationService GetAplosIntegrationService()
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

        // The name of the next write to fail, once.
        public string FailNext { get; set; }

        public void Seed(AplosBillMappingModel model) => Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)] = model;

        public Task<List<AplosBillMappingModel>> GetByBusinessAsync(int pexBusinessAcctId, CancellationToken cancellationToken)
            => Task.FromResult(Rows.Values.Where(row => row.PEXBusinessAcctId == pexBusinessAcctId).ToList());

        public Task AddAsync(AplosBillMappingModel model, CancellationToken cancellationToken) => UpsertAsync(model, cancellationToken);

        public Task UpsertAsync(AplosBillMappingModel model, CancellationToken cancellationToken)
        {
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)] = model;
            return Task.CompletedTask;
        }

        public Task MarkPaidAsync(AplosBillMappingModel model, DateTime paidUtc, CancellationToken cancellationToken)
        {
            ThrowIfFailing(nameof(MarkPaidAsync));
            model.PaidSyncedUtc = paidUtc;
            return UpsertAsync(model, cancellationToken);
        }

        public Task MarkFailedAsync(AplosBillMappingModel model, DateTime failedUtc, CancellationToken cancellationToken)
        {
            model.FirstFailedUtc = failedUtc;
            return UpsertAsync(model, cancellationToken);
        }

        public Task MarkReversedAsync(AplosBillMappingModel model, DateTime reversedUtc, CancellationToken cancellationToken)
        {
            ThrowIfFailing(nameof(MarkReversedAsync));
            model.ReversedUtc = reversedUtc;
            model.PaidSyncedUtc = null;
            model.FirstFailedUtc = null;
            model.AwaitingChargeSinceUtc = null;
            model.AwaitingChargeSinceUtc = null;
            return UpsertAsync(model, cancellationToken);
        }

        public Task ClearReversedAsync(AplosBillMappingModel model, CancellationToken cancellationToken)
        {
            ThrowIfFailing(nameof(ClearReversedAsync));
            model.ReversedUtc = null;
            return UpsertAsync(model, cancellationToken);
        }

        public Task MarkAwaitingChargeAsync(AplosBillMappingModel model, DateTime awaitingSinceUtc, CancellationToken cancellationToken)
        {
            model.AwaitingChargeSinceUtc = awaitingSinceUtc;
            return UpsertAsync(model, cancellationToken);
        }

        public Task MarkPaidFromClearingAsync(AplosBillMappingModel model, decimal clearingAccountNumber, CancellationToken cancellationToken)
        {
            model.PaidFromClearingAccountNumber = clearingAccountNumber;
            return UpsertAsync(model, cancellationToken);
        }

        public Task ClearPaidFromClearingAsync(AplosBillMappingModel model, CancellationToken cancellationToken)
        {
            model.PaidFromClearingAccountNumber = null;
            return UpsertAsync(model, cancellationToken);
        }

        public Task MarkCardChargeClaimedAsync(AplosBillMappingModel model, long cardTransactionId, CancellationToken cancellationToken)
        {
            model.CardTransactionId = cardTransactionId;
            return UpsertAsync(model, cancellationToken);
        }

        private void ThrowIfFailing(string write)
        {
            if (FailNext != write) return;

            FailNext = null;
            throw new InvalidOperationException("Table storage is unavailable.");
        }

        private static string Key(int pexBusinessAcctId, string aplosPayableId) => $"{pexBusinessAcctId}|{aplosPayableId}";
    }
}
