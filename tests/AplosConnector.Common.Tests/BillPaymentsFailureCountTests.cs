using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Response;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using Moq;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class BillPaymentsFailureCountTests : BillPaymentsTestBase
{
    [Fact]
    public async Task ABillMarkedPaidWritesNoNotes()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(BillAmount, paid: 0m);

        await RunSync(useBillPay: true);

        var row = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
        Assert.Equal(string.Empty, row.SyncNotes);
    }

    [Fact]
    public async Task OneFailedBillIsCountedBeforeItsReason()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(BillAmount, paid: 0m);
        _mockAplosApiClient
            .Setup(client => client.PayPayable(It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AplosApiException(new AplosApiErrorResponse { Status = 503 }));

        await RunSync(useBillPay: true);

        Assert.StartsWith("1 bill payment needs attention. Bill INV-9001: ", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task MissingAccountsCountEveryWaitingBill()
    {
        SeedUnpaidMapping();
        _billMappingStorage.Seed(new AplosBillMappingModel
        {
            CreatedUtc = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc),
            PEXBusinessAcctId = PexBusinessAcctId,
            AplosPayableId = "9002",
            AplosReferenceNumber = "INV-9002",
            PexBillInboxId = 101,
            MetadataRelationId = 101,
            Amount = 40m
        });

        await RunSync(useBillPay: true, achClearingAccountNumber: 0m, pexFundsAccountNumber: 0m);

        var notes = Assert.Single(_historyRows).SyncNotes;
        Assert.StartsWith("2 bill payments need attention. ", notes);
        Assert.Contains("INV-9001", notes);
        Assert.Contains("INV-9002", notes);
    }

    [Fact]
    public async Task AFailedRunWithNoBillToBlameFailsWithoutABillCount()
    {
        SeedUnpaidMapping();
        _mockPexApiClient
            .Setup(client => client.GetBillPayments(It.IsAny<string>(), It.IsAny<BillPaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("PEX is unavailable."));

        await RunSync(useBillPay: true);

        var row = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), row.SyncStatus);
        Assert.Contains("PEX is unavailable.", row.SyncNotes);
        Assert.DoesNotContain("attention", row.SyncNotes);
    }

    [Fact]
    public async Task ARunThatFailsAfterABillFailedCountsOnlyTheBill()
    {
        const int UnreadableBillId = PexBillId + 1;
        SeedUnpaidMapping();
        SetupPaidPexBill();
        _mockPexApiClient
            .Setup(client => client.GetBillPayments(It.IsAny<string>(), It.IsAny<BillPaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillPaymentListResponseModel
            {
                Items =
                [
                    new BillPaymentModel { BillId = PexBillId, BillRefNo = "INV-9001", PaymentRequestStatus = PaymentRequestStatus.Closed },
                    new BillPaymentModel { BillId = UnreadableBillId, BillRefNo = "INV-9002", PaymentRequestStatus = PaymentRequestStatus.Closed }
                ],
                PageInfo = new PageInfoModel { Page = 1, PageSize = 50, TotalItems = 2 }
            });
        _mockPexApiClient
            .Setup(client => client.GetBillPaymentRequest(It.IsAny<string>(), UnreadableBillId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PEX unavailable"));
        _mockPexApiClient
            .Setup(client => client.GetPayments(It.IsAny<string>(), It.IsAny<PaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("PEX payments are unavailable."));

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        var row = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), row.SyncStatus);
        Assert.StartsWith("1 bill payment needs attention. Bill INV-9002: ", row.SyncNotes);
        Assert.EndsWith("PEX payments are unavailable.", row.SyncNotes);
    }
}
