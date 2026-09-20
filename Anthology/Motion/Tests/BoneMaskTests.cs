namespace Prowl.Motion.Tests;

public class BoneMaskTests
{
    [Fact]
    public void FixedWeight_AppliesToEveryBone()
    {
        var m = new BoneMask(TestSkeletons.MakeChain(), 1f);
        Assert.Equal(1.0, (double)m.GetWeight(0), 5);
        Assert.Equal(1.0, (double)m.GetWeight(2), 5);
    }

    [Fact]
    public void SetWeight_UpdatesSingleBone()
    {
        var m = new BoneMask(TestSkeletons.MakeChain(), 0f);
        m.SetWeight(1, 0.5f);
        Assert.Equal(0.5, (double)m.GetWeight(1), 5);
    }

    [Fact]
    public void CombineWith_MultipliesElementwise()
    {
        var s = TestSkeletons.MakeChain();
        var a = new BoneMask(s, 0.5f);
        a.CombineWith(new BoneMask(s, 0.5f));
        Assert.Equal(0.25, (double)a.GetWeight(0), 5);
    }

    [Fact]
    public void BlendTo_LerpsWeights()
    {
        var s = TestSkeletons.MakeChain();
        var a = new BoneMask(s, 0f);
        a.BlendTo(new BoneMask(s, 1f), 0.5f);
        Assert.Equal(0.5, (double)a.GetWeight(0), 5);
    }

    [Fact]
    public void Length_MatchesBoneCount()
        => Assert.Equal(3, new BoneMask(TestSkeletons.MakeChain()).Length);
}
