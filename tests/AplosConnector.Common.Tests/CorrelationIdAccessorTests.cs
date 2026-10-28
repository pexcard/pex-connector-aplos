using AplosConnector.Common.Correlation;
using Xunit;

namespace AplosConnector.Common.Tests;

public class CorrelationIdAccessorTests
{
    private readonly CorrelationIdAccessor _accessor = new(CorrelationConstants.Prefix);

    [Fact]
    public void Generate_ReturnsThePrefixFollowedBy32HexCharacters()
    {
        Assert.Matches("^ext-aplos-[0-9a-f]{32}$", _accessor.Generate());
    }

    [Fact]
    public void Generate_ReturnsANewIdEachCall()
    {
        Assert.NotEqual(_accessor.Generate(), _accessor.Generate());
    }

    [Fact]
    public void GetOrCreate_CachesTheGeneratedIdForLaterCalls()
    {
        Assert.Null(_accessor.Current);

        var id = _accessor.GetOrCreate();

        Assert.Matches("^ext-aplos-[0-9a-f]{32}$", id);
        Assert.Equal(id, _accessor.Current);
        Assert.Equal(id, _accessor.GetOrCreate());
    }

    [Fact]
    public void GetOrCreate_ReturnsTheCurrentId_WhenOneIsSet()
    {
        _accessor.Current = "ext-aplos-from-middleware";

        Assert.Equal("ext-aplos-from-middleware", _accessor.GetOrCreate());
    }
}
