using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>What a graph refuses to be built from.</summary>
public class GraphValidation_Tests
{
    private static AnimationClip Ramp(Skeleton skeleton, float rootTravel = 0f, params AnimationEvent[] events)
        => TestClips.Ramp(skeleton, rootTravel: rootTravel, events: events);

    [Fact]
    public void MissingParameter_Throws()
    {
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddReferencePose());
        AnimationGraphInstance instance = graph.CreateInstance(TestSkeletons.MakeChain());
        Assert.Throws<ArgumentException>(() => instance.SetFloat("Nope", 1f));
    }

    [Fact]
    public void NoRoot_ThrowsOnCreate()
    {
        var graph = new AnimationGraph();
        graph.AddReferencePose();
        Assert.Throws<InvalidOperationException>(() => graph.CreateInstance(TestSkeletons.MakeChain()));
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

    [Fact]
    public void SharedPoseNode_IsRejected()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int clip = g.AddClip(Ramp(skeleton));
        int sm = g.AddStateMachine();
        g.AddState(sm, clip);
        g.AddState(sm, clip);
        g.SetRoot(sm);

        var ex = Assert.Throws<GraphValidationException>(() => g.CreateInstance(skeleton));
        Assert.Equal(clip, ex.NodeIndex);
    }

    [Fact]
    public void Validation_RejectsMissingTransitionTarget()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        g.AddTransition(sm, a, 5);
        g.SetRoot(sm);

        Assert.Throws<GraphValidationException>(() => g.CreateInstance(skeleton));
    }

    [Fact]
    public void Validation_RejectsMissingDefaultState()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int sm = g.AddStateMachine();
        g.AddState(sm, g.AddClip(Ramp(skeleton)));
        g.SetStateMachineDefault(sm, 3);
        g.SetRoot(sm);

        Assert.Throws<GraphValidationException>(() => g.CreateInstance(skeleton));
    }

    [Fact]
    public void Validation_RejectsPoseNodeCycles()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var self = new AnimationGraph();
        self.SetRoot(self.AddPassthrough(0));
        Assert.Throws<GraphValidationException>(() => self.CreateInstance(skeleton));

        var loop = new AnimationGraph();
        int first = loop.AddPassthrough(1);
        loop.AddPassthrough(first);
        loop.SetRoot(first);
        Assert.Throws<GraphValidationException>(() => loop.CreateInstance(skeleton));
    }

    [Fact]
    public void Validation_RejectsASubGraphReferencingItself()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        g.SetRoot(g.AddReferencedGraph(g));

        Assert.Throws<GraphValidationException>(() => g.CreateInstance(skeleton));
    }

    [Fact]
    public void Validation_RejectsSubGraphLinksToMissingParameters()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var child = new AnimationGraph();
        child.AddFloatParameter("Speed");
        child.SetRoot(child.AddClip(Ramp(skeleton)));

        var g = new AnimationGraph();
        int value = g.AddFloatParameter("Value");
        int sub = g.AddReferencedGraph(child);
        g.LinkGraphParameter(sub, value, "Missing");
        g.SetRoot(sub);

        Assert.Throws<GraphValidationException>(() => g.CreateInstance(skeleton));
    }

    [Fact]
    public void NodeNames_MustBeUnique()
    {
        var g = new AnimationGraph();
        g.AddFloatParameter("slot");
        Assert.Throws<ArgumentException>(() => g.AddExternalGraphSlot("slot"));
    }

    [Fact]
    public void APoseNodeListedTwiceByOneParent_SaysSo()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(TestClips.Const(skeleton, 0f));
        graph.SetRoot(graph.AddBlend1D(graph.AddConstFloat(0.5f), new[] { (clip, 0f), (clip, 1f) }));

        GraphValidationException error = Assert.Throws<GraphValidationException>(() => graph.CreateInstance(skeleton));
        Assert.Contains("listed twice", error.Message);
    }

    [Fact]
    public void ValueInputs_OfTheWrongKind_FailValidation()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var compare = new AnimationGraph();
        compare.AddFloatCompare(compare.AddVectorParameter("V", new Float3(5f, 5f, 5f)), CompareOp.Greater, 1f);
        compare.SetRoot(compare.AddReferencePose());
        Assert.Throws<GraphValidationException>(() => compare.CreateInstance(skeleton));

        var ik = new AnimationGraph();
        ik.SetRoot(ik.AddTwoBoneIK(ik.AddReferencePose(), ik.AddFloatParameter("NotATarget", 3f), 0, 1, 2));
        Assert.Throws<GraphValidationException>(() => ik.CreateInstance(skeleton));
    }
}
