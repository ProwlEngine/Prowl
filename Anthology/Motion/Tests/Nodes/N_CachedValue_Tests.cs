using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Cached Value node: sample and hold, on entry, on a rising edge, or until its branch leaves.</summary>
public class N_CachedValue_Tests
{
    private static (AnimationGraph Graph, int Root) NewGraph()
    {
        var g = new AnimationGraph();
        int root = g.AddReferencePose();
        return (g, root);
    }

    [Fact]
    public void CachedValue_DriverAlreadyTrueOnTheFirstFrame_IsNotARisingEdge()
    {
        var g = new AnimationGraph();
        int x = g.AddFloatParameter("X", 1f);
        int driver = g.AddBoolParameter("Driver", true);
        int cached = g.AddCachedValue(x, driver);
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        Assert.Equal(1.0, (double)instance.EvaluateValueNode(cached).AsFloat(), 4);
        instance.SetFloat("X", 2f);
        Assert.Equal(1.0, (double)instance.EvaluateValueNode(cached).AsFloat(), 4);
    }

    [Fact]
    public void CachedValue_RelatchesEachTimeItsStateIsEntered()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int source = g.AddFloatParameter("Source", 2f);
        int away = g.AddBoolParameter("Away");
        int cached = g.AddCachedValue(source);
        int blend = g.AddBlend1D(cached, new[] { (g.AddClip(TestClips.Const(skeleton, 0f)), 0f), (g.AddClip(TestClips.Const(skeleton, 10f)), 10f) });
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, blend);
        int b = g.AddState(sm, g.AddReferencePose());
        g.AddTransition(sm, a, b, away, 0f);
        g.AddTransition(sm, b, a, g.AddNot(away), 0f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);
        Assert.Equal(2.0, (double)TestClips.RootZ(instance), 3);

        instance.SetBool("Away", true);
        instance.Update(0.1f);
        instance.SetFloat("Source", 8f);
        instance.SetBool("Away", false);
        instance.Update(0.1f);
        instance.Update(0.1f);

        Assert.Equal(8.0, (double)TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void CachedValue_OnExit_FreezesWhenItsBranchStartsLeaving()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int source = g.AddFloatParameter("Source", 2f);
        int go = g.AddBoolParameter("Go");
        int cached = g.AddCachedValue(source, mode: CachedValueMode.OnExit);
        int blend = g.AddBlend1D(cached, new[] { (g.AddClip(TestClips.Const(skeleton, 0f)), 0f), (g.AddClip(TestClips.Const(skeleton, 10f)), 10f) });
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, blend);
        int b = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, 0f)));
        g.AddTransition(sm, a, b, go, 10f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.SetFloat("Source", 4f);
        instance.Update(0.1f);
        instance.SetBool("Go", true);
        instance.Update(0.1f);
        instance.SetFloat("Source", 9f);
        instance.Update(0.1f);

        Assert.Equal(4.0, (double)((PoseNodeInstance)instance.GetNodeInstance(blend)).Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void CachedValue_SampleAndHold()
    {
        var (g, root) = NewGraph();
        int src = g.AddFloatParameter("Src", 1f);
        int sample = g.AddBoolParameter("Sample", false);
        int cached = g.AddCachedValue(src, sample);
        g.SetRoot(root);

        AnimationGraphInstance i = g.CreateInstance(TestSkeletons.MakeChain());
        Assert.Equal(1.0, (double)i.EvaluateValueNode(cached).AsFloat(), 4); // latched on first eval

        i.SetFloat("Src", 5f);
        Assert.Equal(1.0, (double)i.EvaluateValueNode(cached).AsFloat(), 4); // still holding 1

        i.SetBool("Sample", true);
        Assert.Equal(5.0, (double)i.EvaluateValueNode(cached).AsFloat(), 4); // re-latched on rising edge

        i.SetFloat("Src", 9f);
        i.SetBool("Sample", false);
        Assert.Equal(5.0, (double)i.EvaluateValueNode(cached).AsFloat(), 4); // holding 5
    }
}
