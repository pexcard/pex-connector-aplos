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

public class PexBillPaymentTransactionSyncTests : BillPaymentTransactionSyncTestBase
{
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public async Task PexLeavesVendorCardChargesOutOnlyWhileSyncBillPaymentsIsOn(bool useBillPay, bool syncBillPayments, bool expectedIncludeVendorBillPay)
    {
        await RunBothSyncs(mapping =>
        {
            mapping.UseBillPayEnabled = useBillPay;
            mapping.SyncBillPayments = syncBillPayments;
        });

        Assert.Equal([expectedIncludeVendorBillPay], _includeVendorBillPayArgs.Distinct());
    }

    [Theory]
    [InlineData(PayeeFundsDestinationType.SingleUseVendorVirtualCard, PaymentRequestStatusTrigger.Paid)]
    [InlineData(PayeeFundsDestinationType.VendorVirtualCard, PaymentRequestStatusTrigger.AutoBilledVendorCard)]
    public async Task APexCreatedCardBillIsBookedOnceAsABillPaymentNotAlsoAsAPurchase(PayeeFundsDestinationType fundsDestinationType, PaymentRequestStatusTrigger trigger)
    {
        SetupPaidBill(fundsDestinationType, trigger);

        await RunBothSyncs();

        Assert.Empty(_payments);
        var aplosTransaction = Assert.Single(_createdAplosTransactions);
        Assert.EndsWith($"Bill payment #{PexBillId}", aplosTransaction.Note);
        Assert.Single(aplosTransaction.Lines, line => line.Account.AccountNumber == RegisterAccountNumber && line.Fund.Id == FundId);
    }

    [Fact]
    public async Task APexOriginatedBillPaidByCardIsBookedAsAPurchaseByTheTransactionSync()
    {
        SetupPaidBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);

        await RunBothSyncs(mapping => mapping.SyncBillPayments = false);

        var aplosTransaction = Assert.Single(_createdAplosTransactions);
        Assert.Equal(VendorCardTransactionId.ToString(), aplosTransaction.Note);
        Assert.Single(aplosTransaction.Lines, line => line.Account.AccountNumber == RegisterAccountNumber);
        Assert.DoesNotContain(aplosTransaction.Lines, line => line.Account.AccountNumber == CardClearingAccountNumber);
    }

    [Fact]
    public async Task APexCreatedAchBillIsBookedAgainstTheAchBankAccount()
    {
        SetupPaidBill(PayeeFundsDestinationType.BankAccount);

        await RunBothSyncs();

        Assert.Empty(_payments);
        var aplosTransaction = Assert.Single(_createdAplosTransactions);
        Assert.Single(aplosTransaction.Lines, line => line.Account.AccountNumber == AchClearingAccountNumber);
    }

    [Fact]
    public async Task APexOriginatedBillPaidByAchIsNotSynced()
    {
        SetupPaidBill(PayeeFundsDestinationType.BankAccount);

        await RunBothSyncs(mapping => mapping.SyncBillPayments = false);

        Assert.Empty(_createdAplosTransactions);
    }

    [Fact]
    public async Task AnAutoBilledVendorCardChargeIsBookedAsAPurchaseByTheTransactionSync()
    {
        SetupPaidBill(PayeeFundsDestinationType.VendorVirtualCard, PaymentRequestStatusTrigger.AutoBilledVendorCard);

        await RunBothSyncs(mapping => mapping.SyncBillPayments = false);

        var aplosTransaction = Assert.Single(_createdAplosTransactions);
        Assert.Single(aplosTransaction.Lines, line => line.Account.AccountNumber == RegisterAccountNumber);
        Assert.Empty(_payments);
    }

    [Fact]
    public async Task APostedBillsChargeWhoseNoteWasDeletedInPexIsNotBookedAfterSyncBillPaymentsIsTurnedOff()
    {
        SetupPaidBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        await RunBothSyncs();
        _cardTransactionNotes.Clear();

        await RunBothSyncs(mapping => mapping.SyncBillPayments = false);

        Assert.EndsWith($"Bill payment #{PexBillId}", Assert.Single(_createdAplosTransactions).Note);
    }

    [Fact]
    public async Task AChargeBookedAsAPurchaseWhileSyncBillPaymentsWasOffIsNotPostedAgainOnceItIsOn()
    {
        SetupPaidBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard);
        await RunBothSyncs(mapping => mapping.SyncBillPayments = false);

        await RunBothSyncs();

        Assert.Equal(VendorCardTransactionId.ToString(), Assert.Single(_createdAplosTransactions).Note);
    }

    [Fact]
    public async Task AnAutoBilledChargeCarryingItsBillsSyncedNoteIsNotBookedAsAPurchase()
    {
        SetupPaidBill(PayeeFundsDestinationType.VendorVirtualCard, PaymentRequestStatusTrigger.AutoBilledVendorCard);
        _cardTransactionNotes.Add($"Synced bill payment #{PexBillId} to Aplos on 2026-09-16T12:00:00.0000000Z. Aplos transaction 77000.");

        await RunBothSyncs(mapping => mapping.SyncBillPayments = false);

        Assert.Empty(_createdAplosTransactions);
    }
}
