namespace Prowl.Motion.Tests;

/// <summary>The Float Switch node.</summary>
public class N_FloatSwitch_Tests
{
    private static AnimationGraphInstance Build(AnimationGraph graph)
    {
        graph.SetRoot(graph.AddReferencePose());
        return graph.CreateInstance(TestSkeletons.MakeChain());
    }

    [Fact]
    public void FloatSwitch_PicksTheSideItsSelectorNames()
    {
        var g = new AnimationGraph();
        int selector = g.AddBoolParameter("Selector", true);
        int picked = g.AddFloatSwitch(selector, g.AddConstFloat(11f), g.AddConstFloat(22f));
        AnimationGraphInstance instance = Build(g);

        Assert.Equal(11f, instance.EvaluateValueNode(picked).AsFloat(), 4);
        instance.SetBool("Selector", false);
        Assert.Equal(22f, instance.EvaluateValueNode(picked).AsFloat(), 4);
    }
}
