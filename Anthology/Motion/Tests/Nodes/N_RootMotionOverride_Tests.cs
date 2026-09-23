using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Root Motion Override node.</summary>
public class N_RootMotionOverride_Tests
{
    private static AnimationClip ForwardMover(Skeleton skeleton, params AnimationEvent[] events)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        var rootFrames = new[] { Transform3D.Identity, new Transform3D(new Float3(0f, 0f, 1f), Quaternion.Identity, Float3.One) };
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f, rootMotion: new RootMotion(rootFrames, 1f), events: events);
    }

    [Fact]
    public void RootMotionEvent_SuspendsOverride()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();

        // With the override (speedScale 0) and NO event: root motion is zeroed.
        var noEvent = new AnimationGraph();
        int c1 = noEvent.AddClip(ForwardMover(skeleton), loop: false);
        noEvent.SetRoot(noEvent.AddRootMotionOverride(c1, speedScale: 0f));
        AnimationGraphInstance a = noEvent.CreateInstance(skeleton);
        a.Update(0.1f);
        Assert.Equal(0.0, (double)a.RootMotionDelta.position.Z, 4);

        // Same override but a RootMotionEvent covers the region: the clip's root motion plays through.
        var withEvent = new AnimationGraph();
        int c2 = withEvent.AddClip(ForwardMover(skeleton, new RootMotionEvent(0f, 0.9f)), loop: false);
        withEvent.SetRoot(withEvent.AddRootMotionOverride(c2, speedScale: 0f));
        AnimationGraphInstance b = withEvent.CreateInstance(skeleton);
        b.Update(0.1f);
        Assert.True(b.RootMotionDelta.position.Z > 0.01f);
    }
}
