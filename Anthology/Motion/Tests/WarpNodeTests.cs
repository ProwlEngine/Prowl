using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class WarpNodeTests
{
    private static AnimationClip ForwardMover(Skeleton skeleton, float distance)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        var rootFrames = new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 0f, distance), Quaternion.Identity, Float3.One),
        };
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f, rootMotion: new RootMotion(rootFrames, 1f));
    }

    // Applies each delta in the frame of the accumulated root, so rotation in the deltas steers the travel.
    private static Float3 AccumulateRootMotion(AnimationGraphInstance instance, int steps, float dt)
    {
        Float3 position = default;
        Quaternion rotation = Quaternion.Identity;
        for (int i = 0; i < steps; i++)
        {
            instance.Update(dt);
            position += rotation * instance.RootMotionDelta.position;
            rotation *= instance.RootMotionDelta.rotation;
        }
        return position;
    }

    [Fact]
    public void OrientationWarp_RotatesTravelDirectionToTarget()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int dir = graph.AddVectorParameter("Dir", new Float3(1f, 0f, 0f)); // want to travel +X
        int clip = graph.AddClip(ForwardMover(skeleton, 1f), loop: false);  // clip travels +Z by 1
        int warp = graph.AddOrientationWarp(clip, dir);
        graph.SetRoot(warp);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        Float3 total = AccumulateRootMotion(instance, 12, 0.1f);

        Assert.Equal(1.0, (double)total.X, 2); // re-headed onto +X
        Assert.Equal(0.0, (double)total.Z, 2);
    }

    [Fact]
    public void TargetWarp_ReachesDesiredDisplacement()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int goal = graph.AddVectorParameter("Goal", new Float3(2f, 0f, 0f)); // end up 2 along +X
        int clip = graph.AddClip(ForwardMover(skeleton, 2f), loop: false);    // clip travels +Z by 2
        int warp = graph.AddTargetWarp(clip, goal);
        graph.SetRoot(warp);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        Float3 total = AccumulateRootMotion(instance, 12, 0.1f);

        Assert.Equal(2.0, (double)total.X, 2);
        Assert.Equal(0.0, (double)total.Z, 2);
    }
}
