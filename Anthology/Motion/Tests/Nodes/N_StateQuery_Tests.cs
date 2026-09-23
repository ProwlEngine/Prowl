using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The State Query nodes: time in state and progress through it.</summary>
public class N_StateQuery_Tests
{
    private static AnimationClip Ramp(Skeleton skeleton, float rootTravel = 0f, params AnimationEvent[] events)
        => TestClips.Ramp(skeleton, rootTravel: rootTravel, events: events);

    [Fact]
    public void TimeInState_CountsFromTheStartOfTheTransitionIntoIt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        int b = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        g.AddTransition(sm, a, b, go, 0.5f);
        int time = g.AddStateQuery(sm, StateQuery.TimeInState);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 10; i++)
            instance.Update(0.1f);
        instance.SetBool("Go", true);
        instance.Update(0.1f);
        instance.Update(0.1f);
        Assert.Equal(0.1, (double)instance.EvaluateValueNode(time).AsFloat(), 3);

        for (int i = 0; i < 5; i++)
            instance.Update(0.1f);
        Assert.Equal(0.6, (double)instance.EvaluateValueNode(time).AsFloat(), 3);
    }

    [Fact]
    public void TimeInState_FiresTransitionAfterDelay()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int idle = graph.AddClip(TestClips.Const(skeleton, 0f));
        int walk = graph.AddClip(TestClips.Const(skeleton, 10f));
        int sm = graph.AddStateMachine();
        int idleState = graph.AddState(sm, idle, "Idle");
        int walkState = graph.AddState(sm, walk, "Walk");
        int afterHalfSecond = graph.AddStateTimeElapsed(sm, 0.5f);
        graph.AddTransition(sm, idleState, walkState, afterHalfSecond, duration: 0f);
        graph.SetStateMachineDefault(sm, idleState);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.1f); // 0.1s in -> still idle
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        for (int i = 0; i < 8; i++)
            instance.Update(0.1f); // well past 0.5s

        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void StateFinished_FiresWhenContentClipCompletes()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int oneShot = graph.AddClip(TestClips.Const(skeleton, 0f), loop: false); // 1s clamped clip
        int done = graph.AddClip(TestClips.Const(skeleton, 7f));
        int sm = graph.AddStateMachine();
        int playing = graph.AddState(sm, oneShot, "Playing");
        int finished = graph.AddState(sm, done, "Finished");
        graph.AddTransition(sm, playing, finished, graph.AddStateFinished(sm), duration: 0f);
        graph.SetStateMachineDefault(sm, playing);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.5f); // halfway through the 1s clip -> not finished
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        for (int i = 0; i < 4; i++)
            instance.Update(0.2f); // run the clip past its end

        Assert.Equal(7.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }
}
