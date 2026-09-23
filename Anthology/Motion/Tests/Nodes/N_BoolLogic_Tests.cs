using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The And, Or and Not node.</summary>
public class N_BoolLogic_Tests
{
    [Fact]
    public void Not_InvertsBool_ForReverseTransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int isWalking = graph.AddBoolParameter("IsWalking", true);
        int notWalking = graph.AddNot(isWalking);
        int idle = graph.AddClip(TestClips.Const(skeleton, 0f));
        int walk = graph.AddClip(TestClips.Const(skeleton, 10f));
        int sm = graph.AddStateMachine();
        int walkState = graph.AddState(sm, walk);
        int idleState = graph.AddState(sm, idle);
        graph.AddTransition(sm, walkState, idleState, notWalking, duration: 0f);
        graph.SetStateMachineDefault(sm, walkState);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.016f);
        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 2); // walking

        instance.SetBool("IsWalking", false); // notWalking becomes true -> transition to idle
        instance.Update(0.016f);
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }
}
