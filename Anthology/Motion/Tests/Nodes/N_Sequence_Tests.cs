using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>The Sequence node: playing its children one after another.</summary>
public class N_Sequence_Tests
{
    private static readonly Skeleton s_skeleton = TestSkeletons.MakeChain();

    private static float RootZ(AnimationGraphInstance instance) => instance.Pose.GetTransform(0).position.Z;

    // Each child is a one second clip holding a distinct Z, so the output says which one is playing.
    private static int[] Marked(AnimationGraph graph, params float[] values)
    {
        var children = new int[values.Length];
        for (int i = 0; i < values.Length; i++)
            children[i] = graph.AddClip(TestClips.Const(s_skeleton, values[i]), loop: false);
        return children;
    }

    private static List<float> Observe(AnimationGraphInstance instance, int frames, float step = 0.25f)
    {
        var seen = new List<float>();
        for (int i = 0; i < frames; i++)
        {
            instance.Update(step, Transform3D.Identity);
            seen.Add(RootZ(instance));
        }
        return seen;
    }

    [Fact]
    public void Sequence_PlaysItsChildrenInOrderAndHoldsTheLast()
    {
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddSequence(Marked(graph, 1f, 2f, 3f)));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        List<float> seen = Observe(instance, 16);

        Assert.Equal(new[] { 1f, 2f, 3f }, seen.Distinct().ToArray());
        Assert.Equal(3f, seen[^1]);
        Assert.True(seen.IndexOf(2f) < seen.IndexOf(3f));
    }

    [Fact]
    public void Sequence_LoopsBackToTheStartWhenAskedTo()
    {
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddSequence(Marked(graph, 1f, 2f), loop: true));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        List<float> seen = Observe(instance, 24);

        Assert.True(seen.Count(v => v == 1f) > 4, "the sequence never came back around");
        Assert.Equal(1f, seen[0]);
    }

    [Fact]
    public void Sequence_WithOneChild_JustPlaysIt()
    {
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddSequence(Marked(graph, 7f)));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        Assert.All(Observe(instance, 8), value => Assert.Equal(7f, value));
    }

    [Fact]
    public void Sequence_RestartsFromTheFirstChildWhenReentered()
    {
        var graph = new AnimationGraph();
        int on = graph.AddBoolParameter("On", true);
        int[] children = Marked(graph, 1f, 2f);
        int sequence = graph.AddSequence(children);
        int idle = graph.AddClip(TestClips.Const(s_skeleton, 9f));
        graph.SetRoot(graph.AddConditionSelector(new[] { (sequence, on), (idle, -1) }));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        Observe(instance, 8);
        instance.SetBool("On", false);
        Observe(instance, 2);
        instance.SetBool("On", true);

        Assert.Equal(1f, Observe(instance, 1)[0]);
    }
}
