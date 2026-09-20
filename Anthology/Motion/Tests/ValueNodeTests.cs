using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class ValueNodeTests
{
    private static AnimationClip ConstClip(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    [Fact]
    public void FloatCompare_DrivesAStateTransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddFloatParameter("Speed");
        int fast = graph.AddFloatCompare(speed, CompareOp.Greater, 0.5f); // Speed > 0.5
        int idle = graph.AddClip(ConstClip(skeleton, 0f));
        int run = graph.AddClip(ConstClip(skeleton, 10f));
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

    [Fact]
    public void Not_InvertsBool_ForReverseTransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int isWalking = graph.AddBoolParameter("IsWalking", true);
        int notWalking = graph.AddNot(isWalking);
        int idle = graph.AddClip(ConstClip(skeleton, 0f));
        int walk = graph.AddClip(ConstClip(skeleton, 10f));
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

    [Fact]
    public void FloatMathAndRemap_DriveBlend()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int raw = graph.AddFloatParameter("Raw"); // 0..100
        int normalized = graph.AddFloatRemap(raw, 0f, 100f, 0f, 1f); // -> 0..1
        int a = graph.AddClip(ConstClip(skeleton, 0f));
        int b = graph.AddClip(ConstClip(skeleton, 10f));
        int blend = graph.AddBlend1D(normalized, new[] { (a, 0f), (b, 1f) });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetFloat("Raw", 50f); // -> 0.5 -> blend midpoint
        instance.Update(0.016f);
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }
}
