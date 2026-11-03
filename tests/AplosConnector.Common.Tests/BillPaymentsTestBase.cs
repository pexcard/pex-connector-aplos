using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Const;
using AplosConnector.Common.Entities;
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
using Newtonsoft.Json.Linq;
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

public abstract partial class BillPaymentsTestBase
{
    protected const int PexBusinessAcctId = 6118231;
    protected const string AplosPayableId = "9001";
    protected const int PexBillId = 4242;
    protected const int AchPaymentId = 5001;
    protected const long MetadataRelationId = 100;
    protected const decimal AchClearingAccountNumber = 1010m;
    protected const decimal CardClearingAccountNumber = 1020m;
    protected const decimal PexFundsAccountNumber = 1030m;
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
    protected readonly List<JObject> _payableUpdates = [];
    protected readonly List<string> _aplosWrites = [];

    // The raw payable as Aplos stores it; the note write reads and replaces it.
    protected JObject _payableForUpdate = PayableForUpdate("Customer memo");

    protected BillPaymentsTestBase()
    {
        SetupDefaults();
    }

    protected async Task<Pex2AplosMappingModel> RunSync(
        bool useBillPay,
        decimal achClearingAccountNumber = AchClearingAccountNumber,
        decimal cardClearingAccountNumber = CardClearingAccountNumber,
        decimal pexFundsAccountNumber = PexFundsAccountNumber,
        bool syncBillPayments = true,
        Action<Pex2AplosMappingModel> configure = null,
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
            AplosRegisterAccountNumber = pexFundsAccountNumber,
            // Set by Sync's run-level settings refresh, which also clears the toggles when bill pay is off.
            UseBillPayEnabled = useBillPay,
            SyncOutstandingBills = useBillPay,
            SyncBillPayments = syncBillPayments && useBillPay
        };
        configure?.Invoke(mapping);

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
                    PayeeName = "Acme Supplies",
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
                        Type = account.Type,
                        Activity = account.Activity,
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

    // A null note is Aplos leaving the key out.
    protected static JObject PayableForUpdate(string note)
    {
        var payable = new JObject
        {
            ["id"] = 9001,
            ["reference_num"] = "INV-9001",
            ["modified"] = "2026-09-02T05:00:00.000-0700",
            ["amount"] = 125.50m,
            ["lines"] = new JArray(new JObject { ["id"] = 1, ["amount"] = 125.50m, ["tags"] = new JArray(new JObject { ["id"] = 120728 }) })
        };
        if (note is not null) payable["note"] = note;
        return payable;
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
            .Callback<string, AplosApiPayablePaymentModel, CancellationToken>((_, payment, _) =>
            {
                _payments.Add(payment);
                _aplosWrites.Add("pay");
            })
            .ReturnsAsync(() => new AplosApiPayableResponse
            {
                Data = new AplosApiPayableData { Payable = new AplosApiPayableDetail { Id = AplosPayableId, Amount = 125.50m, PaidAmount = 125.50m } }
            });

        _mockAplosApiClient
            .Setup(client => client.GetPayableForUpdate(AplosPayableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (JObject)_payableForUpdate?.DeepClone());

        _mockAplosApiClient
            .Setup(client => client.UpdatePayable(AplosPayableId, It.IsAny<JObject>(), It.IsAny<CancellationToken>()))
            .Callback<string, JObject, CancellationToken>((_, payable, _) =>
            {
                _payableUpdates.Add((JObject)payable.DeepClone());
                _payableForUpdate = (JObject)payable.DeepClone();
                _aplosWrites.Add("note");
            })
            .Returns(Task.CompletedTask);

        _mockPexApiClient
            .Setup(client => client.AddTransactionNote(It.IsAny<string>(), It.IsAny<TransactionModel>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransactionModel, string, bool, bool, CancellationToken>((_, transaction, noteText, _, _, _) =>
            {
                if (transaction.TransactionId == VendorCardTransactionId) _cardTransactionNotes.Add(noteText);
            })
            .Returns(Task.CompletedTask);

        _mockPexApiClient
            .Setup(client => client.GetCardholderPurchase(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, long transactionId, CancellationToken _) => new TransactionResultModel
            {
                TransactionId = transactionId,
                Notes = transactionId == VendorCardTransactionId
                    ? _cardTransactionNotes.Select(note => new TransactionResultNoteModel { Content = note }).ToList()
                    : []
            });

        // Every clearing account is found, enabled and an asset account unless a test says otherwise.
        SetupClearingAccount(new AplosApiAccountDetail { Name = "Clearing", Category = "asset", Type = "Register", Activity = "cash", IsEnabled = true });

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
}
