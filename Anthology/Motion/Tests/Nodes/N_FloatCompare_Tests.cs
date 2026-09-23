using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Float Compare node.</summary>
public class N_FloatCompare_Tests
{
    [Fact]
    public void FloatCompare_DrivesAStateTransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddFloatParameter("Speed");
        int fast = graph.AddFloatCompare(speed, CompareOp.Greater, 0.5f); // Speed > 0.5
        int idle = graph.AddClip(TestClips.Const(skeleton, 0f));
        int run = graph.AddClip(TestClips.Const(skeleton, 10f));
        int sm = graph.AddStateMachine();
        int idleState = graph.AddState(sm, idle);
        int runState = graph.AddState(sm, run);
        graph.AddTransition(sm, idleState, runState, fast, duration: 0f);
        graph.SetStateMachineDefault(sm, idleState);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.SetFloat("Speed", 0.2f);
        instance.Update(0.016f);
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 2); // stays idle

        instance.SetFloat("Speed", 0.9f);
        instance.Update(0.016f);
        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 2); // transitions to run
    }
}
