using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Pose Smoothing node: damping a pose so it lags and settles.</summary>
public class N_PoseSmoothing_Tests
{
    private static Skeleton Rig() => TestSkeletons.MakeChain();

    private static float RootZ(AnimationGraphInstance instance) => instance.Pose.GetTransform(0).position.Z;

    private static void Run(AnimationGraphInstance instance, int frames, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

    // A clip whose one bone rises from nothing to full height over a second.
    private static AnimationClip Rise(Skeleton skeleton)
    {
        var low = new Pose(skeleton);
        low.SetToReferencePose();
        low.SetTransform(0, new Transform3D(Float3.Zero, Quaternion.Identity, Float3.One));

        var high = new Pose(skeleton);
        high.SetToReferencePose();
        high.SetTransform(0, new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One));

        return new AnimationClip(skeleton, new[] { low, high }, 1f);
    }

    private static Skeleton OneBone() => new(
        new[] { new StringID("Bone") },
        new[] { Skeleton.InvalidIndex },
        new[] { Transform3D.Identity });

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

    /// <summary>A driven setting is read every update, so changing what drives it changes the node.</summary>
    [Fact]
    public void PoseSmoothing_ReadsItsHalfLifeFromTheGraph()
    {
        Skeleton skeleton = OneBone();

        float Run(FloatInput halfLife)
        {
            var graph = new AnimationGraph();
            int clip = graph.AddClip(Rise(skeleton));
            graph.SetRoot(graph.AddPoseSmoothing(clip, halfLife));

            AnimationGraphInstance instance = graph.CreateInstance(skeleton);
            for (int frame = 0; frame < 30; frame++) instance.Update(1f / 60f);
            return (float)instance.Pose.GetTransform(0).position.Y;
        }

        var driver = new AnimationGraph();
        int constant = driver.AddConstFloat(0.5f);
        driver.SetRoot(driver.AddPoseSmoothing(driver.AddClip(Rise(skeleton)), FloatInput.From(constant)));

        AnimationGraphInstance driven = driver.CreateInstance(skeleton);
        for (int frame = 0; frame < 30; frame++) driven.Update(1f / 60f);

        float direct = Run(0f);
        Assert.Equal(0.5, (double)direct, 1);
        Assert.True((float)driven.Pose.GetTransform(0).position.Y < direct - 0.05f);
    }

    [Fact]
    public void PoseSmoothing_KeepsFollowingWhilePlayingBackward()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddPoseSmoothing(graph.AddClip(TestClips.Ramp(skeleton, endZ: 10f, duration: 1f)), 0.05f));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0f);
        for (int i = 0; i < 30; i++)
            instance.Update(-1f / 60f);

        Assert.True(TestClips.RootZ(instance) > 3f, $"the pose froze at {TestClips.RootZ(instance):N3}");
    }
}
