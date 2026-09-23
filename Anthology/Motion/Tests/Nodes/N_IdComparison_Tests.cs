namespace Prowl.Motion.Tests;

/// <summary>The Id Comparison node.</summary>
public class N_IdComparison_Tests
{
    private static AnimationGraphInstance Build(AnimationGraph graph)
    {
        graph.SetRoot(graph.AddReferencePose());
        return graph.CreateInstance(TestSkeletons.MakeChain());
    }

    [Fact]
    public void IdComparison_MatchesAnIdInItsList()
    {
        var g = new AnimationGraph();
        int id = g.AddIdParameter("Id", new StringID("A"));
        int matches = g.AddIdComparison(id, IdComparison.Matches, new[] { new StringID("A"), new StringID("B") });
        AnimationGraphInstance instance = Build(g);

        Assert.True(instance.EvaluateValueNode(matches).AsBool());
        instance.SetId("Id", new StringID("C"));
        Assert.False(instance.EvaluateValueNode(matches).AsBool());
    }
}
