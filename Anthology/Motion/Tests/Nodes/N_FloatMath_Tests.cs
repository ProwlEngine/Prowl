using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Float Math node.</summary>
public class N_FloatMath_Tests
{
    private static float Read(AnimationGraph graph, Skeleton skeleton, int node)
    {
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.016f);
        return instance.EvaluateValueNode(node).AsFloat();
    }

    [Theory]
    [InlineData(FloatMathOp.Absolute, -3f, 3f)]
    [InlineData(FloatMathOp.Negate, 2f, -2f)]
    public void Maths_UnaryOperationsReadAAlone(FloatMathOp op, float a, float expected)
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int math = graph.AddNode(new FloatMathDefinition(graph.AddConstFloat(a), -1, op));
        graph.SetRoot(graph.AddReferencePose());

        Assert.Equal(expected, Read(graph, skeleton, math), 4);
    }
}
