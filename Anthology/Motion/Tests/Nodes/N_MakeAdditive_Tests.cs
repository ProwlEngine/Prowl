using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Make Additive node: a pose measured as a difference from another.</summary>
public class N_MakeAdditive_Tests
{
    private static Skeleton Rig() => TestSkeletons.MakeChain();

    private static float RootZ(AnimationGraphInstance instance) => instance.Pose.GetTransform(0).position.Z;

    private static void Run(AnimationGraphInstance instance, int frames, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

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
}
