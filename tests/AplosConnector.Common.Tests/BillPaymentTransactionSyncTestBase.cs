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

// Both stages over one fake feed: the bill stage first, then the transaction sync, as Sync runs them.
public abstract class BillPaymentTransactionSyncTestBase : BillPaymentsTestBase
{
    protected const int VendorCardAcctId = 5150;
    protected const decimal ExpenseAccountNumber = 5000m;
    protected const decimal RegisterAccountNumber = 1050m;
    protected const int FundId = 1;

    protected readonly List<AplosApiTransactionDetail> _createdAplosTransactions = [];
    protected const long RefundTransactionId = 987700;
    protected const decimal RefundAmount = 20m;

    protected readonly List<bool> _includeVendorBillPayArgs = [];
    protected bool _paidByCard;
    // Stands in for includeVendorBillPay=false (154868): PEX leaves vendor card charges out but keeps their credits.
    protected bool _feedDropsVendorCardCharges;
    protected bool _refundInFeed;

    protected BillPaymentTransactionSyncTestBase()
    {
        SetupTransactionSyncDefaults();
    }

    protected async Task<Pex2AplosMappingModel> RunBothSyncs(Action<Pex2AplosMappingModel> configureMapping = null)
    {
        var mapping = NewSyncMapping();

        configureMapping?.Invoke(mapping);

        // Sync's order: the marker must be on the charge before the transaction sync reads it.
        var service = GetAplosIntegrationService();
        await service.SyncBillPayments(NullLogger.Instance, mapping, UtcNow, default);
        await service.SyncTransactions(NullLogger.Instance, mapping, UtcNow, default);
        return mapping;
    }

    protected static Pex2AplosMappingModel NewSyncMapping() => new()
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
        SyncOutstandingBills = true,
        SyncBillPayments = true,
        BillPaymentsAchClearingAccountNumber = AchClearingAccountNumber,
        BillPaymentsCardClearingAccountNumber = CardClearingAccountNumber
    };

    protected void SetupPaidBill(PayeeFundsDestinationType fundsDestinationType, PaymentRequestStatusTrigger trigger = PaymentRequestStatusTrigger.Paid)
    {
        // Only a card payment leaves a card transaction behind.
        _paidByCard = fundsDestinationType != PayeeFundsDestinationType.BankAccount;
        SetupPaidPexBill(fundsDestinationType, statusTrigger: trigger);
    }

    protected void SetupTransactionSyncDefaults()
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
                List<TransactionModel> transactions = [];
                if (includeVendorBillPay && inBatch && _paidByCard && !_feedDropsVendorCardCharges) transactions.Add(VendorCardBillPayTransaction());
                if (inBatch && _refundInFeed) transactions.Add(VendorCardRefund());
                return new CardholderTransactions(transactions);
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

    protected TransactionModel VendorCardBillPayTransaction()
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

    protected static TransactionModel VendorCardRefund() => new()
    {
        TransactionId = RefundTransactionId,
        AcctId = VendorCardAcctId,
        TransactionAmount = RefundAmount,
        TransactionTime = PayoutDate.UtcDateTime,
        SettlementTime = PayoutDate.UtcDateTime,
        MerchantName = "Acme Supplies",
        TransactionNotes = [],
        TransactionTags = new TransactionTagsModel(),
        TransactionType = TransactionType.Network
    };
}
