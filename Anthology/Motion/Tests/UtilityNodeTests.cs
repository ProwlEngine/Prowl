using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>Noise, timer, spring, curve and the debug pose node.</summary>
public class UtilityNodeTests
{
    private static readonly Skeleton s_skeleton = TestSkeletons.MakeChain();

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

    // ---- noise ---------------------------------------------------------------------------------

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

    // ---- timer ---------------------------------------------------------------------------------

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

    // ---- spring --------------------------------------------------------------------------------

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

    // ---- curve ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 10f)]
    [InlineData(2f, 10f)]
    public void Curve_ShapesItsInput(float input, float expected)
    {
        var curve = new AnimationCurve();
        curve.AddKey(0f, 0f);
        curve.AddKey(1f, 10f);

        Skeleton skeleton = ChannelRig();
        var graph = new AnimationGraph();
        int value = graph.AddFloatParameter("Value", input);
        int shaped = graph.AddCurve(value, curve);
        int clip = graph.AddClip(TestClips.Const(skeleton, 0f));
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Channel, shaped) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(expected, instance.Pose.GetFloat(0), 2);
    }

    // ---- debug ---------------------------------------------------------------------------------

    [Fact]
    public void DebugPose_HandsEachFrameToItsInspector()
    {
        int seen = 0;
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddDebugPose(graph.AddClip(TestClips.Const(s_skeleton, 3f)), _ => seen++));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        for (int i = 0; i < 5; i++)
            instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(5, seen);
        Assert.Equal(3f, instance.Pose.GetTransform(0).position.Z, 4);
    }

    // One bad solve upstream should not spread through everything downstream.
    [Fact]
    public void DebugPose_PutsABrokenBoneBackToItsReferencePose()
    {
        var poses = new[] { TestClips.At(s_skeleton, 0f) };
        poses[0].SetTransform(1, new Transform3D(new Float3(float.NaN, 0f, 0f), Quaternion.Identity, Float3.One));
        var clip = new AnimationClip(s_skeleton, poses, 1f);

        var graph = new AnimationGraph();
        int debug = graph.AddDebugPose(graph.AddClip(clip));
        graph.SetRoot(debug);
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(s_skeleton.GetBoneParentSpaceTransform(1).position, instance.Pose.GetTransform(1).position);
    }
}
