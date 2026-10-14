using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Services;
using Moq;
using Newtonsoft.Json.Linq;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

// Aplos's pay call drops note and memo, so the payment type goes on the payable's note, which the payment copies.
public class PayableNoteTests : BillPaymentsTestBase
{
    [Fact]
    public async Task ThePaymentTypeIsAppendedToThePayableNoteBeforeItIsPaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: BillAmount, paid: 0m);

        await RunSync(useBillPay: true);

        // A paid payable is locked, so after the pay call there is nothing left to write to.
        Assert.Equal(["note", "pay"], _aplosWrites);
        // PUT replaces the whole payable, so anything else that changed would be the customer's data lost.
        Assert.True(JToken.DeepEquals(PayableForUpdate("Customer memo | [PEXBP:9001] | Paid via ACH"), Assert.Single(_payableUpdates)));
    }

    [Fact]
    public async Task APayableWithNoNoteGetsTheTokenAlone()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: BillAmount, paid: 0m);
        _payableForUpdate = PayableForUpdate(null);

        await RunSync(useBillPay: true);

        Assert.Equal("[PEXBP:9001] | Paid via ACH", Assert.Single(_payableUpdates).Value<string>("note"));
    }

    [Fact]
    public async Task WhenTheNoteCannotBeWrittenThePayableIsStillPaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: BillAmount, paid: 0m);
        _mockAplosApiClient
            .Setup(client => client.UpdatePayable(AplosPayableId, It.IsAny<JObject>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AplosApiException(new AplosApiErrorResponse { Status = 500 }));

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }

    [Fact]
    public async Task ANoteThatWouldPassAplossLimitIsLeftAloneAndThePayableIsStillPaid()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: BillAmount, paid: 0m);
        _payableForUpdate = PayableForUpdate(new string('x', 1980));

        await RunSync(useBillPay: true);

        Assert.Empty(_payableUpdates);
        Assert.Single(_payments);
    }

    [Fact]
    public async Task WhenThePayFailsAfterTheNoteLandedTheRetryDoesNotWriteItAgain()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: BillAmount, paid: 0m);
        var payCalls = 0;
        _mockAplosApiClient
            .Setup(client => client.PayPayable(AplosPayableId, It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++payCalls == 1
                ? throw new AplosApiException(new AplosApiErrorResponse { Status = 500 })
                : new AplosApiPayableResponse { Data = new AplosApiPayableData { Payable = new() { Id = AplosPayableId, Amount = BillAmount, PaidAmount = BillAmount } } });

        await RunSync(useBillPay: true);
        await RunSync(useBillPay: true);

        Assert.Equal(2, payCalls);
        Assert.Single(_payableUpdates);
        Assert.Equal("Customer memo | [PEXBP:9001] | Paid via ACH", _payableForUpdate.Value<string>("note"));
    }

    // A failed ACH payment re-issued by card: the connector's part is replaced, the customer's is kept.
    [Fact]
    public void ARetryOnAnotherRailReplacesOnlyTheConnectorsPart()
        => Assert.Equal(
            "Memo | [PEXBP:9001] | Paid via Card",
            AplosIntegrationService.AppendPaymentTypeToPayableNote(
                "Memo | [PEXBP:9001] | Paid via ACH", "9001", new BillPaymentRequestModel { PayeeFundsDestinationType = PayeeFundsDestinationType.SingleUseVendorVirtualCard }));
}
