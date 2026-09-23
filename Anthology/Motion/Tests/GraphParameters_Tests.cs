using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Graph parameters: control parameters, triggers and virtual parameters.</summary>
public class GraphParameters_Tests
{
    [Fact]
    public void Parameters_RejectTheWrongType()
    {
        var g = new AnimationGraph();
        g.AddBoolParameter("Flag");
        g.AddFloatParameter("Speed");
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        Assert.Throws<ArgumentException>(() => instance.SetFloat("Flag", 1f));
        Assert.Throws<ArgumentException>(() => instance.SetVector("Speed", new Float3(1f, 0f, 0f)));

        int speed = instance.GetParameterIndex("Speed");
        instance.SetFloat(speed, 3f);
        instance.SetInt(speed, 4);
        Assert.Equal(4.0, (double)instance.GetFloat("Speed"), 4);
    }

    [Fact]
    public void Trigger_TurnsItselfOffOnceATransitionFiresOnIt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int jump = graph.AddTriggerParameter("Jump");
        int held = graph.AddBoolParameter("Held");
        int sm = graph.AddStateMachine();
        int idle = graph.AddState(sm, graph.AddClip(TestClips.Const(skeleton, 0f)), "Idle");
        int air = graph.AddState(sm, graph.AddClip(TestClips.Const(skeleton, 10f)), "Air");
        graph.AddTransition(sm, idle, air, graph.AddOr(jump, held), duration: 0f);
        graph.SetStateMachineDefault(sm, idle);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.016f);
        Assert.False(instance.GetBool("Jump"));

        instance.SetBool("Jump", true);
        instance.SetBool("Held", true);
        instance.Update(0.016f);

        // The trigger is spent, while a plain bool read by the same condition is left as the game set it.
        Assert.False(instance.GetBool("Jump"));
        Assert.True(instance.GetBool("Held"));
    }

    [Fact]
    public void Trigger_StaysSetUntilSomethingFiresOnIt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int jump = graph.AddTriggerParameter("Jump");
        int never = graph.AddConstBool(false);
        int sm = graph.AddStateMachine();
        int idle = graph.AddState(sm, graph.AddClip(TestClips.Const(skeleton, 0f)), "Idle");
        int air = graph.AddState(sm, graph.AddClip(TestClips.Const(skeleton, 10f)), "Air");
        graph.AddTransition(sm, idle, air, graph.AddAnd(jump, never));
        graph.SetStateMachineDefault(sm, idle);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetBool("Jump", true);
        for (int i = 0; i < 5; i++) instance.Update(0.016f);

        Assert.True(instance.GetBool("Jump"));
    }

    [Fact]
    public void VirtualParameter_IsFindableByName()
    {
        var g = new AnimationGraph();
        int math = g.AddFloatMath(g.AddConstFloat(2f), g.AddConstFloat(3f), FloatMathOp.Add);
        int vp = g.AddVirtualParameter("Sum", math);
        g.SetRoot(g.AddReferencePose());

        Assert.Equal(math, vp);
        Assert.Equal(math, g.GetNodeIndex("Sum"));

        AnimationGraphInstance i = g.CreateInstance(TestSkeletons.MakeChain());
        Assert.Equal(5.0, (double)i.EvaluateValueNode(g.GetNodeIndex("Sum")).AsFloat(), 4);
    }

    [Fact]
    public void ControlParameter_AddedThroughAddNode_IsRegistered()
    {
        var g = new AnimationGraph();
        g.AddNode(new ControlParameterDefinition("Speed", AnimationValueType.Float, ParameterValue.FromFloat(2f)));
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        instance.Update(0.1f);

        Assert.Equal(0, instance.GetParameterIndex("Speed"));
        Assert.Equal(2.0, (double)instance.GetFloat("Speed"), 4);
    }

    [Fact]
    public void ControlParameter_RejectedByNameCollision_LeavesNothingBehind()
    {
        var g = new AnimationGraph();
        g.NameNode(g.AddReferencePose(), "Speed");

        Assert.Throws<ArgumentException>(() => g.AddFloatParameter("Speed"));

        Assert.Empty(g.Parameters);
        Assert.Equal(-1, g.GetParameterIndex("Speed"));
        Assert.Equal(1, g.NodeCount);
    }
}
