namespace Prowl.Motion.Tests;

/// <summary>The Float Abs node.</summary>
public class N_FloatAbs_Tests
{
    private static AnimationGraphInstance Build(AnimationGraph graph)
    {
        graph.SetRoot(graph.AddReferencePose());
        return graph.CreateInstance(TestSkeletons.MakeChain());
    }

    [Fact]
    public void FloatAbs_DropsTheSign()
    {
        var g = new AnimationGraph();
        int abs = g.AddFloatAbs(g.AddConstFloat(-3.5f));

        Assert.Equal(3.5f, Build(g).EvaluateValueNode(abs).AsFloat(), 4);
    }
}
