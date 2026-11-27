
namespace Prowl.Recast.Core.Tests;

public class RcMathTest
{
    [Fact]
    public void TestSqr()
    {
        Assert.Equal(0, RcMath.Sqr(0));
        Assert.Equal(25, RcMath.Sqr(5));
        Assert.Equal(25, RcMath.Sqr(-5));
        Assert.Equal(float.PositiveInfinity, RcMath.Sqr(float.PositiveInfinity));
        Assert.Equal(float.PositiveInfinity, RcMath.Sqr(float.NegativeInfinity));
        Assert.Equal(float.NaN, RcMath.Sqr(float.NaN));
    }

    [Fact]
    public void TestLerp()
    {
        //
        Assert.Equal(30, RcMath.Lerp(-10, 10, 2f));
        Assert.Equal(10, RcMath.Lerp(-10, 10, 1f));
        Assert.Equal(0, RcMath.Lerp(-10, 10, 0.5f));
        Assert.Equal(-5, RcMath.Lerp(-10, 10, 0.25f));
        Assert.Equal(-10, RcMath.Lerp(-10, 10, 0));
        Assert.Equal(-20, RcMath.Lerp(-10, 10, -0.5f));
        Assert.Equal(-30, RcMath.Lerp(-10, 10, -1f));

        //
        Assert.Equal(10, RcMath.Lerp(10, 10, 0.5f));
        Assert.Equal(10, RcMath.Lerp(10, 10, 0.8f));

        //
        Assert.Equal(-5, RcMath.Lerp(10, -10, 0.75f));
    }
}