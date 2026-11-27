namespace Prowl.Motion.Tests;

/// <summary>The Float Range Comparison node.</summary>
public class N_FloatRange_Tests
{
    private static AnimationGraphInstance Build(AnimationGraph graph)
    {
        graph.SetRoot(graph.AddReferencePose());
        return graph.CreateInstance(TestSkeletons.MakeChain());
    }

    [Theory]
    [InlineData(5f, true)]
    [InlineData(-1f, false)]
    [InlineData(11f, false)]
    public void FloatRange_HoldsOnlyInsideItsBounds(float value, bool expected)
    {
        var g = new AnimationGraph();
        int inside = g.AddFloatRangeComparison(g.AddConstFloat(value), 0f, 10f);

        Assert.Equal(expected, Build(g).EvaluateValueNode(inside).AsBool());
    }
}
