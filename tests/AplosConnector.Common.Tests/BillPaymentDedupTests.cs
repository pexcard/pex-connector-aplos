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

public class BillPaymentDedupTests : BillPaymentsTestBase
{
    [Fact]
    public async Task AnAuditNoteFailureAfterAPaymentStillCountsTheBillAsSynced()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);
        _mockPexApiClient
            .Setup(client => client.AddTransactionRelationshipNote(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PEX unavailable"));

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        var historyRow = Assert.Single(_historyRows);
        Assert.Equal(SyncStatus.Success.ToString(), historyRow.SyncStatus);
        Assert.Equal(1, historyRow.SyncedRecords);
    }

    [Fact]
    public async Task RunningTheSyncTwiceOverTheSamePaidBillPostsExactlyOneAplosTransaction()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);

        await RunSync(useBillPay: true);
        await RunSync(useBillPay: true);

        var payment = Assert.Single(_payments);
        Assert.Equal(CashAccountNumber, payment.CashAccountNumber);
        _mockAplosApiClient.Verify(client => client.PayPayable(
            It.IsAny<string>(), It.IsAny<AplosApiPayablePaymentModel>(), It.IsAny<CancellationToken>()), Times.Once);

        var row = Assert.Single(_billMappingStorage.Rows.Values);
        Assert.NotNull(row.PaidSyncedUtc);

        _mockAplosApiClient.Verify(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ALostPaidWriteCannotCauseASecondPaymentAndIsHealedOnTheNextRun()
    {
        SeedUnpaidMapping();
        SetupPaidPexBill();
        SetupLivePayable(amount: 125.50m, paid: 0m);
        _billMappingStorage.FailNextWrite = true;

        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        Assert.Null(Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);

        SetupLivePayable(amount: 125.50m, paid: 125.50m);
        await RunSync(useBillPay: true);

        Assert.Single(_payments);
        _mockAplosApiClient.Verify(client => client.GetPayable(AplosPayableId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(UtcNow, Assert.Single(_billMappingStorage.Rows.Values).PaidSyncedUtc);
    }
}
