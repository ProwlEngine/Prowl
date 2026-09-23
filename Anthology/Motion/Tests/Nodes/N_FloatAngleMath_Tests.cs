namespace Prowl.Motion.Tests;

/// <summary>The Float Angle Math node.</summary>
public class N_FloatAngleMath_Tests
{
    private static AnimationGraphInstance Build(AnimationGraph graph)
    {
        graph.SetRoot(graph.AddReferencePose());
        return graph.CreateInstance(TestSkeletons.MakeChain());
    }

    [Fact]
    public void FloatAngleMath_WrapsIntoHalfATurnEitherWay()
    {
        var g = new AnimationGraph();
        int angle = g.AddFloatAngleMath(g.AddConstFloat(270f), AngleOp.ClampTo180);

        Assert.Equal(-90f, Build(g).EvaluateValueNode(angle).AsFloat(), 3);
    }
}
