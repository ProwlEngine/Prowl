using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Transition Event Condition node.</summary>
public class N_TransitionEventCondition_Tests
{
    [Fact]
    public void TransitionEvent_BlockPreventsTransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();

        var graph = new AnimationGraph();
        // The source clip is fully blocked for transitioning away.
        int blockedClip = graph.AddClip(TestClips.Const(skeleton, 0f, new TransitionEvent(TransitionRule.BlockTransition, 0.0f, 0.9f)));
        int target = graph.AddClip(TestClips.Const(skeleton, 5f));
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
