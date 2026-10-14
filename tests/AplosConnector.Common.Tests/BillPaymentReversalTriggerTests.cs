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

// Which PEX request states count as a reversal, and that an unsettled one never pays the Aplos payable.
public class BillPaymentReversalTriggerTests : BillPaymentReversalTestBase
{
    // An ACH request keeps the PayoutDate it got at creation even when it later fails, so the date alone must
    // never be enough to pay the Aplos payable: only a Closed request has settled, and Aplos cannot void a pay.
    [Theory]
    [InlineData(PaymentRequestStatusTrigger.PaymentTransferFailed)]
    [InlineData(PaymentRequestStatusTrigger.Rejected)]
    public async Task AnUnsettledRequestWithAPayoutDateNeverPaysTheAplosPayable(PaymentRequestStatusTrigger trigger)
    {
        SeedUnpaidMapping();
        SetupPexBill(PaymentRequestStatus.PaymentException, trigger);

        await RunBillPaymentsSync();

        Assert.Empty(_payments);
        Assert.Null(Row().PaidSyncedUtc);
        _mockAplosApiClient.Verify(client => client.PayPayable(It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Transfer failures happen before payout and Closed has no exits (PEX-confirmed), so those can never follow a
    // payment that reached Aplos.
    [Theory]
    [InlineData(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentFailed, true)]
    [InlineData(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentProcessingException, true)]
    [InlineData(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentCancelled, true)]
    [InlineData(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.Cancelled, true)]
    [InlineData(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.Rejected, true)]
    [InlineData(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.Paid, false)]
    [InlineData(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.PaidViaExternalMethod, false)]
    [InlineData(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.PaymentCancelled, false)]
    [InlineData(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.PaymentTransferFailed, false)]
    [InlineData(PaymentRequestStatus.Closed, PaymentRequestStatusTrigger.PaymentTransferProcessingException, false)]
    [InlineData(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentTransferFailed, false)]
    [InlineData(PaymentRequestStatus.PaymentException, PaymentRequestStatusTrigger.PaymentTransferProcessingException, false)]
    public void AReversalIsRecognisedFromTheRequestStatusRatherThanThePayoutDate(
        PaymentRequestStatus status,
        PaymentRequestStatusTrigger trigger,
        bool expected)
    {
        var paymentRequest = new BillPaymentRequestModel
        {
            PaymentRequestId = PexBillId,
            PaymentRequestStatus = status,
            PaymentRequestStatusTrigger = trigger,
            PayoutDate = PayoutDate
        };

        Assert.Equal(expected, AplosIntegrationService.IsBillPaymentReversed(paymentRequest));
    }
}
