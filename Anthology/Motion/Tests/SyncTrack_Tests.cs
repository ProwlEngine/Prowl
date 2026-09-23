using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Sync tracks: phase across clips of different timing, and synchronized blending.</summary>
public class SyncTrack_Tests
{
    // A clip whose bone-0 Z ramps 0..10 across the whole clip, with a second sync event at the given time.
    private static AnimationClip RampClip(Skeleton skeleton, float secondEventTime)
    {
        var f0 = new Pose(skeleton); f0.SetToReferencePose();
        f0.SetTransform(0, new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One));
        var f1 = new Pose(skeleton); f1.SetToReferencePose();
        f1.SetTransform(0, new Transform3D(new Float3(0f, 0f, 10f), Quaternion.Identity, Float3.One));

        var sync = new SyncTrack(new[] { new SyncEvent(new StringID("s0"), 0f), new SyncEvent(new StringID("s1"), secondEventTime) });
        return new AnimationClip(skeleton, new[] { f0, f1 }, 1f, syncTrack: sync);
    }

    [Fact]
    public void SyncTrack_FirstEventAfterZero_WrapsTheLastEvent()
    {
        SyncTrack source = TestClips.Track(0.1f, 0.6f);
        SyncTrack target = TestClips.Track(0f, 0.5f);

        Assert.Equal(0.95, (double)source.RemapTo(source.GetTime(0.05f), target), 3);
        Assert.Equal(0.25, (double)source.RemapTo(source.GetTime(0.35f), target), 3);
        Assert.Equal(0.75, (double)source.RemapTo(source.GetTime(0.85f), target), 3);
    }

    [Fact]
    public void SyncTrack_BlendHasTheLowestCommonMultipleOfEvents()
    {
        var blended = new SyncTrack(SyncTrack.Default, TestClips.Track(0f, 0.25f, 0.5f, 0.75f), 0.5f);
        Assert.Equal(4, blended.EventCount);
        Assert.Equal(6, new SyncTrack(TestClips.Track(0f, 0.5f), TestClips.Track(0f, 0.3f, 0.6f), 0.5f).EventCount);
    }

    [Fact]
    public void BlendSynchronized_UsesTheSourceLoopToReachLaterTargetEvents()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip single = TestClips.Ramp(skeleton);
        AnimationClip four = TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0f, 0.25f, 0.5f, 0.75f));
        var result = new Pose(skeleton);

        Blender.BlendSynchronized(result, single, four, 0.5f, 1f, new Pose(skeleton), new Pose(skeleton), sourceLoop: 3);

        Assert.Equal(8.75, (double)result.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void WrapperNodes_ForwardTheChildSyncTrack()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        SyncTrack footsteps = TestClips.Track(0f, 0.5f);
        var g = new AnimationGraph();
        int clip = g.AddClip(TestClips.Ramp(skeleton, syncTrack: footsteps));
        int speed = g.AddSpeedScale(clip, defaultSpeed: 1.5f);
        int mirror = g.AddMirror(speed);
        int overrideRm = g.AddRootMotionOverride(mirror);
        int selector = g.AddSelector(g.AddIntParameter("Which"), new[] { overrideRm });
        g.SetRoot(selector);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);
        instance.Update(0.1f);

        foreach (int node in new[] { speed, mirror, overrideRm, selector })
            Assert.Same(footsteps, ((PoseNodeInstance)instance.GetNodeInstance(node)).SyncTrack);
    }

    [Fact]
    public void SyncTrack_TimeZeroOnATrackStartingLater_StaysAtZero()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        AnimationEvent[] end = { new IdEvent(new StringID("end"), 1f) };
        int b = g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0.25f, 0.75f), events: end), loop: false);
        int layer = g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0.25f, 0.75f), events: end), loop: false);
        g.SetRoot(g.AddLayerBlend(b, new[] { new LayerInfo(layer) { Synchronized = true } }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0f);

        Assert.Equal(0.0, (double)((PoseNodeInstance)instance.GetNodeInstance(layer)).NormalizedTime, 3);
        Assert.Equal(0, TestClips.CountId(instance.Events, "end"));
    }

    [Fact]
    public void Default_BehavesLikeRawTime()
    {
        var t = SyncTrack.Default.GetTime(0.3f);
        Assert.Equal(0, t.EventIndex);
        Assert.Equal(0.3, (double)t.PercentageThrough, 4);
        Assert.Equal(0.3, (double)SyncTrack.Default.GetNormalizedTime(t), 4);
    }

    [Fact]
    public void GetTime_LocatesEvent()
    {
        var track = new SyncTrack(new[]
        {
            new SyncEvent(new StringID("a"), 0.0f),
            new SyncEvent(new StringID("b"), 0.5f),
        });

        var t = track.GetTime(0.75f); // halfway through the second event [0.5,1.0)
        Assert.Equal(1, t.EventIndex);
        Assert.Equal(0.5, (double)t.PercentageThrough, 4);
        Assert.Equal(0.75, (double)track.GetNormalizedTime(t), 4);
    }

    [Fact]
    public void RemapTo_AlignsPhaseAcrossDifferentlyTimedTracks()
    {
        // Same two events, but the second event is at a different normalized time on each track.
        var a = new SyncTrack(new[] { new SyncEvent(new StringID("step"), 0f), new SyncEvent(new StringID("step2"), 0.5f) });
        var b = new SyncTrack(new[] { new SyncEvent(new StringID("step"), 0f), new SyncEvent(new StringID("step2"), 0.25f) });

        // At A's second-event start (0.5), the equivalent time on B is its second-event start (0.25).
        float bTime = a.RemapTo(a.GetTime(0.5f), b);
        Assert.Equal(0.25, (double)bTime, 4);
    }

    [Fact]
    public void BlendSynchronized_TimeWarpsTargetToReferencePhase()
    {
        var skeleton = TestSkeletons.MakeChain();
        AnimationClip a = RampClip(skeleton, 0.5f);
        AnimationClip b = RampClip(skeleton, 0.25f);

        var result = new Pose(skeleton);
        var sa = new Pose(skeleton);
        var sb = new Pose(skeleton);

        // Reference phase 0.5 == A's second event. B should be sampled at ITS second event (0.25),
        // i.e. Z = 2.5 - not at raw 0.5 (which would be Z = 5.0). weight 1 = full target.
        Blender.BlendSynchronized(result, a, b, referencePhase: 0.5f, weight: 1f, sa, sb);
        Assert.Equal(2.5, (double)result.GetTransform(0).position.Z, 3);
    }
}
