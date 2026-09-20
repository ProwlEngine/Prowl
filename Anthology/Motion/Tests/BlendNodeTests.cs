using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>The blending nodes: inertialization, snapshot, make additive, weighted blend, smoothing.</summary>
public class BlendNodeTests
{
    private static Skeleton Rig() => TestSkeletons.MakeChain();

    private static float RootZ(AnimationGraphInstance instance) => instance.Pose.GetTransform(0).position.Z;

    private static void Run(AnimationGraphInstance instance, int frames, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

    // ---- inertialize ---------------------------------------------------------------------------

    [Fact]
    public void Inertialize_AbsorbsASelectorSwitch()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int which = graph.AddIntParameter("Which");
        int low = graph.AddClip(TestClips.Const(skeleton, 0f));
        int high = graph.AddClip(TestClips.Const(skeleton, 10f));
        int selector = graph.AddSelector(which, new[] { low, high });
        graph.SetRoot(graph.AddInertialBlend(selector, 0.3f));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 4);
        instance.SetInt("Which", 1);
        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.True(RootZ(instance) < 1f, $"the switch popped to {RootZ(instance):N2}");

        Run(instance, 30);
        Assert.Equal(10f, RootZ(instance), 2);
    }

    [Fact]
    public void Inertialize_FollowsItsChildWhenNothingJumps()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddInertialBlend(graph.AddClip(TestClips.Ramp(skeleton, endZ: 10f), loop: false), 0.3f));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 30, 1f / 30f);

        Assert.Equal(10f, RootZ(instance), 2);
    }

    [Fact]
    public void Inertialize_StartsOnItsTriggerWhenGivenOne()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int go = graph.AddBoolParameter("Go");
        int which = graph.AddIntParameter("Which");
        int selector = graph.AddSelector(which, new[] { graph.AddClip(TestClips.Const(skeleton, 0f)), graph.AddClip(TestClips.Const(skeleton, 10f)) });
        graph.SetRoot(graph.AddInertialBlend(selector, 0.3f, go));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 4);
        instance.SetInt("Which", 1);
        instance.SetBool("Go", true);
        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.True(RootZ(instance) < 1f, $"the triggered blend popped to {RootZ(instance):N2}");
    }

    // ---- snapshot ------------------------------------------------------------------------------

    [Fact]
    public void Snapshot_HoldsThePoseWhileHeld()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int hold = graph.AddBoolParameter("Hold");
        graph.SetRoot(graph.AddPoseSnapshot(graph.AddClip(TestClips.Ramp(skeleton, endZ: 10f, duration: 1f), loop: false), hold));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 15, 1f / 30f);
        float frozen = RootZ(instance);
        instance.SetBool("Hold", true);
        Run(instance, 10, 1f / 30f);

        Assert.Equal(frozen, RootZ(instance), 3);

        instance.SetBool("Hold", false);
        Run(instance, 15, 1f / 30f);
        Assert.True(RootZ(instance) > frozen, "the clip did not resume after the hold");
    }

    // ---- make additive -------------------------------------------------------------------------

    [Fact]
    public void MakeAdditive_LayersAClipOnTopOfAnother()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int basePose = graph.AddClip(TestClips.Const(skeleton, 4f));
        int additive = graph.AddMakeAdditive(graph.AddClip(TestClips.Const(skeleton, 3f)));
        graph.SetRoot(graph.AddLayerBlend(basePose, new[] { new LayerInfo(additive, additive: true) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 2);

        // The clip sits 3 from the reference pose, so it adds 3 on top of the base's 4.
        Assert.Equal(7f, RootZ(instance), 3);
    }

    [Fact]
    public void MakeAdditive_AgainstAnotherPose_MeasuresTheDifference()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int reference = graph.AddClip(TestClips.Const(skeleton, 2f));
        int additive = graph.AddMakeAdditive(graph.AddClip(TestClips.Const(skeleton, 3f)), reference);
        graph.SetRoot(graph.AddLayerBlend(graph.AddClip(TestClips.Const(skeleton, 4f)), new[] { new LayerInfo(additive, additive: true) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 2);

        Assert.Equal(5f, RootZ(instance), 3);
    }

    [Fact]
    public void MakeAdditive_HalfWeight_AddsHalfTheDifference()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int weight = graph.AddFloatParameter("Weight", 0.5f);
        int additive = graph.AddMakeAdditive(graph.AddClip(TestClips.Const(skeleton, 3f)));
        graph.SetRoot(graph.AddLayerBlend(graph.AddClip(TestClips.Const(skeleton, 4f)), new[] { new LayerInfo(additive, weight, additive: true) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 2);

        Assert.Equal(5.5f, RootZ(instance), 3);
    }

    // ---- weighted blend ------------------------------------------------------------------------

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

    // ---- smoothing -----------------------------------------------------------------------------

    [Fact]
    public void Smoothing_ClosesHalfTheGapPerHalfLife()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int which = graph.AddIntParameter("Which");
        int selector = graph.AddSelector(which, new[] { graph.AddClip(TestClips.Const(skeleton, 0f)), graph.AddClip(TestClips.Const(skeleton, 8f)) });
        graph.SetRoot(graph.AddPoseSmoothing(selector, 0.1f));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 4);
        instance.SetInt("Which", 1);
        Run(instance, 6, 1f / 60f);

        Assert.Equal(4f, RootZ(instance), 1);

        Run(instance, 60);
        Assert.Equal(8f, RootZ(instance), 2);
    }

    [Fact]
    public void Smoothing_StartsOnItsChildRatherThanEasingUpFromNothing()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddPoseSmoothing(graph.AddClip(TestClips.Const(skeleton, 6f)), 0.2f));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(6f, RootZ(instance), 3);
    }
}
