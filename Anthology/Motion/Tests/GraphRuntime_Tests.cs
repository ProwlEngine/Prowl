using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Running a graph: updating, finding nodes, allocation, value caching and inspection.</summary>
public class GraphRuntime_Tests
{
    private static AnimationClip Ramp(Skeleton skeleton, float rootTravel = 0f, params AnimationEvent[] events)
        => TestClips.Ramp(skeleton, rootTravel: rootTravel, events: events);

    [Fact]
    public void Graph_PlaysAClip()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(TestClips.Const(skeleton, 7f));
        graph.SetRoot(clip);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.1f);

        Assert.Equal(7.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void NamedNodes_AreFindableByName()
    {
        var graph = new AnimationGraph();
        int refPose = graph.AddReferencePose();
        graph.NameNode(refPose, "Base");
        graph.SetRoot(refPose);

        Assert.Equal(refPose, graph.GetNodeIndex("Base"));
        Assert.Equal(-1, graph.GetNodeIndex("Missing"));
    }

    [Fact]
    public void EditingTheGraph_AfterCreatingAnInstance_DoesNotBreakIt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        int b = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);
        instance.Update(0.1f);

        g.AddTransition(sm, a, b, g.AddBoolParameter("Late"));
        instance.Update(0.1f);

        Assert.Equal(2.0, (double)TestClips.RootZ(instance), 2);
    }

    [Fact]
    public void Update_DoesNotAllocateOnceWarm()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int speed = g.AddFloatParameter("Speed", 0.5f);
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddBlend1D(speed, new[] { (g.AddClip(Ramp(skeleton, 1f)), 0f), (g.AddClip(Ramp(skeleton, 2f)), 1f) }));
        int b = g.AddState(sm, g.AddClip(Ramp(skeleton, 1f, new IdEvent(new StringID("x"), 0.5f))));
        g.AddTransition(sm, a, b, go, 0.3f);
        g.AddTransition(sm, b, a, g.AddNot(go), 0.3f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        long lowest = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 120; i++)
            {
                instance.SetBool("Go", i % 20 < 10);
                instance.Update(1f / 30f);
            }
            lowest = Math.Min(lowest, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0, lowest);
    }

    [Fact]
    public void ResetGraphState_ReturnsToTheDefaultState()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        int b = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, -5f)));
        g.AddTransition(sm, a, b, go, 0f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.SetBool("Go", true);
        instance.Update(0.1f);
        instance.Update(0.1f);
        Assert.Equal(-5.0, (double)TestClips.RootZ(instance), 3);

        instance.SetBool("Go", false);
        instance.ResetGraphState();
        instance.Update(0.1f);

        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 2);
    }

    [Fact]
    public void AnEventReadBeforeItsClipPlays_StillCountsLaterInTheFrame()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int fired = graph.AddIdEventCondition(new StringID("Go"));
        // The speed is read before the clip under it samples its event, which is what used to cache false.
        int speed = graph.AddFloatSwitch(fired, graph.AddConstFloat(1f), graph.AddConstFloat(1f));
        int step = graph.AddSpeedScale(graph.AddClip(TestClips.Const(skeleton, 0f, new IdEvent(new StringID("Go"), 0.5f))), speed);

        int machine = graph.AddStateMachine();
        int a = graph.AddState(machine, step, "A");
        int b = graph.AddState(machine, graph.AddClip(TestClips.Const(skeleton, 10f)), "B");
        graph.AddTransition(machine, a, b, fired, duration: 0f);
        graph.SetStateMachineDefault(machine, a);
        graph.SetRoot(machine);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        for (int i = 0; i < 60; i++)
            instance.Update(1f / 60f);

        Assert.Equal(10f, TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void AnEventCondition_SeesAWeightChangedAfterItWasRead()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int fired = graph.AddIdEventCondition(new StringID("Go"));
        int loud = graph.AddClip(TestClips.Const(skeleton, 0f, new IdEvent(new StringID("Go"), 0f, 1f)));
        // The second input reads the condition after the first has sampled its event, but before the
        // blend turns that event down to nothing.
        int reader = graph.AddSpeedScale(graph.AddClip(TestClips.Const(skeleton, 0f)),
            graph.AddFloatSwitch(fired, graph.AddConstFloat(1f), graph.AddConstFloat(1f)));
        int blend = graph.AddWeightedBlend(new[] { new WeightedPose(loud, -1, 0f), new WeightedPose(reader, -1, 1f) });

        int machine = graph.AddStateMachine();
        int a = graph.AddState(machine, blend, "A");
        int b = graph.AddState(machine, graph.AddClip(TestClips.Const(skeleton, 10f)), "B");
        graph.AddTransition(machine, a, b, fired, duration: 0f);
        graph.SetStateMachineDefault(machine, a);
        graph.SetRoot(machine);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        for (int i = 0; i < 10; i++)
            instance.Update(1f / 60f);

        Assert.Equal(0f, TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void ReadingANodeForDebugging_LeavesItAsItWas()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();

        (AnimationGraphInstance Instance, int Held) Build()
        {
            var graph = new AnimationGraph();
            int fired = graph.AddIdEventCondition(new StringID("Go"));
            // Holds the timer on a rising edge of the event, and is read before the clip samples it each frame.
            int held = graph.AddCachedValue(graph.AddTimer(), fired);
            int speed = graph.AddFloatMath(held, graph.AddConstFloat(1f), FloatMathOp.Max);
            graph.SetRoot(graph.AddSpeedScale(graph.AddClip(TestClips.Const(skeleton, 0f, new IdEvent(new StringID("Go"), 0.5f))), speed));
            return (graph.CreateInstance(skeleton), held);
        }

        (AnimationGraphInstance watched, int watchedNode) = Build();
        (AnimationGraphInstance untouched, int untouchedNode) = Build();
        for (int i = 0; i < 60; i++)
        {
            watched.Update(1f / 60f);
            untouched.Update(1f / 60f);
            watched.TryReadValueNode(watchedNode, out _);
            watched.EvaluateValueNode(watchedNode);
        }

        Assert.True(untouched.TryReadValueNode(untouchedNode, out ParameterValue expected));
        Assert.True(watched.TryReadValueNode(watchedNode, out ParameterValue actual));
        Assert.Equal(expected.AsFloat(), actual.AsFloat());
    }

    [Fact]
    public void ReadingAnEventConditionForDebugging_ShowsTheAnswerAsTheFrameEnded()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int fired = graph.AddIdEventCondition(new StringID("Go"));
        // Read before the clip samples its event, so its first answer each frame is false.
        int speed = graph.AddFloatSwitch(fired, graph.AddConstFloat(1f), graph.AddConstFloat(1f));
        graph.SetRoot(graph.AddSpeedScale(graph.AddClip(TestClips.Const(skeleton, 0f, new IdEvent(new StringID("Go"), 0f, 1f))), speed));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f);

        Assert.True(instance.TryReadValueNode(fired, out ParameterValue value));
        Assert.True(value.AsBool());
        Assert.True(instance.EvaluateValueNode(fired).AsBool());
    }
}
