using System;

using Prowl.Graphite.Debugging;

using Xunit;

namespace Prowl.Graphite.Tests;

public class ResourceRangeTests
{
    private static ResourceRange Bytes(uint offset, uint size) => ResourceRange.Bytes(offset, size);

    private static ResourceRange Tex(uint mip, uint mips, uint layer, uint layers) => ResourceRange.Subresources(mip, mips, layer, layers);

    [Fact]
    public void Forms_AreDistinguishedAndDefaultIsEmptyBytes()
    {
        Assert.False(Bytes(0, 8).IsTexture);
        Assert.True(Tex(0, 1, 0, 1).IsTexture);
        Assert.True(default(ResourceRange).IsEmpty);
        Assert.False(default(ResourceRange).IsTexture);
        Assert.True(Bytes(4, 0).IsEmpty);
        Assert.True(Tex(0, 0, 0, 3).IsEmpty);
        Assert.False(Bytes(4, 1).IsEmpty);
    }

    [Theory]
    [InlineData(0u, 10u, 5u, 10u, true)]
    [InlineData(0u, 10u, 10u, 5u, false)]
    [InlineData(0u, 10u, 9u, 1u, true)]
    [InlineData(5u, 5u, 0u, 5u, false)]
    [InlineData(0u, 0u, 0u, 10u, false)]
    [InlineData(0u, 10u, 2u, 3u, true)]
    public void Bytes_Overlaps(uint offset, uint size, uint otherOffset, uint otherSize, bool expected)
    {
        Assert.Equal(expected, Bytes(offset, size).Overlaps(Bytes(otherOffset, otherSize)));
        Assert.Equal(expected, Bytes(otherOffset, otherSize).Overlaps(Bytes(offset, size)));
    }

    [Fact]
    public void Texture_OverlapNeedsBothAxes()
    {
        Assert.True(Tex(0, 2, 0, 2).Overlaps(Tex(1, 2, 1, 2)));
        Assert.False(Tex(0, 2, 0, 2).Overlaps(Tex(2, 1, 0, 2)));
        Assert.False(Tex(0, 2, 0, 2).Overlaps(Tex(0, 2, 2, 1)));
        Assert.False(Tex(0, 2, 0, 2).Overlaps(Tex(2, 1, 2, 1)));
    }

    [Fact]
    public void MixedForms_NeverOverlapOrContain()
    {
        Assert.False(Bytes(0, 16).Overlaps(Tex(0, 1, 0, 1)));
        Assert.False(Bytes(0, 16).Contains(Tex(0, 1, 0, 1)));
        Assert.False(Tex(0, 4, 0, 4).Contains(Bytes(0, 1)));
        Assert.True(Bytes(0, 16).Intersect(Tex(0, 1, 0, 1)).IsEmpty);
    }

    [Fact]
    public void Contains_RequiresFullCover()
    {
        Assert.True(Bytes(0, 10).Contains(Bytes(2, 3)));
        Assert.True(Bytes(0, 10).Contains(Bytes(0, 10)));
        Assert.False(Bytes(0, 10).Contains(Bytes(5, 6)));
        Assert.False(Bytes(0, 10).Contains(Bytes(10, 1)));
        Assert.True(Bytes(0, 10).Contains(Bytes(3, 0)));
        Assert.False(Bytes(0, 0).Contains(Bytes(0, 1)));
        Assert.True(Tex(0, 4, 0, 4).Contains(Tex(1, 2, 1, 2)));
        Assert.False(Tex(0, 4, 0, 4).Contains(Tex(3, 2, 0, 1)));
        Assert.False(Tex(0, 4, 0, 4).Contains(Tex(0, 1, 3, 2)));
    }

    [Fact]
    public void Intersect_ReturnsSharedPart()
    {
        Assert.Equal(Bytes(5, 5), Bytes(0, 10).Intersect(Bytes(5, 10)));
        Assert.Equal(Bytes(2, 3), Bytes(0, 10).Intersect(Bytes(2, 3)));
        Assert.Equal(default, Bytes(0, 5).Intersect(Bytes(5, 5)));
        Assert.Equal(Tex(1, 1, 1, 2), Tex(0, 2, 0, 3).Intersect(Tex(1, 3, 1, 4)));
        Assert.Equal(default, Tex(0, 1, 0, 1).Intersect(Tex(1, 1, 0, 1)));
    }

    [Fact]
    public void Union_IsBoundingRange()
    {
        Assert.Equal(Bytes(0, 15), Bytes(0, 5).Union(Bytes(10, 5)));
        Assert.Equal(Bytes(0, 10), Bytes(0, 10).Union(Bytes(2, 3)));
        Assert.Equal(Tex(0, 3, 0, 4), Tex(0, 1, 0, 1).Union(Tex(2, 1, 3, 1)));
    }

    [Fact]
    public void Union_IgnoresEmptyAndRejectsMixedForms()
    {
        Assert.Equal(Bytes(4, 4), default(ResourceRange).Union(Bytes(4, 4)));
        Assert.Equal(Bytes(4, 4), Bytes(4, 4).Union(Bytes(0, 0)));
        Assert.Equal(Tex(1, 1, 1, 1), Tex(1, 1, 1, 1).Union(default));
        Assert.Throws<ArgumentException>(() => Bytes(0, 4).Union(Tex(0, 1, 0, 1)));
    }

    [Fact]
    public void Math_DoesNotOverflowNearUintMax()
    {
        ResourceRange high = Bytes(uint.MaxValue - 4, 4);
        Assert.True(high.Overlaps(Bytes(uint.MaxValue - 2, 2)));
        Assert.False(high.Overlaps(Bytes(0, 10)));
        Assert.True(Bytes(0, uint.MaxValue).Contains(high));
        Assert.Equal(Bytes(uint.MaxValue - 2, 2), high.Intersect(Bytes(uint.MaxValue - 2, 10)));
    }
}
