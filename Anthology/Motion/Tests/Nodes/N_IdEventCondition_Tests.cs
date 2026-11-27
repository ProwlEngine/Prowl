using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Id Event Condition node.</summary>
public class N_IdEventCondition_Tests
{
    private static AnimationClip Ramp(Skeleton skeleton, float rootTravel = 0f, params AnimationEvent[] events)
        => TestClips.Ramp(skeleton, rootTravel: rootTravel, events: events);

    [Fact]
    public void IdEvent_DrivesStateTransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var go = new StringID("Go");

        var graph = new AnimationGraph();
        int a = graph.AddClip(TestClips.Const(skeleton, 0f, new IdEvent(go, 0.4f, 0.3f)));
        int b = graph.AddClip(TestClips.Const(skeleton, 10f));
        int sm = graph.AddStateMachine();
        int sa = graph.AddState(sm, a, "A");
        int sb = graph.AddState(sm, b, "B");
        graph.AddTransition(sm, sa, sb, graph.AddIdEventCondition(go), duration: 0f);
        graph.SetStateMachineDefault(sm, sa);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.2f); // t=0.2, before the event window
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        for (int i = 0; i < 4; i++)
            instance.Update(0.1f); // cross into [0.4,0.7] -> event fires -> transition

        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void EventConditions_IgnoreEventsFromTheLosingSideOfATransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton, 0f, new IdEvent(new StringID("x"), 0.65f))));
        int b = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, 0f)));
        g.AddTransition(sm, a, b, go, 1f);
        g.SetRoot(sm);
        int activeOnly = g.AddIdEventCondition(new StringID("x"));
        int any = g.AddNode(new IdEventConditionDefinition(new[] { new StringID("x") }, false) { IncludeInactiveBranch = true });
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 6; i++)
            instance.Update(0.1f);
        instance.SetBool("Go", true);
        instance.Update(0.1f);

        Assert.False(instance.EvaluateValueNode(activeOnly).AsBool());
        Assert.True(instance.EvaluateValueNode(any).AsBool());
    }
}
