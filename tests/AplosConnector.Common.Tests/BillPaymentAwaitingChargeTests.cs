using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Const;
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

public class BillPaymentAwaitingChargeTests : BillPaymentsTestBase
{
    private const string ImportNote = "with ID #100 on 2026-09-02T00:00:00.0000000Z.";

    [Fact]
    public async Task ACardPaymentWaitingForItsChargeStartsTheWaitWithoutReportingIt()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, hasPayoutDate: false, linkedCardCharge: false);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).AwaitingChargeSinceUtc);
        Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(_historyRows).SyncStatus);
    }

    [Fact]
    public async Task AWaitOf30DaysKeepsItsStartAndIsNotReported()
    {
        var awaitingSinceUtc = UtcNow.AddDays(-30);
        SeedUnpaidMapping(awaitingChargeSinceUtc: awaitingSinceUtc);
        SetupPaidPexBill(PayeeFundsDestinationType.VendorVirtualCard, hasPayoutDate: false, linkedCardCharge: false);

        await RunSync(useBillPay: true);

        _mockAplosApiClient.Verify(client => client.GetPayable(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(awaitingSinceUtc, Assert.Single(_billMappingStorage.Rows.Values).AwaitingChargeSinceUtc);
        Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(_historyRows).SyncStatus);
    }

    [Fact]
    public async Task ACardPaymentWithNoChargeAfter30DaysTellsTheCustomerWhatToCheck()
    {
        SeedUnpaidMapping(awaitingChargeSinceUtc: UtcNow.AddDays(-31));
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, hasPayoutDate: false, linkedCardCharge: false);
        SetupLivePayable(BillAmount, paid: 0m);

        await RunSync(useBillPay: true);

        VerifyNoPayCall();
        var history = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Failed.ToString(), history.SyncStatus);
        Assert.Contains("INV-9001: PEX sent the vendor a card for this bill at least 31 days ago", history.SyncNotes);
        Assert.Contains("mark the bill paid in Aplos", history.SyncNotes);
    }

    [Fact]
    public async Task ALongWaitIsNotReportedOnceTheBillIsPaidInAplos()
    {
        SeedUnpaidMapping(awaitingChargeSinceUtc: UtcNow.AddDays(-31));
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, hasPayoutDate: false, linkedCardCharge: false);
        SetupLivePayable(BillAmount, paid: BillAmount);

        await RunSync(useBillPay: true);

        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
        Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(_historyRows).SyncStatus);
    }

    [Fact]
    public async Task ALongWaitIsStillReportedWhenAplosCannotBeRead()
    {
        SeedUnpaidMapping(awaitingChargeSinceUtc: UtcNow.AddDays(-31));
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, hasPayoutDate: false, linkedCardCharge: false);
        _mockAplosApiClient
            .Setup(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Aplos is unavailable."));

        await RunSync(useBillPay: true);

        Assert.Contains("PEX sent the vendor a card for this bill at least 31 days ago", Assert.Single(_historyRows).SyncNotes);
    }

    [Fact]
    public async Task ABillPaidOutsidePexIsNotTreatedAsWaitingForACharge()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, hasPayoutDate: false, linkedCardCharge: false,
            statusTrigger: PaymentRequestStatusTrigger.PaidViaExternalMethod);

        await RunSync(useBillPay: true);

        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).AwaitingChargeSinceUtc);
    }

    [Fact]
    public async Task AnAplosImportWithNoMappingRowIsNotFlaggedWhileItWaitsForItsCharge()
    {
        _billMappingStorage.Seed(new AplosBillMappingModel { PEXBusinessAcctId = PexBusinessAcctId, AplosPayableId = "8001", PexBillInboxId = 999, MetadataRelationId = 999, Amount = 10m });
        SetupPaidPexBill(PayeeFundsDestinationType.SingleUseVendorVirtualCard, hasPayoutDate: false, linkedCardCharge: false,
            metadata: new PaymentRequestMetadataModel
            {
                Notes = [new TransactionNoteModel { NoteText = $"{AplosIntegrationService.GetAplosBillSyncedNote(AplosPayableId)} {ImportNote}" }]
            });

        await RunSync(useBillPay: true);

        Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(_historyRows).SyncStatus);
    }
}
