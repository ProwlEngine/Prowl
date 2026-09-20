namespace Prowl.Motion.Tests;

public class GraphEditorApiTests
{
    [Fact]
    public void NamedNodes_AreFindableByName()
    {
        var graph = new AnimationGraph();
        int refPose = graph.AddReferencePose();
        graph.NameNode(refPose, "Base");
        graph.SetRoot(refPose);

        Assert.Equal(refPose, graph.GetNodeIndex("Base"));
        Assert.Equal(-1, graph.GetNodeIndex("Missing"));
    }

    [Fact]
    public void BadChildKind_ThrowsGraphValidationException()
    {
        // Wire a Blend1D's parameter pin to a pose node (wrong kind) -> bind should report it.
        var graph = new AnimationGraph();
        int refPose = graph.AddReferencePose();
        int badBlend = graph.AddBlend1D(refPose, new[] { (refPose, 0f) }); // refPose used as the float parameter
        graph.SetRoot(badBlend);

        var ex = Assert.Throws<GraphValidationException>(() => graph.CreateInstance(TestSkeletons.MakeChain()));
        Assert.Equal(badBlend, ex.NodeIndex);
    }
}
