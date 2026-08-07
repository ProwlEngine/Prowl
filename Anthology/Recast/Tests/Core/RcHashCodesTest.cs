
namespace Prowl.Recast.Core.Tests;

public class RcHashCodesTest
{
    [Fact]
    public void TestCombineHashCodes()
    {
        Assert.Equal(0, RcHashCodes.CombineHashCodes(0, 0));
        Assert.Equal(32, RcHashCodes.CombineHashCodes(int.MaxValue, int.MaxValue));
        Assert.Equal(-33, RcHashCodes.CombineHashCodes(int.MaxValue, int.MinValue));
        Assert.Equal(0, RcHashCodes.CombineHashCodes(int.MinValue, int.MinValue));
        Assert.Equal(-1, RcHashCodes.CombineHashCodes(int.MinValue, int.MaxValue));
        Assert.Equal(32, RcHashCodes.CombineHashCodes(int.MaxValue / 2, int.MaxValue / 2));
    }

    [Fact]
    public void TestIntHash()
    {
        Assert.Equal(4158654902u, RcHashCodes.WangHash(0));
        Assert.Equal(357654460u, RcHashCodes.WangHash(1));
        Assert.Equal(715307540u, RcHashCodes.WangHash(2));
        Assert.Equal(1072960876u, RcHashCodes.WangHash(3));

        Assert.Equal(1430614333u, RcHashCodes.WangHash(4));
        Assert.Equal(1788267159u, RcHashCodes.WangHash(5));
        Assert.Equal(2145921005u, RcHashCodes.WangHash(6));
        Assert.Equal(2503556531u, RcHashCodes.WangHash(7));

        Assert.Equal(2861226262u, RcHashCodes.WangHash(8));
        Assert.Equal(3218863982u, RcHashCodes.WangHash(9));
        Assert.Equal(3576533554u, RcHashCodes.WangHash(10));
        Assert.Equal(3934169234u, RcHashCodes.WangHash(11));

        //
        Assert.Equal(1755403298u, RcHashCodes.WangHash(int.MaxValue));
        Assert.Equal(3971045735u, RcHashCodes.WangHash(uint.MaxValue));
    }
}