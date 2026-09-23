using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Float Spring node: a value following another on a damped spring.</summary>
public class N_FloatSpring_Tests
{
    private static readonly StringID Channel = new("Value");

    private static Skeleton ChannelRig()
        => new(new[] { new StringID("Root") }, new[] { Skeleton.InvalidIndex }, new[] { Transform3D.Identity }, -1, new[] { Channel });

    // Writes a value node onto a float channel of the pose, which is how the graph itself reads it.
    private static int ShowOnChannel(AnimationGraph graph, Skeleton skeleton, int value)
        => graph.AddFloatChannelLayer(graph.AddClip(TestClips.Const(skeleton, 0f)), new[] { new ChannelDriver(Channel, value) });

    [Theory]
    [InlineData(30f)]
    [InlineData(24f)]
    [InlineData(20f)]
    public void FloatSpring_StaysStableAtLowFrameRates(float fps)
    {
        Skeleton skeleton = ChannelRig();
        var graph = new AnimationGraph();
        int target = graph.AddFloatParameter("Target");
        graph.SetRoot(ShowOnChannel(graph, skeleton, graph.AddFloatSpring(target)));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / fps);
        instance.SetFloat("Target", 1f);
        for (int i = 0; i < fps * 10f; i++)
            instance.Update(1f / fps);

        Assert.Equal(1f, instance.Pose.GetFloat(0), 2);
    }

    [Fact]
    public void Spring_LagsBehindAStepAndThenReachesIt()
    {
        Skeleton skeleton = ChannelRig();
        var graph = new AnimationGraph();
        int target = graph.AddFloatParameter("Target");
        int spring = graph.AddFloatSpring(target, frequency: 3f);
        int clip = graph.AddClip(TestClips.Const(skeleton, 0f));
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Channel, spring) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);
        instance.SetFloat("Target", 10f);
        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.True(instance.Pose.GetFloat(0) < 5f, $"the spring snapped straight to {instance.Pose.GetFloat(0):N2}");

        for (int i = 0; i < 180; i++)
            instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(10f, instance.Pose.GetFloat(0), 1);
    }

    [Fact]
    public void Spring_UnderdampedOvershootsItsTarget()
    {
        Skeleton skeleton = ChannelRig();
        var graph = new AnimationGraph();
        int target = graph.AddFloatParameter("Target", 1f);
        int spring = graph.AddFloatSpring(target, frequency: 4f, damping: 0.2f);
        int clip = graph.AddClip(TestClips.Const(skeleton, 0f));
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Channel, spring) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);
        instance.SetFloat("Target", 0f);

        float lowest = 0f;
        for (int i = 0; i < 120; i++)
        {
            instance.Update(1f / 60f, Transform3D.Identity);
            lowest = MathF.Min(lowest, instance.Pose.GetFloat(0));
        }

        Assert.True(lowest < -0.05f, $"a lightly damped spring should overshoot, lowest was {lowest:N3}");
    }

    [Fact]
    public void Spring_CriticallyDamped_DoesNotOvershoot()
    {
        Skeleton skeleton = ChannelRig();
        var graph = new AnimationGraph();
        int target = graph.AddFloatParameter("Target", 1f);
        int spring = graph.AddFloatSpring(target, frequency: 4f, damping: 1f);
        int clip = graph.AddClip(TestClips.Const(skeleton, 0f));
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Channel, spring) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);
        instance.SetFloat("Target", 0f);

        for (int i = 0; i < 120; i++)
        {
            instance.Update(1f / 60f, Transform3D.Identity);
            Assert.True(instance.Pose.GetFloat(0) > -0.01f, $"it overshot to {instance.Pose.GetFloat(0):N3}");
        }
    }
}
