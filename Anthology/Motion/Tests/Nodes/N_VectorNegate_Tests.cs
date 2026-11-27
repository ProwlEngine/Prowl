using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Vector Negate node, fed by a Vector Create node.</summary>
public class N_VectorNegate_Tests
{
    private static AnimationGraphInstance Build(AnimationGraph graph)
    {
        graph.SetRoot(graph.AddReferencePose());
        return graph.CreateInstance(TestSkeletons.MakeChain());
    }

    [Fact]
    public void VectorNegate_PointsTheVectorTheOtherWay()
    {
        var g = new AnimationGraph();
        int negated = g.AddVectorNegate(g.AddVectorCreate(g.AddConstFloat(1f), g.AddConstFloat(-2f), g.AddConstFloat(3f)));

        Float3 n = Build(g).EvaluateValueNode(negated).Vector;
        Assert.Equal((-1f, 2f, -3f), (n.X, n.Y, n.Z));
    }
}
