using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Noise node: a smooth wandering value.</summary>
public class N_Noise_Tests
{
    // Reads a value node by driving a clip's playback speed with it would be indirect, so the value is
    // written onto a float channel instead, where a test can read it straight off the pose.
    private static readonly StringID Channel = new("Value");

    private static Skeleton ChannelRig()
        => new(new[] { new StringID("Root") }, new[] { Skeleton.InvalidIndex }, new[] { Transform3D.Identity }, -1, new[] { Channel });

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
    public void Noise_StaysWithinItsAmplitudeAndKeepsMoving()
    {
        (AnimationGraphInstance instance, Func<float> read) = Probe(g => g.AddNoise(frequency: 2f, amplitude: 3f, seed: 5));

        List<float> values = Sample(instance, read, 240);

        Assert.All(values, v => Assert.InRange(v, -3f, 3f));
        Assert.True(values.Distinct().Count() > 100, "the noise barely moved");
        Assert.True(values.Max() > 1f && values.Min() < -1f, "the noise never used its range");
    }

    // Frame to frame it has to be smooth, or it reads as jitter rather than sway.
    [Fact]
    public void Noise_MovesSmoothlyBetweenFrames()
    {
        (AnimationGraphInstance instance, Func<float> read) = Probe(g => g.AddNoise(frequency: 1f, amplitude: 1f, seed: 9));

        List<float> values = Sample(instance, read, 240);

        for (int i = 1; i < values.Count; i++)
            Assert.True(MathF.Abs(values[i] - values[i - 1]) < 0.2f, $"noise jumped {MathF.Abs(values[i] - values[i - 1]):N3} in one frame");
    }

    [Fact]
    public void Noise_WithTheSameSeed_RepeatsExactly()
    {
        List<float> Play(uint seed)
        {
            (AnimationGraphInstance instance, Func<float> read) = Probe(g => g.AddNoise(frequency: 3f, amplitude: 1f, seed: seed));
            return Sample(instance, read, 60);
        }

        Assert.Equal(Play(11), Play(11));
        Assert.NotEqual(Play(11), Play(12));
    }
}
