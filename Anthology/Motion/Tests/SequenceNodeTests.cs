using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>The random selector and the sequence node.</summary>
public class SequenceNodeTests
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

    // ---- sequence ------------------------------------------------------------------------------

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

    // ---- random selector -----------------------------------------------------------------------

    [Fact]
    public void RandomSelector_PlaysSomethingAndKeepsPickingAsClipsFinish()
    {
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddRandomSelector(Marked(graph, 1f, 2f, 3f), seed: 12345));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        List<float> seen = Observe(instance, 40);

        Assert.All(seen, value => Assert.Contains(value, new[] { 1f, 2f, 3f }));
        Assert.True(seen.Distinct().Count() > 1, "the selector never picked a different child");
    }

    [Fact]
    public void RandomSelector_WithTheSameSeed_PicksTheSameOrder()
    {
        List<float> Play(uint seed)
        {
            var graph = new AnimationGraph();
            graph.SetRoot(graph.AddRandomSelector(Marked(graph, 1f, 2f, 3f, 4f), seed: seed));
            return Observe(graph.CreateInstance(s_skeleton), 40);
        }

        Assert.Equal(Play(999), Play(999));
        Assert.NotEqual(Play(999), Play(1000));
    }

    [Fact]
    public void RandomSelector_DoesNotRepeatTheSameChildBackToBack()
    {
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddRandomSelector(Marked(graph, 1f, 2f, 3f), seed: 7));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        List<float> runs = Observe(instance, 60).Aggregate(new List<float>(), (list, value) =>
        {
            if (list.Count == 0 || list[^1] != value)
                list.Add(value);
            return list;
        });

        for (int i = 1; i < runs.Count; i++)
            Assert.NotEqual(runs[i - 1], runs[i]);
    }

    [Fact]
    public void RandomSelector_RespectsItsWeights()
    {
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddRandomSelector(Marked(graph, 1f, 2f), new[] { 1f, 0f }, seed: 4242));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        // The second child has no weight, so only a forced no repeat pick can ever reach it.
        List<float> seen = Observe(instance, 40);

        Assert.True(seen.Count(v => v == 1f) > seen.Count(v => v == 2f) * 2, "the weights were ignored");
    }

    [Fact]
    public void RandomSelector_WithOneChild_KeepsPlayingIt()
    {
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddRandomSelector(Marked(graph, 5f), seed: 1));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        Assert.All(Observe(instance, 20), value => Assert.Equal(5f, value));
    }

    [Fact]
    public void RandomSelector_CanKeepTheChildItPicked()
    {
        var graph = new AnimationGraph();
        var definition = new RandomSelectorDefinition(Marked(graph, 1f, 2f, 3f)) { Seed = 3, RepickWhenFinished = false };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        Assert.Single(Observe(instance, 30).Distinct());
    }
}
