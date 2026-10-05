using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class BillPaymentTransactionSyncTests : BillPaymentsTestBase
{
    private const int VendorCardAcctId = 5150;
    private const decimal ExpenseAccountNumber = 5000m;
    private const decimal RegisterAccountNumber = 1050m;
    private const int FundId = 1;

    private readonly List<AplosApiTransactionDetail> _createdAplosTransactions = [];
    private readonly List<bool> _includeVendorBillPayArgs = [];
    private bool _paidByCard;

    public BillPaymentTransactionSyncTests()
    {
        SetupTransactionSyncDefaults();
    }

    [Fact]
    public async Task PayingAnAplosBillByVendorCardMarksThePayablePaidAndBooksNoExpense()
    {
        SeedUnpaidMapping();
        SetupPaidBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: 0m);

        await RunBothSyncs(mapping => mapping.SyncOutstandingBills = true);

        Assert.Single(_payments);
        Assert.Empty(_createdAplosTransactions);
        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);

        var marker = Assert.Single(_cardTransactionNotes);
        Assert.Contains(AplosIntegrationService.SyncedAsBillPaymentNote, marker);
        Assert.Contains("Paid via Card", marker);

        Assert.Equal([true], _includeVendorBillPayArgs.Distinct());
    }

    [Fact]
    public async Task AFailedPayCallKeepsTheMarkedChargeOutOfTheTransactionSync()
    {
        SeedUnpaidMapping();
        SetupPaidBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: 0m);
        _mockAplosApiClient
            .Setup(client => client.PayPayable(It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AplosApiException(new AplosApiErrorResponse { Status = 503 }));

        await RunBothSyncs();

        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.Contains(_cardTransactionNotes, note => note.Contains(AplosIntegrationService.SyncedAsBillPaymentNote));
        Assert.Empty(_createdAplosTransactions);
    }

    [Fact]
    public async Task APexOriginatedBillPaidByCardIsBookedAsAPurchaseByTheTransactionSync()
    {
        SetupPaidBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);

        await RunBothSyncs();

        Assert.Empty(_payments);
        var aplosTransaction = Assert.Single(_createdAplosTransactions);
        Assert.Equal(VendorCardTransactionId.ToString(), aplosTransaction.Note);
        Assert.Single(aplosTransaction.Lines, line => line.Account.AccountNumber == RegisterAccountNumber);
        Assert.DoesNotContain(aplosTransaction.Lines, line => line.Account.AccountNumber == CashAccountNumber);
    }

    [Fact]
    public async Task APexOriginatedBillPaidByAchIsNotSynced()
    {
        SetupPaidBill(PayeeFundsDestinationType.BankAccount);

        await RunBothSyncs();

        Assert.Empty(_payments);
        Assert.Empty(_createdAplosTransactions);
    }

    [Fact]
    public async Task AnAutoBilledVendorCardChargeIsBookedAsAPurchaseByTheTransactionSync()
    {
        SetupPaidBill(PayeeFundsDestinationType.VendorVirtualCard, PaymentRequestStatusTrigger.AutoBilledVendorCard);

        await RunBothSyncs();

        var aplosTransaction = Assert.Single(_createdAplosTransactions);
        Assert.Single(aplosTransaction.Lines, line => line.Account.AccountNumber == RegisterAccountNumber);
        Assert.Empty(_payments);
    }

    [Fact]
    public async Task TurningTheImportOffStillClosesBillsAlreadyImported()
    {
        SeedUnpaidMapping();
        SetupPaidBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: 0m);

        await RunBothSyncs(mapping => mapping.SyncOutstandingBills = false);

        Assert.Single(_payments);
        Assert.Empty(_createdAplosTransactions);
        Assert.Contains(_cardTransactionNotes, note => note.Contains(AplosIntegrationService.SyncedAsBillPaymentNote));
    }

    [Fact]
    public async Task AnAlreadyMarkedPaidAplosBillIsNotPaidOrBookedAgain()
    {
        SeedPaidMapping();
        SetupPaidBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: BillAmount);
        _cardTransactionNotes.Add($"{AplosIntegrationService.SyncedAsBillPaymentNote} #{PexBillId}. Paid via Card");

        await RunBothSyncs();

        Assert.Empty(_payments);
        Assert.Empty(_createdAplosTransactions);
    }

    [Fact]
    public async Task AFailedBusinessSettingsReadDoesNotStopTheRun()
    {
        _mockPexApiClient
            .Setup(client => client.GetBusinessSettings(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PEX unavailable"));
        _mockAplosApiClient
            .Setup(client => client.GetAplosAccessToken(It.IsAny<CancellationToken>()))
            .ReturnsAsync("aplos-token");
        SetupPaidBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);

        var mapping = NewSyncMapping();
        mapping.PEXFundingSource = 0;
        mapping.AplosPartnerVerified = true;

        await GetAplosIntegrationService().Sync(mapping, default);

        _mockPexApiClient.Verify(client => client.GetAllCardholderTransactions(
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SyncMarksTheChargeBeforeTheTransactionSyncReadsIt()
    {
        SeedUnpaidMapping();
        SetupPaidBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        SetupLivePayable(BillAmount, paid: 0m);
        _mockAplosApiClient
            .Setup(client => client.GetAplosAccessToken(It.IsAny<CancellationToken>()))
            .ReturnsAsync("aplos-token");

        var mapping = NewSyncMapping();
        mapping.AplosPartnerVerified = true;

        await GetAplosIntegrationService().Sync(mapping, default);

        Assert.Single(_payments);
        Assert.Empty(_createdAplosTransactions);
    }

    private async Task<Pex2AplosMappingModel> RunBothSyncs(Action<Pex2AplosMappingModel> configureMapping = null)
    {
        var mapping = NewSyncMapping();

        configureMapping?.Invoke(mapping);

        // Sync's order: the marker must be on the charge before the transaction sync reads it.
        var service = GetAplosIntegrationService();
        await service.SyncBillPayments(NullLogger.Instance, mapping, UtcNow, default);
        await service.SyncTransactions(NullLogger.Instance, mapping, UtcNow, default);
        return mapping;
    }

    private static Pex2AplosMappingModel NewSyncMapping() => new()
    {
        PEXBusinessAcctId = PexBusinessAcctId,
        PEXExternalAPIToken = "token",
        AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication,
        AplosAccountId = "accountId",
        AplosClientId = "clientId",
        AplosPrivateKey = "privateKey",
        IsManualSync = true,
        EarliestTransactionDateToSync = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        SyncTransactions = true,
        SyncTransactionsCreateContact = true,
        AplosRegisterAccountNumber = RegisterAccountNumber,
        DefaultAplosFundId = FundId,
        DefaultAplosTransactionAccountNumber = ExpenseAccountNumber,
        ExpenseAccountMappings = [],
        // Set by Sync's run-level settings refresh.
        UseBillPayEnabled = true,
        BillPaymentsAplosCashAccountNumber = CashAccountNumber
    };

    private void SetupPaidBill(PayeeFundsDestinationType fundsDestinationType, PaymentRequestStatusTrigger trigger = PaymentRequestStatusTrigger.Paid)
    {
        // Only a card payment leaves a card transaction behind.
        _paidByCard = fundsDestinationType != PayeeFundsDestinationType.BankAccount;
        SetupPaidPexBill(fundsDestinationType, statusTrigger: trigger);
    }

    private void SetupTransactionSyncDefaults()
    {
        _mockPexApiClient
            .Setup(client => client.GetBusinessSettings(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessSettingsModel { UseBillPay = true });

        _mockPexApiClient
            .Setup(client => client.IsTagsAvailable(It.IsAny<string>(), It.IsAny<CustomFieldType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockPexApiClient
            .Setup(client => client.GetTags(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // PEX returns a bill pay card transaction only when the caller asks for it.
        _mockPexApiClient
            .Setup(client => client.GetAllCardholderTransactions(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DateTime startDate, DateTime endDate, bool _, bool _, bool includeVendorBillPay, CancellationToken _) =>
            {
                _includeVendorBillPayArgs.Add(includeVendorBillPay);

                var inBatch = startDate <= PayoutDate.UtcDateTime && PayoutDate.UtcDateTime <= endDate;
                return new CardholderTransactions(includeVendorBillPay && inBatch && _paidByCard ? [VendorCardBillPayTransaction()] : []);
            });

        _mockAplosApiClient
            .Setup(client => client.GetFunds(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockAplosApiClient
            .Setup(client => client.GetAccounts(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockAplosApiClient
            .Setup(client => client.GetTags(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockAplosApiClient
            .Setup(client => client.GetTransactions(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _mockAplosIntegrationMappingService
            .Setup(service => service.Map(It.IsAny<IEnumerable<AplosApiFundDetail>>()))
            .Returns([new PexAplosApiObject { Id = FundId.ToString(), Name = "General Fund" }]);
        _mockAplosIntegrationMappingService
            .Setup(service => service.Map(It.IsAny<IEnumerable<AplosApiAccountDetail>>()))
            .Returns([new PexAplosApiObject { Id = ExpenseAccountNumber.ToString(), Name = "Supplies" }]);

        _mockAplosApiClient
            .Setup(client => client.CreateTransaction(It.IsAny<AplosApiTransactionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<AplosApiTransactionDetail, CancellationToken>((aplosTransaction, _) => _createdAplosTransactions.Add(aplosTransaction))
            .ReturnsAsync(() => new AplosApiTransactionResponse
            {
                Data = new AplosApiTransactionData { Transaction = new AplosApiTransactionDetail { Id = 77000 } }
            });
    }

    private TransactionModel VendorCardBillPayTransaction()
    {
        return new TransactionModel
        {
            TransactionId = VendorCardTransactionId,
            AcctId = VendorCardAcctId,
            TransactionAmount = -BillAmount,
            TransactionTime = PayoutDate.UtcDateTime,
            SettlementTime = PayoutDate.UtcDateTime,
            MerchantName = "Acme Supplies",
            TransactionNotes = _cardTransactionNotes.Select(note => new TransactionNoteModel { NoteText = note }).ToList(),
            TransactionTags = new TransactionTagsModel(),
            TransactionType = TransactionType.Network
        };
    }
}
