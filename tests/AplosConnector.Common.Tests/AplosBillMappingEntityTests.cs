using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using Xunit;

namespace AplosConnector.Common.Tests;

public class AplosBillMappingEntityTests
{
    [Fact]
    public void AClaimForABillCreatedInPexIsKeyedByItsPaymentRequestAndRoundTrips()
    {
        var entity = new AplosBillMappingEntity(new AplosBillMappingModel { PEXBusinessAcctId = 6118231, PexPaymentRequestId = 41001, CardTransactionId = 987654 });

        Assert.Equal(("6118231", "pex-41001"), (entity.PartitionKey, entity.RowKey));

        var rebuilt = entity.ToModel();
        Assert.Equal((41001, 987654L, (string)null), (rebuilt.PexPaymentRequestId, rebuilt.CardTransactionId, rebuilt.AplosPayableId));
    }
}
