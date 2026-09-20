using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class BoneTargetResolutionTests
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
}
