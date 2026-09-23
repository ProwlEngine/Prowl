namespace Prowl.Motion.Tests;

/// <summary>The Id To Float node.</summary>
public class N_IdToFloat_Tests
{
    private static AnimationGraphInstance Build(AnimationGraph graph)
    {
        graph.SetRoot(graph.AddReferencePose());
        return graph.CreateInstance(TestSkeletons.MakeChain());
    }

    [Fact]
    public void IdToFloat_GivesTheNumberBesideTheIdOrItsDefault()
    {
        var g = new AnimationGraph();
        int id = g.AddIdParameter("Id", new StringID("A"));
        int number = g.AddIdToFloat(id, new[] { new StringID("A"), new StringID("B") }, new[] { 10f, 20f }, defaultValue: -1f);
        AnimationGraphInstance instance = Build(g);

        Assert.Equal(10f, instance.EvaluateValueNode(number).AsFloat(), 4);
        instance.SetId("Id", new StringID("C"));
        Assert.Equal(-1f, instance.EvaluateValueNode(number).AsFloat(), 4);
    }
}
