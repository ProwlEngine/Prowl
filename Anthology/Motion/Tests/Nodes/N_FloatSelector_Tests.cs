using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Float Selector node: the value beside the first condition that holds.</summary>
public class N_FloatSelector_Tests
{
    private static float Read(AnimationGraph graph, Skeleton skeleton, int node)
    {
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.016f);
        return instance.EvaluateValueNode(node).AsFloat();
    }

    private static (AnimationGraph Graph, int Root) NewGraph()
    {
        var g = new AnimationGraph();
        int root = g.AddReferencePose();
        return (g, root);
    }

    [Fact]
    public void Chooser_ReadsItsValuesAndDefaultFromTheGraph()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int no = graph.AddConstBool(false);
        int yes = graph.AddConstBool(true);
        int seven = graph.AddConstFloat(7f);
        int three = graph.AddConstFloat(3f);

        int picked = graph.AddNode(new FloatSelectorDefinition(new[] { no, yes }, new[] { FloatInput.Of(1f), FloatInput.From(seven) },
            FloatInput.Of(0f), easing: EasingOp.None));
        int fallback = graph.AddNode(new FloatSelectorDefinition(new[] { no }, new[] { FloatInput.Of(1f) },
            FloatInput.From(three), easing: EasingOp.None));
        graph.SetRoot(graph.AddReferencePose());

        Assert.Equal(7f, Read(graph, skeleton, picked), 4);
        Assert.Equal(3f, Read(graph, skeleton, fallback), 4);
    }

    [Fact]
    public void Chooser_KeepsTheFirstPickEvenWhenItIsNotANumber()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int yes = graph.AddConstBool(true);
        int broken = graph.AddConstFloat(float.NaN);
        int picked = graph.AddNode(new FloatSelectorDefinition(new[] { yes, yes }, new[] { FloatInput.From(broken), FloatInput.Of(7f) },
            FloatInput.Of(0f), easing: EasingOp.None));
        graph.SetRoot(graph.AddReferencePose());
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(1f / 60f);

        Assert.NotEqual(7f, instance.EvaluateValueNode(picked).AsFloat());
    }

    [Fact]
    public void FloatSelector_PicksFirstTrueCondition()
    {
        var (g, root) = NewGraph();
        int sel = g.AddFloatSelector(
            new[] { g.AddConstBool(false), g.AddConstBool(true) },
            new[] { 1f, 2f },
            defaultValue: 9f);
        g.SetRoot(root);

        AnimationGraphInstance i = g.CreateInstance(TestSkeletons.MakeChain());
        Assert.Equal(2.0, (double)i.EvaluateValueNode(sel).AsFloat(), 4);
    }
}
