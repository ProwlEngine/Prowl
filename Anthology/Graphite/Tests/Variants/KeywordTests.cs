using Xunit;

namespace Prowl.Graphite.Variants.Tests;


public class KeywordTests
{
    [Fact]
    public void SameNameAndValue_AreEqual()
    {
        Keyword a = new("LIGHTING", "ON");
        Keyword b = new("LIGHTING", "ON");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(a.LongHash(), b.LongHash());
    }


    [Fact]
    public void DifferentValue_AreNotEqual()
    {
        Keyword a = new("LIGHTING", "ON");
        Keyword b = new("LIGHTING", "OFF");

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }


    [Fact]
    public void Interning_SameNameSharesNameId_DifferentValueDiffersValueId()
    {
        Keyword a = new("MODE", "A");
        Keyword b = new("MODE", "B");

        Assert.Equal(a.NameId, b.NameId);
        Assert.NotEqual(a.ValueId, b.ValueId);
    }


    [Fact]
    public void Interning_SameStringSharesId()
    {
        Keyword a = new("X", "SHARED");
        Keyword b = new("Y", "SHARED");

        Assert.Equal(a.ValueId, b.ValueId);
    }
}
