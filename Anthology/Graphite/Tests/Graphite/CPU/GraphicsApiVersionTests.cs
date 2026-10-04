using Xunit;

namespace Prowl.Graphite.Tests;

public class GraphicsApiVersionTests
{
    [Fact]
    public void IsKnown_FalseOnlyWhenAllPartsAreZero()
    {
        Assert.False(GraphicsApiVersion.Unknown.IsKnown);
        Assert.True(new GraphicsApiVersion(1, 0, 0, 0).IsKnown);
        Assert.True(new GraphicsApiVersion(1, 3, 0, 2).IsKnown);
    }
}
