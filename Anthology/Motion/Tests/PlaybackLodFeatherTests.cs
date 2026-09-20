using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class PlaybackTests
{
    [Fact]
    public void NormalizeLoop_Wraps()
        => Assert.Equal(0.5, (double)ClipPlayback.NormalizeLoop(1.5f, 1f), 4);

    [Fact]
    public void NormalizeClamp_Holds()
        => Assert.Equal(1.0, (double)ClipPlayback.NormalizeClamp(1.5f, 1f), 4);

    [Fact]
    public void CrossedLoop_DetectsWrap()
    {
        Assert.True(ClipPlayback.CrossedLoop(0.95f, 0.05f));
        Assert.False(ClipPlayback.CrossedLoop(0.2f, 0.4f));
    }

    [Fact]
    public void RootMotionApply_MovesByDelta()
    {
        var world = new Transform3D(new Float3(1f, 0f, 0f), Quaternion.Identity, Float3.One);
        var delta = new Transform3D(new Float3(0f, 0f, 2f), Quaternion.Identity, Float3.One);
        Transform3D moved = RootMotionUtil.Apply(world, delta);
        Assert.Equal(1.0, (double)moved.position.X, 4);
        Assert.Equal(2.0, (double)moved.position.Z, 4);
    }
}

public class LodAndFeatherTests
{
    [Fact]
    public void LowLod_ComputesLeadingBones()
    {
        var ids = new[] { new StringID("Root"), new StringID("Hip"), new StringID("Knee") };
        var parents = new[] { Skeleton.InvalidIndex, 0, 1 };
        var local = new[]
        {
            new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
        };
        var skeleton = new Skeleton(ids, parents, local, numBonesToSampleAtLowLOD: 2);
        Assert.Equal(2, skeleton.GetBoneCount(SkeletonLOD.Low));

        var pose = new Pose(skeleton);
        pose.SetToReferencePose(false);
        pose.CalculateModelSpaceTransforms(SkeletonLOD.Low);

        Assert.Equal(1.0, (double)pose.GetModelSpaceTransform(1).position.Y, 4); // hip model Y
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
