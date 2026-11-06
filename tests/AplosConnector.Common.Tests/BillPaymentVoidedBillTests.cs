using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using Moq;
using PexCard.Api.Client.Core.Enums;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class BillPaymentVoidedBillTests : BillPaymentsTestBase
{
    private const string VoidedNote = "Bill INV-9001: Aplos shows this bill voided, so the PEX payment of 125.50 was not recorded. Record the payment in Aplos or reverse it in PEX.";

    [Theory]
    [InlineData(PayeeFundsDestinationType.BankAccount)]
    [InlineData(PayeeFundsDestinationType.SingleUseVendorVirtualCard)]
    public async Task ABillMissingFromThePayablesListIsNotRecordedInAplos(PayeeFundsDestinationType rail)
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(rail);
        SetupLivePayable(BillAmount, paid: 0m);
        SetupListedPayables();

        await RunSync(useBillPay: true);

        Assert.Empty(_aplosWrites);
        _mockAplosApiClient.Verify(client => client.CreateTransaction(It.IsAny<AplosApiTransactionDetail>(), It.IsAny<CancellationToken>()), Times.Never);
        var notes = Assert.Single(_historyRows).SyncNotes;
        Assert.Contains(VoidedNote, notes);
        Assert.DoesNotContain("Retried until", notes);
        var row = Assert.Single(_billMappingStorage.Rows.Values);
        Assert.Equal(UtcNow, row.FirstFailedUtc);
        Assert.Null(row.PaidSyncedUtc);
    }

    [Fact]
    public async Task TheListIsQueriedByTheLivePayablesBillDateAndVendor()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupListedPayables();
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
                        Amount = BillAmount,
                        BillDate = new DateTime(2026, 9, 5),
                        Contact = new AplosApiContactDetail { Id = 77 }
                    }
                }
            });
        _mockAplosApiClient
            .Setup(client => client.GetPayables(new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 5), 77, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AplosApiPayableDetail { Id = "8001" }, new AplosApiPayableDetail { Id = AplosPayableId }]);

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
    }

    [Fact]
    public async Task AFailedListReadIsRetriedAndNeverTakenAsListed()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(BillAmount, paid: 0m);
        _mockAplosApiClient
            .Setup(client => client.GetPayables(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AplosApiException(new AplosApiErrorResponse { Status = 500 }));

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Contains("Retried until", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ABillBackOnTheListIsPaidOnTheNextSync()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(BillAmount, paid: 0m);
        _mockAplosApiClient
            .SetupSequence(client => client.GetPayables(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([new AplosApiPayableDetail { Id = AplosPayableId }]);

        await RunSync(useBillPay: true);
        Assert.Empty(_payments);
        await RunSync(useBillPay: true);

        Assert.Single(_payments);
    }

    [Fact]
    public async Task ABillAlreadyPaidInAplosNeverQueriesTheList()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(BillAmount, paid: BillAmount);

        await RunSync(useBillPay: true);

        _mockAplosApiClient.Verify(client => client.GetPayables(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }

    // Lists other bills only; a narrower setup made after this one wins in Moq.
    private void SetupListedPayables()
    {
        _mockAplosApiClient
            .Setup(client => client.GetPayables(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AplosApiPayableDetail { Id = "8001" }]);
    }
}
