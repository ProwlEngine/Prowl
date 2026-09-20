using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class PoseNodeRegressionTests
{
    [Fact]
    public void LayerBlend_AdditiveLayerKeepsTheBaseRootMotion()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int basePose = g.AddClip(TestClips.Ramp(skeleton, rootTravel: 1f));
        int layer = g.AddZeroPose();
        g.SetRoot(g.AddLayerBlend(basePose, new[] { new LayerInfo(layer, additive: true) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
    }

    [Fact]
    public void LayerBlend_FullyMaskedOverrideLayerKeepsTheBaseRootMotion()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int basePose = g.AddClip(TestClips.Ramp(skeleton, rootTravel: 1f));
        int layer = g.AddClip(TestClips.Const(skeleton, 3f));
        int mask = g.AddFixedWeightBoneMask(0f);
        g.SetRoot(g.AddLayerBlend(basePose, new[] { new LayerInfo(layer, maskNodeIndex: mask) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 3);
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
    public void SpeedScale_InsideABlend_SpeedsUpTheChild()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P", 0f);
        int scaled = g.AddSpeedScale(g.AddClip(TestClips.Ramp(skeleton)), defaultSpeed: 2f);
        g.SetRoot(g.AddBlend1D(p, new[] { (scaled, 0f), (g.AddClip(TestClips.Ramp(skeleton)), 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 12; i++)
            instance.Update(0.02f);

        Assert.Equal(0.5, (double)((PoseNodeInstance)instance.GetNodeInstance(scaled)).Duration, 3);
        Assert.Equal(0.48, (double)instance.NormalizedTime, 3);
    }

    [Fact]
    public void SpeedScale_ClampsNegativeSpeed()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int speed = g.AddFloatParameter("Speed", -1f);
        g.SetRoot(g.AddSpeedScale(g.AddClip(TestClips.Ramp(skeleton), loop: false), speed));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 5; i++)
            instance.Update(0.1f);
        instance.SetFloat("Speed", 1f);
        for (int i = 0; i < 5; i++)
            instance.Update(0.05f);

        Assert.Equal(0.25, (double)instance.NormalizedTime, 3);
    }

    [Fact]
    public void WrapperNodes_ForwardTheChildSyncTrack()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        SyncTrack footsteps = TestClips.Track(0f, 0.5f);
        var g = new AnimationGraph();
        int clip = g.AddClip(TestClips.Ramp(skeleton, syncTrack: footsteps));
        int speed = g.AddSpeedScale(clip, defaultSpeed: 1.5f);
        int mirror = g.AddMirror(speed);
        int overrideRm = g.AddRootMotionOverride(mirror);
        int selector = g.AddSelector(g.AddIntParameter("Which"), new[] { overrideRm });
        g.SetRoot(selector);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);
        instance.Update(0.1f);

        foreach (int node in new[] { speed, mirror, overrideRm, selector })
            Assert.Same(footsteps, ((PoseNodeInstance)instance.GetNodeInstance(node)).SyncTrack);
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
    public void FloatClamp_WithMinAboveMax_FailsWhenBuilt()
    {
        var g = new AnimationGraph();
        int x = g.AddFloatParameter("X");
        Assert.Throws<ArgumentException>(() => g.AddFloatClamp(x, 5f, 1f));
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
    public void CachedValue_DriverAlreadyTrueOnTheFirstFrame_IsNotARisingEdge()
    {
        var g = new AnimationGraph();
        int x = g.AddFloatParameter("X", 1f);
        int driver = g.AddBoolParameter("Driver", true);
        int cached = g.AddCachedValue(x, driver);
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        Assert.Equal(1.0, (double)instance.EvaluateValueNode(cached).AsFloat(), 4);
        instance.SetFloat("X", 2f);
        Assert.Equal(1.0, (double)instance.EvaluateValueNode(cached).AsFloat(), 4);
    }

    [Fact]
    public void CachedValue_RelatchesEachTimeItsStateIsEntered()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int source = g.AddFloatParameter("Source", 2f);
        int away = g.AddBoolParameter("Away");
        int cached = g.AddCachedValue(source);
        int blend = g.AddBlend1D(cached, new[] { (g.AddClip(TestClips.Const(skeleton, 0f)), 0f), (g.AddClip(TestClips.Const(skeleton, 10f)), 10f) });
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, blend);
        int b = g.AddState(sm, g.AddReferencePose());
        g.AddTransition(sm, a, b, away, 0f);
        g.AddTransition(sm, b, a, g.AddNot(away), 0f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);
        Assert.Equal(2.0, (double)TestClips.RootZ(instance), 3);

        instance.SetBool("Away", true);
        instance.Update(0.1f);
        instance.SetFloat("Source", 8f);
        instance.SetBool("Away", false);
        instance.Update(0.1f);
        instance.Update(0.1f);

        Assert.Equal(8.0, (double)TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void CachedValue_OnExit_FreezesWhenItsBranchStartsLeaving()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int source = g.AddFloatParameter("Source", 2f);
        int go = g.AddBoolParameter("Go");
        int cached = g.AddCachedValue(source, mode: CachedValueMode.OnExit);
        int blend = g.AddBlend1D(cached, new[] { (g.AddClip(TestClips.Const(skeleton, 0f)), 0f), (g.AddClip(TestClips.Const(skeleton, 10f)), 10f) });
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, blend);
        int b = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, 0f)));
        g.AddTransition(sm, a, b, go, 10f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.SetFloat("Source", 4f);
        instance.Update(0.1f);
        instance.SetBool("Go", true);
        instance.Update(0.1f);
        instance.SetFloat("Source", 9f);
        instance.Update(0.1f);

        Assert.Equal(4.0, (double)((PoseNodeInstance)instance.GetNodeInstance(blend)).Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void TargetInfo_MeasuresWorldTargetsInCharacterSpace()
    {
        var g = new AnimationGraph();
        int target = g.AddTargetParameter("T");
        int distance = g.AddTargetInfo(target, TargetInfo.Distance);
        int angle = g.AddTargetInfo(target, TargetInfo.AngleHorizontal);
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        instance.SetTarget("T", Target.FromWorld(new Transform3D(new Float3(100f, 0f, 5f), Quaternion.Identity, Float3.One)));
        instance.Update(0.1f, new Transform3D(new Float3(100f, 0f, 0f), Quaternion.Identity, Float3.One));

        Assert.Equal(5.0, (double)instance.EvaluateValueNode(distance).AsFloat(), 3);
        Assert.Equal(0.0, (double)instance.EvaluateValueNode(angle).AsFloat(), 3);
    }

    [Fact]
    public void Selector_WithNoOptions_FailsValidation()
    {
        var g = new AnimationGraph();
        g.SetRoot(g.AddSelector(g.AddIntParameter("Which"), Array.Empty<int>()));
        Assert.Throws<GraphValidationException>(() => g.CreateInstance(TestSkeletons.MakeChain()));
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
}
