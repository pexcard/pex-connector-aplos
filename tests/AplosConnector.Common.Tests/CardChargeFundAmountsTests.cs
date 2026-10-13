using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Services;
using System.Linq;
using Xunit;

namespace AplosConnector.Common.Tests;

public class CardChargeFundAmountsTests
{
    [Fact]
    public void ABillWhoseLinesAreAllNegativeSplitsLikeAPositiveOne()
    {
        var fundAmounts = AplosIntegrationService.GetCardChargeFundAmounts(Payable(-125.50m, (-100.50m, 1), (-25m, 2)));

        Assert.Equal([(1, 100.50m), (2, 25m)], fundAmounts);
    }

    [Fact]
    public void AFundThatNetsToZeroGetsNoTransferLines()
    {
        var fundAmounts = AplosIntegrationService.GetCardChargeFundAmounts(Payable(125.50m, (125.50m, 1), (10m, 2), (-10m, 2)));

        Assert.Equal([(1, 125.50m)], fundAmounts);
    }

    [Fact]
    public void LinesThatDontAddUpToTheBillGiveNoSplit()
    {
        Assert.Null(AplosIntegrationService.GetCardChargeFundAmounts(Payable(125.50m, (100m, 1))));
    }

    private static AplosApiPayableDetail Payable(decimal amount, params (decimal Amount, int FundId)[] lines) => new()
    {
        Amount = amount,
        Lines = [.. lines.Select(line => new AplosApiTransactionLineDetail { Amount = line.Amount, Fund = new AplosApiFundDetail { Id = line.FundId } })]
    };
}
