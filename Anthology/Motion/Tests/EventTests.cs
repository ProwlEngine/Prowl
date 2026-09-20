using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class EventTests
{
    private static AnimationClip ClipWithEvents(params AnimationEvent[] events)
    {
        var skeleton = TestSkeletons.MakeChain();
        var f0 = new Pose(skeleton); f0.SetToReferencePose();
        var f1 = new Pose(skeleton); f1.SetToReferencePose();
        return new AnimationClip(skeleton, new[] { f0, f1 }, 1f, events: events);
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
}
