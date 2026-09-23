using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Foot Event Condition node.</summary>
public class N_FootEventCondition_Tests
{
    private static AnimationClip Ramp(Skeleton skeleton, float rootTravel = 0f, params AnimationEvent[] events)
        => TestClips.Ramp(skeleton, rootTravel: rootTravel, events: events);

    [Fact]
    public void FootEventCondition_GroupsPhases()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();

        var graph = new AnimationGraph();
        // Left-foot-down event active across most of the clip.
        int walk = graph.AddClip(TestClips.Const(skeleton, 0f, new FootEvent(FootPhase.LeftFootDown, 0.1f, 0.8f)));
        int planted = graph.AddClip(TestClips.Const(skeleton, 4f));
        int sm = graph.AddStateMachine();
        int sWalk = graph.AddState(sm, walk);
        int sPlanted = graph.AddState(sm, planted);
        // LeftPhase groups RightFootPassing OR LeftFootDown -> our LeftFootDown event satisfies it.
        graph.AddTransition(sm, sWalk, sPlanted, graph.AddFootEventCondition(FootPhaseCondition.LeftPhase), duration: 0f);
        graph.SetStateMachineDefault(sm, sWalk);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        for (int i = 0; i < 3; i++)
            instance.Update(0.1f); // cross into the foot-down window

        Assert.Equal(4.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void FootEventCondition_SeesEveryFootEventInTheFrame()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        g.SetRoot(g.AddClip(Ramp(skeleton, 0f, new FootEvent(FootPhase.LeftFootDown, 0.05f), new FootEvent(FootPhase.RightFootDown, 0.06f))));
        int right = g.AddFootEventCondition(FootPhaseCondition.RightFootDown);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.True(instance.EvaluateValueNode(right).AsBool());
    }
}
