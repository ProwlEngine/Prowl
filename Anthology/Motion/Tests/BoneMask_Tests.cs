using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Bone masks: weights per bone and per channel, combining, blending and feathering.</summary>
public class BoneMask_Tests
{
    [Fact]
    public void FixedWeight_AppliesToEveryBone()
    {
        var m = new BoneMask(TestSkeletons.MakeChain(), 1f);
        Assert.Equal(1.0, (double)m.GetWeight(0), 5);
        Assert.Equal(1.0, (double)m.GetWeight(2), 5);
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
    public void BoneMask_BonesNoSeedReachesTakeTheRestWeight()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        StringID leaf = skeleton.GetBoneID(skeleton.BoneCount - 1);

        BoneMask mask = BoneMask.CreateHierarchical(skeleton, new[] { (skeleton.BoneCount - 1, 1f) }, restWeight: 0.25f);

        Assert.Equal(0.25f, mask.GetWeight(0), 4);
        Assert.Equal(1f, mask.GetWeight(skeleton.GetBoneIndex(leaf)), 4);
    }

    [Fact]
    public void Feathering_PropagatesWeightToDescendants()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var rig = avatar.Humanoid!;

        int spine = rig.GetSkeletonBoneIndex(HumanBodyBone.Spine);
        var mask = BoneMask.CreateHierarchical(skeleton, new[] { (spine, 1f) });

        int head = rig.GetSkeletonBoneIndex(HumanBodyBone.Head);     // descendant of spine
        int hips = rig.GetSkeletonBoneIndex(HumanBodyBone.Hips);     // ancestor of spine
        int foot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot); // not under spine

        Assert.Equal(1.0, (double)mask.GetWeight(spine), 4);
        Assert.Equal(1.0, (double)mask.GetWeight(head), 4);
        Assert.Equal(0.0, (double)mask.GetWeight(hips), 4);
        Assert.Equal(0.0, (double)mask.GetWeight(foot), 4);
    }
}
