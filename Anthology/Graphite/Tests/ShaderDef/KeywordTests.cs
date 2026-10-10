using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


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

}
