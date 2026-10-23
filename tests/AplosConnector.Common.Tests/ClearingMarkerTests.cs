using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Language.Flow;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class ClearingMarkerTests : CardChargeTransferTestBase
{
    [Fact]
    public async Task ARetriedTransferMovesTheChargeOutOfTheAccountTheBillWasPaidFromNotTheCurrentSetting()
    {
        const decimal NewClearingAccountNumber = 1699m;
        _accounts[NewClearingAccountNumber] = ClearingAccount;
        SetupCardBill(CreditCardAccount, paid: BillAmount);
        _billMappingStorage.Rows.Values.Single().PaidFromClearingAccountNumber = CardClearingAccountNumber;

        await RunCardSync(cardClearingAccountNumber: NewClearingAccountNumber);

        var transfer = Assert.Single(_aplosTransactions);
        Assert.Equal(CardClearingAccountNumber, transfer.Lines.Single(line => line.Amount > 0m).Account.AccountNumber);
    }

    // The payable alone can't say who paid it; only the connector's own clearing payment owes the transfer.
    [Fact]
    public async Task ABillPaidByHandInAplosClosesWithNoTransfer()
    {
        SetupCardBill(CreditCardAccount, paid: BillAmount);

        await RunCardSync();

        VerifyNoPayCall();
        Assert.Empty(_aplosTransactions);
        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }

    [Fact]
    public async Task ALostClearingMarkerLeavesThePayableUnpaid()
    {
        SetupCardBill(CreditCardAccount);
        _billMappingStorage.Rows.Values.Single().CardTransactionId = VendorCardTransactionId;
        _billMappingStorage.FailNextWrite = true;

        await RunCardSync();

        VerifyNoPayCall();
        Assert.Empty(_aplosTransactions);
        Assert.Contains("could not record that it pays this bill from the vendor card clearing account", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ARefusedPayClearsTheMarkerSoABillThenPaidByHandGetsNoTransfer()
    {
        SetupCardBill(CreditCardAccount);
        SetupPayCall().ThrowsAsync(new AplosApiException(new AplosApiErrorResponse { Status = 405 }));

        await RunCardSync();

        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidFromClearingAccountNumber);

        SetupLivePayableWithLines(paid: BillAmount, lines: [(BillAmount, FundId)]);
        await RunCardSync();

        Assert.Empty(_aplosTransactions);
        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }

    [Fact]
    public async Task APayCallThatComesBackUnpaidClearsTheMarker()
    {
        SetupCardBill(CreditCardAccount);
        SetupPayCall().ReturnsAsync(new AplosApiPayableResponse
        {
            Data = new AplosApiPayableData { Payable = Payable(BillAmount, 0m, [(BillAmount, FundId)]) }
        });

        await RunCardSync();

        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidFromClearingAccountNumber);
        Assert.Empty(_aplosTransactions);
    }

    [Fact]
    public async Task AFailedPayCallOnABillStillUnpaidClearsTheMarkerSoAHandPaymentGetsNoTransfer()
    {
        SetupCardBill(CreditCardAccount);
        SetupPayCall().ThrowsAsync(new AplosApiException(new AplosApiErrorResponse { Status = 503 }));

        await RunCardSync();

        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidFromClearingAccountNumber);

        SetupLivePayableWithLines(paid: BillAmount, lines: [(BillAmount, FundId)]);
        await RunCardSync();

        Assert.Empty(_aplosTransactions);
    }

    [Fact]
    public async Task AFailedPayCallThatWentThroughKeepsTheMarkerAndTheNextRunPostsTheTransfer()
    {
        SetupCardBill(CreditCardAccount);
        SetupPayableReads(Payable(BillAmount, 0m, [(BillAmount, FundId)]), Payable(BillAmount, BillAmount, [(BillAmount, FundId)]));
        SetupPayCall().ThrowsAsync(new AplosApiException(new AplosApiErrorResponse { Status = 503 }));

        await RunCardSync();

        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidFromClearingAccountNumber);

        SetupLivePayableWithLines(paid: BillAmount, lines: [(BillAmount, FundId)]);
        await RunCardSync();

        Assert.Single(_aplosTransactions);
    }

    [Fact]
    public async Task AnUnpaidPayResponseKeepsTheMarkerWhenTheReReadShowsThePaymentWentThrough()
    {
        SetupCardBill(CreditCardAccount);
        SetupPayableReads(Payable(BillAmount, 0m, [(BillAmount, FundId)]), Payable(BillAmount, BillAmount, [(BillAmount, FundId)]));
        SetupPayCall().ReturnsAsync(new AplosApiPayableResponse
        {
            Data = new AplosApiPayableData { Payable = Payable(BillAmount, 0m, [(BillAmount, FundId)]) }
        });

        await RunCardSync();

        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidFromClearingAccountNumber);
    }

    [Fact]
    public async Task AMarkerLeftByAnEarlierRunIsClearedWhenTheBillReadsUnpaid()
    {
        SetupCardBill(CreditCardAccount, paid: 10m);
        _billMappingStorage.Rows.Values.Single().PaidFromClearingAccountNumber = CardClearingAccountNumber;

        await RunCardSync();

        VerifyNoPayCall();
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidFromClearingAccountNumber);
    }

    [Fact]
    public async Task AStoppedBillThatReadsUnpaidLosesItsMarkerSoAHandPaymentGetsNoTransfer()
    {
        SetupCardBill(CreditCardAccount);
        var row = _billMappingStorage.Rows.Values.Single();
        row.PaidFromClearingAccountNumber = CardClearingAccountNumber;
        row.FirstFailedUtc = UtcNow.AddDays(-40);

        await RunCardSync();

        VerifyNoPayCall();
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidFromClearingAccountNumber);

        SetupLivePayableWithLines(paid: BillAmount, lines: [(BillAmount, FundId)]);
        await RunCardSync();

        Assert.Empty(_aplosTransactions);
        Assert.NotNull(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }
}
