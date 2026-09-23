using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Weighted Blend node: mixing any number of poses by weight, with their events.</summary>
public class N_WeightedBlend_Tests
{
    private static Skeleton Rig() => TestSkeletons.MakeChain();

    private static float RootZ(AnimationGraphInstance instance) => instance.Pose.GetTransform(0).position.Z;

    private static void Run(AnimationGraphInstance instance, int frames, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

    [Theory]
    [InlineData(1f, 0f, 0f, 0f)]
    [InlineData(0f, 1f, 0f, 5f)]
    [InlineData(1f, 1f, 0f, 2.5f)]
    [InlineData(1f, 1f, 2f, 6.25f)]
    public void WeightedBlend_MixesByProportion(float a, float b, float c, float expected)
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int wa = graph.AddFloatParameter("A", a);
        int wb = graph.AddFloatParameter("B", b);
        int wc = graph.AddFloatParameter("C", c);
        graph.SetRoot(graph.AddWeightedBlend(new[]
        {
            new WeightedPose(graph.AddClip(TestClips.Const(skeleton, 0f)), wa),
            new WeightedPose(graph.AddClip(TestClips.Const(skeleton, 5f)), wb),
            new WeightedPose(graph.AddClip(TestClips.Const(skeleton, 10f)), wc),
        }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 2);

        Assert.Equal(expected, RootZ(instance), 3);
    }

    [Fact]
    public void WeightedBlend_WithNoWeightAtAll_FallsBackToTheFirstInput()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int zero = graph.AddFloatParameter("Zero");
        graph.SetRoot(graph.AddWeightedBlend(new[]
        {
            new WeightedPose(graph.AddClip(TestClips.Const(skeleton, 3f)), zero),
            new WeightedPose(graph.AddClip(TestClips.Const(skeleton, 9f)), zero),
        }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 2);

        Assert.Equal(3f, RootZ(instance), 3);
    }

    [Fact]
    public void WeightedBlend_IgnoresNegativeAndNonFiniteWeights()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int bad = graph.AddFloatParameter("Bad", float.NaN);
        int negative = graph.AddFloatParameter("Negative", -3f);
        int good = graph.AddFloatParameter("Good", 1f);
        graph.SetRoot(graph.AddWeightedBlend(new[]
        {
            new WeightedPose(graph.AddClip(TestClips.Const(skeleton, 1f)), bad),
            new WeightedPose(graph.AddClip(TestClips.Const(skeleton, 2f)), negative),
            new WeightedPose(graph.AddClip(TestClips.Const(skeleton, 8f)), good),
        }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 2);

        Assert.Equal(8f, RootZ(instance), 3);
    }

    [Fact]
    public void WeightedBlend_FiresEachInputsEventsAsStronglyAsItShows()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int seen = graph.AddClip(TestClips.Const(skeleton, 0f, new IdEvent(new StringID("Seen"), 0f, 1f)));
        int hidden = graph.AddClip(TestClips.Const(skeleton, 0f, new IdEvent(new StringID("Hidden"), 0f, 1f)));
        graph.SetRoot(graph.AddWeightedBlend(new[] { new WeightedPose(seen, -1, 1f), new WeightedPose(hidden, -1, 0f) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f);

        Assert.Equal(2, instance.Events.Count(e => e.Event is IdEvent));
        foreach (SampledEvent e in instance.Events)
            if (e.Event is IdEvent id)
                Assert.Equal(id.Id == new StringID("Hidden") ? 0f : 1f, e.Weight, 4);
    }
}
