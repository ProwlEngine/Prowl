using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>The Is Target Set node.</summary>
public class N_IsTargetSet_Tests
{
    private static AnimationGraphInstance Build(AnimationGraph graph)
    {
        graph.SetRoot(graph.AddReferencePose());
        return graph.CreateInstance(TestSkeletons.MakeChain());
    }

    [Fact]
    public void IsTargetSet_HoldsOnceTheTargetIsGivenAValue()
    {
        var g = new AnimationGraph();
        int isSet = g.AddIsTargetSet(g.AddTargetParameter("T"));
        AnimationGraphInstance instance = Build(g);

        Assert.False(instance.EvaluateValueNode(isSet).AsBool());
        instance.SetTarget("T", Target.FromWorld(new Transform3D(new Float3(0f, 0f, 4f), Quaternion.Identity, Float3.One)));
        Assert.True(instance.EvaluateValueNode(isSet).AsBool());
    }
}
