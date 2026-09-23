using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Target Point node: world and bone targets resolved in character space.</summary>
public class N_TargetPoint_Tests
{
    private static AnimationClip RootAt(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    [Fact]
    public void BoneTarget_ResolvesAgainstPreviousPose()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int clip = g.AddClip(RootAt(skeleton, 7f)); // root bone ends up at model-space Z = 7
        int targetParam = g.AddTargetParameter("T");
        int point = g.AddTargetPoint(targetParam);
        int distance = g.AddTargetInfo(targetParam, TargetInfo.Distance);
        g.SetRoot(clip);

        AnimationGraphInstance i = g.CreateInstance(skeleton);
        i.SetTarget("T", Target.FromBone(0)); // track the root bone

        i.Update(0.016f); // produces the pose and snapshots it as the previous pose

        // The bone target now resolves against that pose (previously it yielded 0 with no pose).
        Assert.Equal(7.0, (double)i.EvaluateValueNode(point).Vector.Z, 3);
        Assert.Equal(7.0, (double)i.EvaluateValueNode(distance).AsFloat(), 3);
    }

    [Fact]
    public void WorldTarget_StillResolvesDirectly()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int targetParam = g.AddTargetParameter("T");
        int point = g.AddTargetPoint(targetParam);
        g.SetRoot(g.AddReferencePose());

        AnimationGraphInstance i = g.CreateInstance(skeleton);
        i.SetTarget("T", Target.FromWorld(new Transform3D(new Float3(2f, 0f, 0f), Quaternion.Identity, Float3.One)));
        Assert.Equal(2.0, (double)i.EvaluateValueNode(point).Vector.X, 3);
    }

    [Fact]
    public void WorldTarget_OnARotatedNonUniformlyScaledCharacter_ResolvesExactly()
    {
        var g = new AnimationGraph();
        int target = g.AddTargetParameter("T");
        int point = g.AddTargetPoint(target);
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        var world = new Transform3D(new Float3(3f, 0f, -2f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.7f), new Float3(2f, 1f, 0.5f));
        var local = new Float3(1f, 2f, 3f);
        instance.SetTarget("T", Target.FromWorld(new Transform3D(world.TransformPoint(local), Quaternion.Identity, Float3.One)));
        instance.Update(0.1f, world);

        Float3 resolved = instance.EvaluateValueNode(point).Vector;
        Assert.Equal(1.0, (double)resolved.X, 3);
        Assert.Equal(2.0, (double)resolved.Y, 3);
        Assert.Equal(3.0, (double)resolved.Z, 3);
    }

    [Fact]
    public void TargetPoint_IsWhereAWorldTargetStands()
    {
        var g = new AnimationGraph();
        int point = g.AddTargetPoint(g.AddTargetParameter("T"));
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        instance.SetTarget("T", Target.FromWorld(new Transform3D(new Float3(0f, 0f, 4f), Quaternion.Identity, Float3.One)));
        Assert.Equal(4f, instance.EvaluateValueNode(point).Vector.Z, 3);
    }
}
