using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Poses: their state, model space transforms and channels.</summary>
public class Pose_Tests
{
    private static Avatar RigWithChannels(params string[] channels)
        => new HumanoidTestRig { FloatChannels = channels }.BuildAvatar();

    [Fact]
    public void Pose_LowLodCache_DoesNotServeStaleHighLodBones()
    {
        var ids = new[] { new StringID("Root"), new StringID("Child") };
        var local = new[] { Transform3D.Identity, new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One) };
        var skeleton = new Skeleton(ids, new[] { Skeleton.InvalidIndex, 0 }, local, numBonesToSampleAtLowLOD: 1);
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.CalculateModelSpaceTransforms();

        pose.SetTransform(0, new Transform3D(new Float3(5f, 0f, 0f), Quaternion.Identity, Float3.One));
        pose.CalculateModelSpaceTransforms(SkeletonLOD.Low);

        Assert.Equal(5.0, (double)pose.GetModelSpaceTransform(1).position.X, 4);
    }

    [Fact]
    public void ModelSpace_DoesNotAllocate()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.CalculateModelSpaceTransforms();

        long lowest = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                pose.CalculateModelSpaceTransforms();
            lowest = Math.Min(lowest, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0, lowest);
    }

    [Fact]
    public void CopyingAPoseWithFewerChannels_ClearsTheRest()
    {
        Avatar with = RigWithChannels("Smile");
        Avatar without = new HumanoidTestRig().BuildAvatar();
        var target = new Pose(with.Skeleton);
        target.SetToReferencePose();
        target.SetFloat(0, 9f);

        target.CopyFrom(HumanoidTestRig.BindPose(without));

        Assert.Equal(0f, target.GetFloat(0), 3);
    }

    [Fact]
    public void NewPose_IsUnset()
        => Assert.Equal(PoseState.Unset, new Pose(TestSkeletons.MakeChain()).State);

    [Fact]
    public void SetToReferencePose_SetsState()
    {
        var p = new Pose(TestSkeletons.MakeChain());
        p.SetToReferencePose();
        Assert.Equal(PoseState.ReferencePose, p.State);
    }

    [Fact]
    public void SetTransform_PromotesToPoseState()
    {
        var p = new Pose(TestSkeletons.MakeChain());
        p.SetTransform(0, Transform3D.Identity);
        Assert.Equal(PoseState.Pose, p.State);
    }

    [Fact]
    public void CalculateModelSpace_AccumulatesDownChain()
    {
        var p = new Pose(TestSkeletons.MakeChain());
        p.SetToReferencePose();
        p.CalculateModelSpaceTransforms();
        Assert.True(p.HasModelSpaceTransforms);
        Assert.Equal(2.0, (double)p.GetModelSpaceTransform(2).position.Y, 4);
    }

    [Fact]
    public void Reset_ReturnsToUnset()
    {
        var p = new Pose(TestSkeletons.MakeChain());
        p.SetToReferencePose();
        p.Reset();
        Assert.Equal(PoseState.Unset, p.State);
    }
}
