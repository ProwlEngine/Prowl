namespace Prowl.Motion.Tests;

public class SkeletonTests
{
    [Fact]
    public void BoneCount_MatchesInput()
        => Assert.Equal(3, TestSkeletons.MakeChain().BoneCount);

    [Fact]
    public void GetParentBoneIndex_ReturnsDirectParent()
    {
        var s = TestSkeletons.MakeChain();
        Assert.Equal(0, s.GetParentBoneIndex(1));
        Assert.Equal(Skeleton.InvalidIndex, s.GetParentBoneIndex(0));
    }

    [Fact]
    public void IsChildBoneOf_WalksFullHierarchy()
    {
        var s = TestSkeletons.MakeChain();
        Assert.True(s.IsChildBoneOf(0, 2));   // Knee is a descendant of Root
        Assert.False(s.IsChildBoneOf(2, 0));
    }

    [Fact]
    public void GetBoneIndex_FindsByIdOrReturnsInvalid()
    {
        var s = TestSkeletons.MakeChain();
        Assert.Equal(2, s.GetBoneIndex(new StringID("Knee")));
        Assert.Equal(Skeleton.InvalidIndex, s.GetBoneIndex(new StringID("DoesNotExist")));
    }

    [Fact]
    public void IsLeafBone_TrueForTipOnly()
    {
        var s = TestSkeletons.MakeChain();
        Assert.True(s.IsLeafBone(2));
        Assert.False(s.IsLeafBone(0));
    }

    [Fact]
    public void ModelSpaceReferencePose_AccumulatesDownChain()
    {
        var s = TestSkeletons.MakeChain();
        // Root(0) + Hip(+1) + Knee(+1) along Y => Knee at Y = 2 in model space.
        Assert.Equal(2.0, (double)s.GetBoneModelSpaceTransform(2).position.Y, 4);
    }
}
