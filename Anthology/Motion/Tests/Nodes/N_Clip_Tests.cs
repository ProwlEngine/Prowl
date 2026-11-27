using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Clip node: playback, looping, reverse and reset drivers, start offsets, and the events and root motion it samples.</summary>
public class N_Clip_Tests
{
    private static AnimationClip Ramp(Skeleton skeleton, float rootTravel = 0f, params AnimationEvent[] events)
        => TestClips.Ramp(skeleton, rootTravel: rootTravel, events: events);

    // A clip whose one bone rises from nothing to full height over a second.
    private static AnimationClip Rise(Skeleton skeleton)
    {
        var low = new Pose(skeleton);
        low.SetToReferencePose();
        low.SetTransform(0, new Transform3D(Float3.Zero, Quaternion.Identity, Float3.One));

        var high = new Pose(skeleton);
        high.SetToReferencePose();
        high.SetTransform(0, new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One));

        return new AnimationClip(skeleton, new[] { low, high }, 1f);
    }

    private static Skeleton OneBone() => new(
        new[] { new StringID("Bone") },
        new[] { Skeleton.InvalidIndex },
        new[] { Transform3D.Identity });

    [Fact]
    public void ReverseAndResetDriversControlClipTime()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int reverse = graph.AddBoolParameter("Reverse");
        int reset = graph.AddBoolParameter("Reset");
        int clip = graph.AddClip(TestClips.Ramp(skeleton), loop: false);
        graph.SetClipDrivers(clip, reverse, reset);
        graph.SetRoot(clip);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.2f);
        instance.Update(0.2f); // t=0.4
        Assert.Equal(4.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetBool("Reverse", true);
        instance.Update(0.2f); // t=0.2 (played backwards)
        Assert.Equal(2.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetBool("Reverse", false);
        instance.SetBool("Reset", true);
        instance.Update(0.1f); // reset to 0, then advance 0.1
        Assert.Equal(1.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void ClipLoop_EventAtTimeZeroFiresEveryLoop()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        g.SetRoot(g.AddClip(TestClips.Ramp(skeleton, events: new AnimationEvent[] { new IdEvent(new StringID("start"), 0f), new IdEvent(new StringID("mid"), 0.5f) })));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        int start = 0, mid = 0;
        for (int i = 0; i < 33; i++)
        {
            instance.Update(0.1f);
            start += TestClips.CountId(instance.Events, "start");
            mid += TestClips.CountId(instance.Events, "mid");
        }

        Assert.Equal(4, start);
        Assert.Equal(3, mid);
    }

    [Fact]
    public void AHugeStepDoesNotStallTheGraph()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        var frame = new Pose(skeleton);
        frame.SetToReferencePose();
        graph.SetRoot(graph.AddClip(new AnimationClip(skeleton, new[] { frame, frame }, 0.033f)));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        instance.Update(1e6f, Transform3D.Identity);
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 500, $"one update took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ReversePlayback_MovesRootBackwardOneStepAtATime()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int reverse = g.AddBoolParameter("Reverse", true);
        int clip = g.AddClip(Ramp(skeleton, 1f, new IdEvent(new StringID("a"), 0.25f), new IdEvent(new StringID("b"), 0.75f)));
        g.SetClipDrivers(clip, playInReverseNodeIndex: reverse);
        g.SetRoot(clip);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        int events = 0;
        for (int i = 0; i < 10; i++)
        {
            instance.Update(0.1f);
            Assert.Equal(-0.1, (double)instance.RootMotionDelta.position.Z, 3);
            int frameEvents = TestClips.CountId(instance.Events, "a") + TestClips.CountId(instance.Events, "b");
            Assert.InRange(frameEvents, 0, 1);
            events += frameEvents;
        }
        Assert.Equal(2, events);
    }

    [Fact]
    public void LongStep_CoversEveryLoopOfRootMotionAndEvents()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        g.SetRoot(g.AddClip(Ramp(skeleton, 1f, new IdEvent(new StringID("a"), 0.25f), new IdEvent(new StringID("b"), 0.75f))));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(2.5f);

        Assert.Equal(2.5, (double)instance.RootMotionDelta.position.Z, 3);
        Assert.Equal(3, TestClips.CountId(instance.Events, "a"));
        Assert.Equal(2, TestClips.CountId(instance.Events, "b"));
    }

    [Fact]
    public void ResetDriver_RestartsWithoutAJump()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int reset = g.AddBoolParameter("Reset");
        int clip = g.AddClip(Ramp(skeleton, 1f), loop: false);
        g.SetClipDrivers(clip, resetTimeNodeIndex: reset);
        g.SetRoot(clip);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 8; i++)
            instance.Update(0.1f);
        instance.SetBool("Reset", true);
        instance.Update(0.1f);

        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 2);
    }

    [Fact]
    public void ClipNode_CanStartPartWayThrough()
    {
        Skeleton skeleton = OneBone();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddNode(new ClipNodeDefinition(Rise(skeleton)) { StartTime = 0.25f }));

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.001f);

        Assert.InRange(instance.Pose.GetTransform(0).position.Y, 0.24f, 0.27f);
    }

    /// <summary>Two clips on the same seed start together; different seeds pull them apart.</summary>
    [Fact]
    public void ClipNode_RandomStartFollowsItsSeed()
    {
        Skeleton skeleton = OneBone();

        float Start(uint seed)
        {
            var graph = new AnimationGraph();
            graph.SetRoot(graph.AddNode(new ClipNodeDefinition(Rise(skeleton)) { RandomStart = true, RandomSeed = seed }));

            AnimationGraphInstance instance = graph.CreateInstance(skeleton);
            instance.Update(0.001f);
            return (float)instance.Pose.GetTransform(0).position.Y;
        }

        Assert.Equal(Start(7), Start(7), 4);
        Assert.NotEqual(Start(7), Start(9), 3);
    }
}
