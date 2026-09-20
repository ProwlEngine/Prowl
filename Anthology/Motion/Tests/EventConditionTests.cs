using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class EventConditionTests
{
    private static AnimationClip ConstClip(Skeleton skeleton, float z, params AnimationEvent[] events)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f, events: events);
    }

    [Fact]
    public void IdEvent_DrivesStateTransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var go = new StringID("Go");

        var graph = new AnimationGraph();
        int a = graph.AddClip(ConstClip(skeleton, 0f, new IdEvent(go, 0.4f, 0.3f)));
        int b = graph.AddClip(ConstClip(skeleton, 10f));
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
    public void FootEventCondition_GroupsPhases()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();

        var graph = new AnimationGraph();
        // Left-foot-down event active across most of the clip.
        int walk = graph.AddClip(ConstClip(skeleton, 0f, new FootEvent(FootPhase.LeftFootDown, 0.1f, 0.8f)));
        int planted = graph.AddClip(ConstClip(skeleton, 4f));
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
    public void TransitionEvent_BlockPreventsTransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();

        var graph = new AnimationGraph();
        // The source clip is fully blocked for transitioning away.
        int blockedClip = graph.AddClip(ConstClip(skeleton, 0f, new TransitionEvent(TransitionRule.BlockTransition, 0.0f, 0.9f)));
        int target = graph.AddClip(ConstClip(skeleton, 5f));
        int sm = graph.AddStateMachine();
        int s0 = graph.AddState(sm, blockedClip);
        int s1 = graph.AddState(sm, target);
        graph.AddTransition(sm, s0, s1, graph.AddTransitionEventCondition(TransitionRuleCondition.AnyAllowed), duration: 0f);
        graph.SetStateMachineDefault(sm, s0);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.1f);
        instance.Update(0.1f);
        // The "any allowed" transition cannot fire while the region is blocked.
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }
}
