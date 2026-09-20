using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class ClipEventSamplingTests
{
    // 3 key frames: bone 0 Z = 0, 10, 20 (frames at normalized 0, 0.5, 1.0).
    private static AnimationClip ThreeStep(Skeleton skeleton, params AnimationEvent[] events)
    {
        Pose Frame(float z)
        {
            var p = new Pose(skeleton); p.SetToReferencePose();
            p.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
            return p;
        }
        return new AnimationClip(skeleton, new[] { Frame(0f), Frame(10f), Frame(20f) }, 1f, events: events);
    }

    private static AnimationClip ForwardMover(Skeleton skeleton, params AnimationEvent[] events)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        var rootFrames = new[] { Transform3D.Identity, new Transform3D(new Float3(0f, 0f, 1f), Quaternion.Identity, Float3.One) };
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f, rootMotion: new RootMotion(rootFrames, 1f), events: events);
    }

    [Fact]
    public void SnapToFrame_FloorsSampleToFrame()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int clip = g.AddClip(ThreeStep(skeleton, new SnapToFrameEvent(FrameSnapMode.Floor, 0f, 1f)));
        g.SetRoot(clip);

        AnimationGraphInstance i = g.CreateInstance(skeleton);
        i.Update(0.7f); // normalized 0.7 -> frame 1.4 -> floor frame 1 -> z = 10 (not interpolated 14)
        Assert.Equal(10.0, (double)i.Pose.GetTransform(0).position.Z, 3);
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
