using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Blend 2D node: blending a field of poses by two values, its triangulation, timing and events.</summary>
public class N_Blend2D_Tests
{
    private static float Blend2D((Float2 Position, float Z)[] samples, float x, float y)
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int px = g.AddFloatParameter("X", x);
        int py = g.AddFloatParameter("Y", y);
        var entries = new (int Child, Float2 Position)[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            entries[i] = (g.AddClip(TestClips.Const(skeleton, samples[i].Z)), samples[i].Position);
        g.SetRoot(g.AddBlend2D(px, py, entries));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);
        instance.Update(0.016f);
        return TestClips.RootZ(instance);
    }

    [Fact]
    public void Blend2D_AtSamplePoint_OutputsThatSample()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int x = graph.AddFloatParameter("X");
        int y = graph.AddFloatParameter("Y");
        int a = graph.AddClip(TestClips.Const(skeleton, 1f));
        int b = graph.AddClip(TestClips.Const(skeleton, 2f));
        int c = graph.AddClip(TestClips.Const(skeleton, 3f));
        int blend = graph.AddBlend2D(x, y, new[]
        {
            (a, new Float2(-1f, 0f)),
            (b, new Float2(1f, 0f)),
            (c, new Float2(0f, 1f)),
        });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.SetFloat("X", 0f);
        instance.SetFloat("Y", 1f); // exactly sample c
        instance.Update(0.016f);
        Assert.Equal(3.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void Blend2D_NearSample_LeansTowardIt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int x = graph.AddFloatParameter("X");
        int y = graph.AddFloatParameter("Y");
        int a = graph.AddClip(TestClips.Const(skeleton, 0f));
        int b = graph.AddClip(TestClips.Const(skeleton, 10f));
        int blend = graph.AddBlend2D(x, y, new[] { (a, new Float2(0f, 0f)), (b, new Float2(2f, 0f)) });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetFloat("X", 1.8f); // close to b
        instance.SetFloat("Y", 0f);
        instance.Update(0.016f);
        Assert.True(instance.Pose.GetTransform(0).position.Z > 8f);
    }

    [Theory]
    [InlineData(10f, 0f, 10f)]
    [InlineData(-3f, 0f, 0f)]
    public void Blend2D_PointOutsideTheSamples_ProjectsOntoTheHull(float x, float y, float expected)
    {
        Assert.Equal(expected, Blend2D(new[] { (new Float2(0f, 0f), 0f), (new Float2(1f, 0f), 10f) }, x, y), 3);
    }

    [Fact]
    public void Blend2D_CollinearSamples_BlendAlongTheLine()
    {
        Assert.Equal(5.0, (double)Blend2D(new[] { (new Float2(0f, 0f), 0f), (new Float2(1f, 0f), 10f), (new Float2(-1f, 0f), -10f) }, 0.5f, 0f), 3);
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(100f)]
    public void Blend2D_ResultDoesNotDependOnUnits(float spacing)
    {
        var samples = new[] { (new Float2(0f, 0f), 0f), (new Float2(spacing, 0f), 10f), (new Float2(0f, spacing), 0f) };
        Assert.Equal(2.5, (double)Blend2D(samples, spacing * 0.25f, 0f), 3);
    }

    [Fact]
    public void Blend2D_InsideATriangle_UsesBarycentricWeights()
    {
        var samples = new[] { (new Float2(0f, 0f), 0f), (new Float2(1f, 0f), 10f), (new Float2(0f, 1f), 20f) };
        Assert.Equal(7.5, (double)Blend2D(samples, 0.25f, 0.25f), 3);
    }

    [Theory]
    [InlineData(1f / 60f)]
    [InlineData(1f / 30f)]
    [InlineData(0.02f)]
    public void Blend2D_FirstFrameAdvancesByDeltaTime(float dt)
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int x = g.AddFloatParameter("X");
        int y = g.AddFloatParameter("Y");
        int child = g.AddSpeedScale(g.AddClip(TestClips.Ramp(skeleton)));
        g.SetRoot(g.AddBlend2D(x, y, new[] { (child, new Float2(0f, 0f)), (g.AddClip(TestClips.Ramp(skeleton)), new Float2(1f, 0f)) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(dt);

        Assert.Equal(dt, (double)instance.NormalizedTime, 4);
    }

    [Fact]
    public void ZeroWeightBlendSample_EmitsNoEventsAndDoesNotSuspendTheOverride()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int x = g.AddFloatParameter("X");
        int y = g.AddFloatParameter("Y");
        int a = g.AddClip(TestClips.Ramp(skeleton, rootTravel: 1f));
        int b = g.AddClip(TestClips.Ramp(skeleton, rootTravel: 10f, events: new AnimationEvent[] { new RootMotionEvent(0f, 1f), new IdEvent(new StringID("step"), 0.05f) }));
        int blend = g.AddBlend2D(x, y, new[] { (a, new Float2(0f, 0f)), (b, new Float2(1f, 0f)) });
        g.SetRoot(g.AddRootMotionOverride(blend, maxLinearSpeed: 1f));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(0, TestClips.CountId(instance.Events, "step"));
        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
    }

    [Fact]
    public void Blend2D_SyncTrackIsTheBlendOfItsActiveChildren()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int x = g.AddFloatParameter("X", 0.5f);
        int y = g.AddFloatParameter("Y");
        int two = g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0f, 0.5f)));
        int three = g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0f, 0.3f, 0.6f)));
        int blend = g.AddBlend2D(x, y, new[] { (two, new Float2(0f, 0f)), (three, new Float2(1f, 0f)) });
        g.SetRoot(blend);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(6, ((PoseNodeInstance)instance.GetNodeInstance(blend)).SyncTrack.EventCount);
    }

    [Fact]
    public void Blend2D_ThreeWayBlend_DoesNotAllocate()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int x = g.AddFloatParameter("X", 0.25f);
        int y = g.AddFloatParameter("Y", 0.25f);
        g.SetRoot(g.AddBlend2D(x, y, new[]
        {
            (g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0f, 0.5f))), new Float2(0f, 0f)),
            (g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0f, 0.3f, 0.6f))), new Float2(1f, 0f)),
            (g.AddClip(TestClips.Ramp(skeleton)), new Float2(0f, 1f)),
        }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        long lowest = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                instance.Update(1f / 60f);
            lowest = Math.Min(lowest, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0, lowest);
    }

    [Theory]
    [InlineData(60f)]
    [InlineData(100f)]
    public void BlendSpace_StretchedLayout_KeepsEveryTriangle(float aspect)
    {
        var points = new[] { new Float2(0f, 0f), new Float2(aspect, 0f), new Float2(0f, 1f), new Float2(aspect, 1f), new Float2(aspect * 0.5f, 0.5f) };
        var space = new BlendSpace2D(points);
        Span<(int Index, float Weight)> weights = stackalloc (int, float)[3];

        int count = space.Evaluate(new Float2(aspect * 0.25f, 0.5f), weights);
        float y = 0f;
        for (int i = 0; i < count; i++)
            y += points[weights[i].Index].Y * weights[i].Weight;

        Assert.Equal(12, space.Triangles.Count);
        Assert.Equal(0.5, (double)y, 3);
    }
}
