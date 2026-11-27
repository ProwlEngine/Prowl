using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Blend 1D node: blending a line of poses by one value, their timing and their events.</summary>
public class N_Blend1D_Tests
{
    private static AnimationClip Ramp(Skeleton skeleton, float rootTravel = 0f, params AnimationEvent[] events)
        => TestClips.Ramp(skeleton, rootTravel: rootTravel, events: events);

    [Fact]
    public void Blend1D_BlendsByParameter()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddFloatParameter("Speed");
        int idle = graph.AddClip(TestClips.Const(skeleton, 0f));
        int run = graph.AddClip(TestClips.Const(skeleton, 10f));
        int blend = graph.AddBlend1D(speed, new[] { (idle, 0f), (run, 1f) });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.SetFloat("Speed", 0.5f);
        instance.Update(0.016f);
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetFloat("Speed", 1f);
        instance.Update(0.016f);
        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void Blend1D_ClampsBelowFirstThreshold()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddFloatParameter("Speed");
        int a = graph.AddClip(TestClips.Const(skeleton, 2f));
        int b = graph.AddClip(TestClips.Const(skeleton, 8f));
        int blend = graph.AddBlend1D(speed, new[] { (a, 1f), (b, 3f) });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetFloat("Speed", -5f); // below first threshold -> clamps to a
        instance.Update(0.016f);
        Assert.Equal(2.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void Blend1D_DifferentSyncEventCounts_PlaysTheWholeLongerClip()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int param = g.AddFloatParameter("P", 0.5f);
        int two = g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0f, 0.5f)));
        int four = g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0f, 0.25f, 0.5f, 0.75f)));
        g.SetRoot(g.AddBlend1D(param, new[] { (two, 0f), (four, 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        float maxFour = 0f;
        for (int i = 0; i < 60; i++)
        {
            instance.Update(1f / 30f);
            maxFour = MathF.Max(maxFour, ((PoseNodeInstance)instance.GetNodeInstance(four)).NormalizedTime);
        }
        Assert.True(maxFour > 0.9f, $"four event clip only reached {maxFour}");
    }

    [Fact]
    public void Blend1D_UnusedChild_IsNotUpdatedAndEmitsNoEvents()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int speed = g.AddFloatParameter("Speed", 0f);
        int idle = g.AddClip(TestClips.Const(skeleton, 0f));
        int run = g.AddClip(Ramp(skeleton, 0f, new IdEvent(new StringID("step"), 0.5f)));
        g.SetRoot(g.AddBlend1D(speed, new[] { (idle, 0f), (run, 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        int steps = 0;
        for (int i = 0; i < 20; i++)
        {
            instance.Update(0.1f);
            steps += TestClips.CountId(instance.Events, "step");
        }
        Assert.Equal(0, steps);
    }

    [Fact]
    public void NaNBlendParameter_KeepsTheLowestEntry()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P", float.NaN);
        g.SetRoot(g.AddBlend1D(p, new[] { (g.AddClip(TestClips.Const(skeleton, 1f)), 0f), (g.AddClip(TestClips.Const(skeleton, 5f)), 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void Blend1D_WithoutLooping_HoldsItsLastFrame()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P", 0.5f);
        int low = g.AddClip(TestClips.Ramp(skeleton, 10f, rootTravel: 1f), loop: false);
        int high = g.AddClip(TestClips.Ramp(skeleton, 20f, rootTravel: 1f), loop: false);
        g.SetRoot(g.AddBlend1D(p, new[] { (low, 0f), (high, 1f) }, loop: false));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        float travel = 0f;
        for (int i = 0; i < 15; i++)
        {
            instance.Update(0.1f);
            travel += instance.RootMotionDelta.position.Z;
        }

        Assert.Equal(15.0, (double)TestClips.RootZ(instance), 2);
        Assert.Equal(1.0, (double)travel, 2);
    }

    [Fact]
    public void Blend1D_PhaseLocksChildrenWithDifferentDurations()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int p = graph.AddFloatParameter("P");
        int a = graph.AddClip(TestClips.Ramp(skeleton, duration: 1.0f)); // 1s clip
        int b = graph.AddClip(TestClips.Ramp(skeleton, duration: 2.0f)); // 2s clip (different duration)
        int blend = graph.AddBlend1D(p, new[] { (a, 0f), (b, 1f) });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetFloat("P", 0.5f);
        instance.Update(0.3f);
        instance.Update(0.3f);

        var aNode = (PoseNodeInstance)instance.GetNodeInstance(a);
        var bNode = (PoseNodeInstance)instance.GetNodeInstance(b);

        // Despite different durations, both children are at the same normalized phase.
        Assert.Equal((double)aNode.NormalizedTime, (double)bNode.NormalizedTime, 4);
        Assert.True(aNode.NormalizedTime > 0.01f); // the shared clock actually advanced

        // The blended (identical) content therefore reads the shared phase.
        Assert.Equal((double)(aNode.NormalizedTime * 10f), (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void NegativeDeltaTime_ThroughABlend_StepsBackwardLikeTheBareClip()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P", 0f);
        AnimationClip walk = TestClips.Ramp(skeleton, rootTravel: 1f, events: new AnimationEvent[] { new IdEvent(new StringID("step"), 0.45f) });
        g.SetRoot(g.AddBlend1D(p, new[] { (g.AddClip(walk), 0f), (g.AddClip(walk), 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.5f);
        float travel = 0f;
        int steps = 0;
        for (int i = 0; i < 6; i++)
        {
            instance.Update(-1f / 60f);
            travel += instance.RootMotionDelta.position.Z;
            steps += TestClips.CountId(instance.Events, "step");
        }

        Assert.Equal(-0.1, (double)travel, 3);
        Assert.Equal(1, steps);
        Assert.Equal(4.0, (double)TestClips.RootZ(instance), 2);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.9f, 0.1f)]
    public void StaticPoseInABlend_DoesNotSpeedUpTheClip(float staticWeight, float expectedTravel)
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P", staticWeight);
        int walk = g.AddClip(TestClips.Ramp(skeleton, rootTravel: 1f, events: new AnimationEvent[] { new IdEvent(new StringID("step"), 0.5f) }));
        g.SetRoot(g.AddBlend1D(p, new[] { (walk, 0f), (g.AddReferencePose(), 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        float travel = 0f;
        int steps = 0;
        for (int i = 0; i < 10; i++)
        {
            instance.Update(0.1f);
            travel += instance.RootMotionDelta.position.Z;
            steps += TestClips.CountId(instance.Events, "step");
        }

        Assert.Equal(expectedTravel, (double)travel, 3);
        Assert.Equal(1, steps);
    }

    [Fact]
    public void ShortClipInABlend_KeepsEveryLoop()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P");
        AnimationClip shortClip = TestClips.Ramp(skeleton, duration: 0.01f, rootTravel: 1f);
        g.SetRoot(g.AddBlend1D(p, new[] { (g.AddClip(shortClip), 0f), (g.AddClip(shortClip), 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        float travel = 0f;
        for (int i = 0; i < 60; i++)
        {
            instance.Update(1f / 60f);
            travel += instance.RootMotionDelta.position.Z;
        }

        Assert.Equal(100.0, (double)travel, 1);
    }
}
