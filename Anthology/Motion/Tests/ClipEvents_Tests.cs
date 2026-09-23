using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Clip events: which events a span of playback samples, across loops and frame snapping.</summary>
public class ClipEvents_Tests
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

    private static AnimationClip ClipWithEvents(params AnimationEvent[] events)
    {
        var skeleton = TestSkeletons.MakeChain();
        var f0 = new Pose(skeleton); f0.SetToReferencePose();
        var f1 = new Pose(skeleton); f1.SetToReferencePose();
        return new AnimationClip(skeleton, new[] { f0, f1 }, 1f, events: events);
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
    public void SampleEvents_DurationEventAcrossTheWrap_IsAddedOnce()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip clip = TestClips.Ramp(skeleton, events: new AnimationEvent[] { new IdEvent(new StringID("whole"), 0f, 1f) });
        var buffer = new SampledEventsBuffer();

        clip.SampleEvents(0.95f, 0.05f, buffer, looped: true);

        Assert.Equal(1, TestClips.CountId(buffer, "whole"));
    }

    [Fact]
    public void SampleEvents_DurationEventRunningPastTheEnd_IsActiveInItsWrappedTail()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip clip = TestClips.Ramp(skeleton, events: new AnimationEvent[] { new IdEvent(new StringID("tail"), 0.9f, 0.2f) });
        var buffer = new SampledEventsBuffer();

        clip.SampleEvents(0.02f, 0.05f, buffer);

        Assert.Equal(1, TestClips.CountId(buffer, "tail"));
    }

    [Fact]
    public void ImmediateEvent_FiresWhenCrossed()
    {
        var clip = ClipWithEvents(new IdEvent(new StringID("hit"), 0.5f));
        var buffer = new SampledEventsBuffer();

        clip.SampleEvents(0.4f, 0.6f, buffer);
        Assert.True(buffer.ContainsId(new StringID("hit")));

        buffer.Clear();
        clip.SampleEvents(0.6f, 0.8f, buffer); // already passed
        Assert.False(buffer.ContainsId(new StringID("hit")));
    }

    [Fact]
    public void LoopWrap_FiresEventsAcrossTheBoundary()
    {
        var clip = ClipWithEvents(new IdEvent(new StringID("loopstart"), 0.05f));
        var buffer = new SampledEventsBuffer();

        // Wrapped from 0.95 -> 0.05 (looped): the event at 0.05 should fire.
        clip.SampleEvents(0.95f, 0.05f, buffer, looped: true);
        Assert.True(buffer.ContainsId(new StringID("loopstart")));
    }

    [Fact]
    public void FootEvent_IsQueryable()
    {
        var clip = ClipWithEvents(new FootEvent(FootPhase.LeftFootDown, 0.25f), new FootEvent(FootPhase.RightFootDown, 0.75f));
        var buffer = new SampledEventsBuffer();
        clip.SampleEvents(0.2f, 0.3f, buffer);

        var feet = new List<FootEvent>(buffer.FootEvents());
        Assert.Single(feet);
        Assert.Equal(FootPhase.LeftFootDown, feet[0].Phase);
        Assert.True(feet[0].IsLeft);
        Assert.True(feet[0].IsFootDown);
    }

    [Fact]
    public void DurationEvent_ActiveAcrossRange()
    {
        var clip = ClipWithEvents(new IdEvent(new StringID("window"), 0.3f, duration: 0.4f)); // active [0.3,0.7)
        var buffer = new SampledEventsBuffer();
        clip.SampleEvents(0.5f, 0.55f, buffer); // inside the window
        Assert.True(buffer.ContainsId(new StringID("window")));
    }

    [Fact]
    public void DurationEventAcrossTheLoop_ReportsProgressAtTheRealEndTime()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip clip = TestClips.Ramp(skeleton, events: new AnimationEvent[] { new IdEvent(new StringID("d"), 0.8f, 0.3f) });
        var buffer = new SampledEventsBuffer();

        clip.SampleEvents(0.9f, 0.05f, buffer, looped: true);

        Assert.Equal(0.833, (double)buffer[0].PercentageThrough, 3);
    }

    [Fact]
    public void SnapToFrame_CoversTheWrappedTailOfTheEvent()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip clip = TestClips.Ramp(skeleton, events: new AnimationEvent[] { new SnapToFrameEvent(FrameSnapMode.Floor, 0.85f, 0.3f) });
        var g = new AnimationGraph();
        g.SetRoot(g.AddClip(clip));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.87f);
        Assert.Equal(8.0, (double)TestClips.RootZ(instance), 3);
        instance.Update(0.18f);
        Assert.Equal(0.0, (double)TestClips.RootZ(instance), 3);
    }
}
