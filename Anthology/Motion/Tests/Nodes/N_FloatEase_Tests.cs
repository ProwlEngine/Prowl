using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Float Ease node: easing toward a moving target the same way at any frame rate.</summary>
public class N_FloatEase_Tests
{
    private static readonly StringID Channel = new("Value");

    private static Skeleton ChannelRig()
        => new(new[] { new StringID("Root") }, new[] { Skeleton.InvalidIndex }, new[] { Transform3D.Identity }, -1, new[] { Channel });

    // Writes a value node onto a float channel of the pose, which is how the graph itself reads it.
    private static int ShowOnChannel(AnimationGraph graph, Skeleton skeleton, int value)
        => graph.AddFloatChannelLayer(graph.AddClip(TestClips.Const(skeleton, 0f)), new[] { new ChannelDriver(Channel, value) });

    private static float EaseRamp(int fps)
    {
        var g = new AnimationGraph();
        int input = g.AddFloatParameter("In");
        int ease = g.AddFloatEase(input, 0.5f, EasingOp.EaseIn);
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        float value = 0f;
        for (int i = 1; i <= fps; i++)
        {
            instance.SetFloat("In", 10f * i / fps);
            instance.Update(1f / fps);
            value = instance.EvaluateValueNode(ease).AsFloat();
        }
        return value;
    }

    [Fact]
    public void FloatEase_ChasingAMovingTargetStopsAllocatingOnceWarm()
    {
        Skeleton skeleton = ChannelRig();

        // Each attempt is a fresh graph, and the lowest counts, so the runtime warming up in the
        // background cannot fail it while a list that keeps growing allocates in every one.
        long lowest = long.MaxValue;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var graph = new AnimationGraph();
            graph.SetRoot(ShowOnChannel(graph, skeleton, graph.AddFloatEase(graph.AddTimer(), 20f, EasingOp.Linear)));
            AnimationGraphInstance instance = graph.CreateInstance(skeleton);
            for (int i = 0; i < 200; i++)
                instance.Update(1f / 1000f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4000; i++)
                instance.Update(1f / 1000f);
            lowest = Math.Min(lowest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, lowest);
    }

    [Fact]
    public void FloatEase_ContinuousInput_ConvergesAsTheFrameRateRises()
    {
        float at30 = EaseRamp(30);
        float at240 = EaseRamp(240);
        float at1000 = EaseRamp(1000);

        Assert.InRange(MathF.Abs(at240 - at1000), 0f, 0.05f);
        Assert.InRange(MathF.Abs(at30 - at1000), 0f, 1f);
    }

    [Fact]
    public void FloatEase_LateJumpToANewTarget_EasesInsteadOfSnapping()
    {
        var g = new AnimationGraph();
        int input = g.AddFloatParameter("In");
        int ease = g.AddFloatEase(input, 1f, EasingOp.Linear);
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        instance.Update(0.01f);
        instance.EvaluateValueNode(ease);
        instance.SetFloat("In", 1f);
        for (int i = 0; i < 99; i++)
        {
            instance.Update(0.01f);
            instance.EvaluateValueNode(ease);
        }
        instance.SetFloat("In", 11f);
        instance.Update(0.01f);

        Assert.InRange(instance.EvaluateValueNode(ease).AsFloat(), 0.9f, 1.2f);
    }

    [Theory]
    [InlineData(EasingOp.Linear, 0.7f)]
    [InlineData(EasingOp.EaseIn, 0.667f)]
    public void FloatEase_SlowInput_MatchesAcrossFrameRates(EasingOp easing, float expected)
    {
        foreach (int fps in new[] { 30, 60, 240 })
        {
            var g = new AnimationGraph();
            int input = g.AddFloatParameter("In");
            int ease = g.AddFloatEase(input, 0.5f, easing);
            g.SetRoot(g.AddReferencePose());
            AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

            float value = 0f;
            for (int i = 1; i <= fps * 2; i++)
            {
                instance.SetFloat("In", 0.4f * i / fps);
                instance.Update(1f / fps);
                value = instance.EvaluateValueNode(ease).AsFloat();
            }
            Assert.InRange(value, expected - 0.01f, expected + 0.01f);
        }
    }

    [Fact]
    public void FloatEase_AfterDaysOfRuntime_StillEases()
    {
        var g = new AnimationGraph();
        int input = g.AddFloatParameter("In");
        int ease = g.AddFloatEase(input, 0.5f, EasingOp.Linear);
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        instance.Update(0.01f);
        instance.EvaluateValueNode(ease);
        for (int i = 0; i < 100; i++)
            instance.Update(6000f);
        instance.EvaluateValueNode(ease);

        instance.SetFloat("In", 1f);
        for (int i = 0; i < 25; i++)
        {
            instance.Update(0.01f);
            instance.EvaluateValueNode(ease);
        }

        Assert.Equal(0.5, (double)instance.EvaluateValueNode(ease).AsFloat(), 2);
    }
}
