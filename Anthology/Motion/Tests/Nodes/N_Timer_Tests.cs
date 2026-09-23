using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Timer node.</summary>
public class N_Timer_Tests
{
    private static readonly StringID Channel = new("Value");

    private static Skeleton ChannelRig()
        => new(new[] { new StringID("Root") }, new[] { Skeleton.InvalidIndex }, new[] { Transform3D.Identity }, -1, new[] { Channel });

    // Writes a value node onto a float channel of the pose, which is how the graph itself reads it.
    private static int ShowOnChannel(AnimationGraph graph, Skeleton skeleton, int value)
        => graph.AddFloatChannelLayer(graph.AddClip(TestClips.Const(skeleton, 0f)), new[] { new ChannelDriver(Channel, value) });

    private static (AnimationGraphInstance Instance, Func<float> Read) Probe(Func<AnimationGraph, int> value)
    {
        Skeleton skeleton = ChannelRig();
        var graph = new AnimationGraph();
        int node = value(graph);
        int clip = graph.AddClip(TestClips.Const(skeleton, 0f));
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Channel, node) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        return (instance, () => instance.Pose.GetFloat(0));
    }

    private static List<float> Sample(AnimationGraphInstance instance, Func<float> read, int frames, float step = 1f / 60f)
    {
        var values = new List<float>(frames);
        for (int i = 0; i < frames; i++)
        {
            instance.Update(step, Transform3D.Identity);
            values.Add(read());
        }
        return values;
    }

    [Fact]
    public void Timer_StepsByTheFrameWhereverItIsFirstRead()
    {
        Skeleton skeleton = ChannelRig();
        var graph = new AnimationGraph();
        // The timer is first read inside a branch playing at speed 0.
        graph.SetRoot(graph.AddSpeedScale(ShowOnChannel(graph, skeleton, graph.AddTimer()), defaultSpeed: 0f));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        for (int i = 0; i < 60; i++)
            instance.Update(1f / 60f);

        Assert.Equal(1f, instance.Pose.GetFloat(0), 2);
    }

    [Fact]
    public void Timer_CountsTheSecondsItHasBeenRunning()
    {
        (AnimationGraphInstance instance, Func<float> read) = Probe(g => g.AddTimer());

        Sample(instance, read, 60);

        Assert.Equal(1f, read(), 2);
    }

    [Fact]
    public void Timer_WrapsAtItsLoopLength()
    {
        (AnimationGraphInstance instance, Func<float> read) = Probe(g => g.AddTimer(loopSeconds: 0.5f, normalized: true));

        Sample(instance, read, 45);

        Assert.Equal(0.5f, read(), 2);
    }

    [Fact]
    public void Timer_HoldsAtZeroWhileItsResetIsSet()
    {
        Skeleton skeleton = ChannelRig();
        var graph = new AnimationGraph();
        int hold = graph.AddBoolParameter("Hold", true);
        int timer = graph.AddTimer(resetNodeIndex: hold);
        int clip = graph.AddClip(TestClips.Const(skeleton, 0f));
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Channel, timer) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        for (int i = 0; i < 30; i++)
            instance.Update(1f / 60f, Transform3D.Identity);
        Assert.Equal(0f, instance.Pose.GetFloat(0), 4);

        instance.SetBool("Hold", false);
        for (int i = 0; i < 30; i++)
            instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(0.5f, instance.Pose.GetFloat(0), 2);
    }
}
