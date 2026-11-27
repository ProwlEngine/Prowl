using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Inertialize node: smoothing a sudden change in its child over time.</summary>
public class N_Inertialize_Tests
{
    private static Skeleton Rig() => TestSkeletons.MakeChain();

    private static float RootZ(AnimationGraphInstance instance) => instance.Pose.GetTransform(0).position.Z;

    private static void Run(AnimationGraphInstance instance, int frames, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

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
}
