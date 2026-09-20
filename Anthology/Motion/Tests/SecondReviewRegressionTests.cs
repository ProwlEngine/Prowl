using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class SecondReviewRegressionTests
{
    [Fact]
    public void InstantTransition_StartedDuringATransition_LeavesNoDanglingTransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int toB = g.AddBoolParameter("ToB");
        int toC = g.AddBoolParameter("ToC");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(TestClips.Ramp(skeleton, 10f)));
        int b = g.AddState(sm, g.AddClip(TestClips.Ramp(skeleton, 20f)));
        int c = g.AddState(sm, g.AddClip(TestClips.Ramp(skeleton, 30f)));
        g.AddTransition(sm, a, b, toB, 0.5f);
        g.AddTransition(sm, b, c, toC, 0f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);
        instance.SetBool("ToB", true);
        instance.Update(0.1f);
        instance.SetBool("ToB", false);
        instance.SetBool("ToC", true);
        instance.Update(0.1f);
        instance.SetBool("ToC", false);

        for (int expected = 6; expected <= 12; expected += 3)
        {
            instance.Update(0.1f);
            Assert.Equal(expected, (double)TestClips.RootZ(instance), 2);
        }
    }

    [Fact]
    public void SynchronizedTransition_OutOfASubGraph_PlaysOnlyTheStep()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var sub = new AnimationGraph();
        sub.SetRoot(sub.AddClip(TestClips.Ramp(skeleton, rootTravel: 1f)));

        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int from = g.AddState(sm, g.AddReferencedGraph(sub));
        int to = g.AddState(sm, g.AddClip(TestClips.Ramp(skeleton, 20f, rootTravel: 1f, events: new AnimationEvent[] { new IdEvent(new StringID("hit"), 0.3f) })));
        g.AddTransition(sm, from, to, go, 0.5f).Sync = TransitionSync.Synchronized;
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 7; i++)
            instance.Update(0.1f);
        instance.SetBool("Go", true);
        instance.Update(0.1f);

        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
        Assert.Equal(0, TestClips.CountId(instance.Events, "hit"));
    }

    [Fact]
    public void ChainedSynchronizedTransitions_KeepEveryClipMovingForward()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int toB = g.AddBoolParameter("ToB");
        int toC = g.AddBoolParameter("ToC");
        int a = g.AddClip(TestClips.Ramp(skeleton, 1f, 0.5f, rootTravel: 1f, syncTrack: TestClips.Track(0f, 0.5f)));
        int b = g.AddClip(TestClips.Ramp(skeleton, 2f, 0.9f, rootTravel: 1f, syncTrack: TestClips.Track(0f, 0.3f, 0.6f)));
        int c = g.AddClip(TestClips.Ramp(skeleton, 3f, 1.3f, rootTravel: 1f, syncTrack: TestClips.Track(0f, 0.5f)));
        int sm = g.AddStateMachine();
        g.AddState(sm, a);
        g.AddState(sm, b);
        g.AddState(sm, c);
        g.AddTransition(sm, 0, 1, toB, 0.5f).Sync = TransitionSync.Synchronized;
        g.AddTransition(sm, 1, 2, toC, 0.5f).Sync = TransitionSync.Synchronized;
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 10; i++)
            instance.Update(0.03f);
        instance.SetBool("ToB", true);
        instance.Update(0.03f);
        instance.SetBool("ToB", false);
        for (int i = 0; i < 4; i++)
            instance.Update(0.03f);

        instance.SetBool("ToC", true);
        for (int frame = 0; frame < 12; frame++)
        {
            instance.Update(0.03f);
            instance.SetBool("ToC", false);
            Assert.InRange(instance.RootMotionDelta.position.Z / 0.03f, 0f, 2.01f);
            foreach (int node in new[] { a, b, c })
            {
                var clip = (PoseNodeInstance)instance.GetNodeInstance(node);
                if (!clip.IsInitialized)
                    continue;
                float step = clip.NormalizedTime - clip.PreviousTime;
                if (step < 0f)
                    step += 1f;
                Assert.InRange(step, 0f, 0.2f);
            }
        }
    }

    [Fact]
    public void SynchronizedTransition_StartingAsTheSourceLoops_DoesNotSkipTheTarget()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int a = g.AddClip(TestClips.Ramp(skeleton, 1f, rootTravel: 1f, syncTrack: TestClips.Track(0f, 0.5f)));
        int b = g.AddClip(TestClips.Ramp(skeleton, 2f, rootTravel: 1f, syncTrack: TestClips.Track(0f, 0.3f, 0.6f)));
        int sm = g.AddStateMachine();
        g.AddState(sm, a);
        g.AddState(sm, b);
        g.AddTransition(sm, 0, 1, go, 0.5f).Sync = TransitionSync.Synchronized;
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        float t = 0f;
        while (t + 0.05f < 0.97f)
        {
            instance.Update(0.05f);
            t += 0.05f;
        }
        instance.Update(0.97f - t);
        instance.SetBool("Go", true);
        instance.Update(0.06f);

        var target = (PoseNodeInstance)instance.GetNodeInstance(b);
        Assert.InRange(target.NormalizedTime - target.PreviousTime, 0f, 0.1f);
        Assert.InRange(instance.RootMotionDelta.position.Z, 0.04f, 0.08f);
    }

    [Fact]
    public void Blend1D_WithoutLooping_HoldsItsLastFrame()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P", 0.5f);
        int low = g.AddClip(TestClips.Ramp(skeleton, 10f, rootTravel: 1f), loop: false);
        int high = g.AddClip(TestClips.Ramp(skeleton, 20f, rootTravel: 1f), loop: false);
        g.SetRoot(g.AddBlend1D(p, new[] { (low, 0f), (high, 1f) }, loop: false));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        float travel = 0f;
        for (int i = 0; i < 15; i++)
        {
            instance.Update(0.1f);
            travel += instance.RootMotionDelta.position.Z;
        }

        Assert.Equal(15.0, (double)TestClips.RootZ(instance), 2);
        Assert.Equal(1.0, (double)travel, 2);
    }

    [Fact]
    public void OrientationWarp_ReversePlayback_StepsBackward()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int reverse = g.AddBoolParameter("Reverse");
        int angle = g.AddFloatParameter("Angle");
        int clip = g.AddClip(TestClips.Ramp(skeleton, 1f, rootTravel: 1f));
        g.SetClipDrivers(clip, reverse);
        g.SetRoot(g.AddOrientationWarpAngle(clip, angle));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 5; i++)
            instance.Update(0.1f);
        instance.SetBool("Reverse", true);
        instance.Update(0.1f);

        Assert.Equal(-0.1, (double)instance.RootMotionDelta.position.Z, 3);
    }

    [Fact]
    public void Transition_WithNegativeDeltaTime_StillCompletes()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(TestClips.Ramp(skeleton, 10f)));
        int b = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, 50f)));
        g.AddTransition(sm, a, b, go, 0.2f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.5f);
        instance.SetBool("Go", true);
        instance.Update(-0.05f);
        instance.SetBool("Go", false);
        for (int i = 0; i < 10; i++)
            instance.Update(-0.05f);

        Assert.Equal(50.0, (double)TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void SyncTrack_TimeZeroOnATrackStartingLater_StaysAtZero()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        AnimationEvent[] end = { new IdEvent(new StringID("end"), 1f) };
        int b = g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0.25f, 0.75f), events: end), loop: false);
        int layer = g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0.25f, 0.75f), events: end), loop: false);
        g.SetRoot(g.AddLayerBlend(b, new[] { new LayerInfo(layer) { Synchronized = true } }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0f);

        Assert.Equal(0.0, (double)((PoseNodeInstance)instance.GetNodeInstance(layer)).NormalizedTime, 3);
        Assert.Equal(0, TestClips.CountId(instance.Events, "end"));
    }

    [Fact]
    public void TargetWarp_GoalChangedMidClip_StillEndsOnTheGoal()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int goal = g.AddVectorParameter("Goal", new Float3(0f, 0f, 2f));
        int clip = g.AddClip(TestClips.Ramp(skeleton, 1f, rootTravel: 1f), loop: false);
        g.SetRoot(g.AddTargetWarp(clip, goal));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        float travel = 0f;
        for (int i = 0; i < 10; i++)
        {
            if (i == 5)
                instance.SetVector("Goal", new Float3(0f, 0f, 3f));
            instance.Update(0.1f);
            travel += instance.RootMotionDelta.position.Z;
        }

        Assert.Equal(3.0, (double)travel, 2);
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

    [Fact]
    public void LayerBlend_NaNRootMotionWeight_KeepsRootMotionFinite()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int weight = g.AddFloatParameter("RootMotionWeight", float.NaN);
        int basePose = g.AddClip(TestClips.Ramp(skeleton, 1f, rootTravel: 1f));
        int layer = g.AddClip(TestClips.Ramp(skeleton, 2f, rootTravel: 2f));
        g.SetRoot(g.AddLayerBlend(basePose, new[] { new LayerInfo(layer) { RootMotionWeightNodeIndex = weight } }, onlySampleBaseRootMotion: false));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
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

    [Fact]
    public void DurationEventAcrossTheLoop_ReportsProgressAtTheRealEndTime()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip clip = TestClips.Ramp(skeleton, events: new AnimationEvent[] { new IdEvent(new StringID("d"), 0.8f, 0.3f) });
        var buffer = new SampledEventsBuffer();

        clip.SampleEvents(0.9f, 0.05f, buffer, looped: true);

        Assert.Equal(0.833, (double)buffer[0].PercentageThrough, 3);
    }

    [Fact]
    public void ValueInputs_OfTheWrongKind_FailValidation()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var compare = new AnimationGraph();
        compare.AddFloatCompare(compare.AddVectorParameter("V", new Float3(5f, 5f, 5f)), CompareOp.Greater, 1f);
        compare.SetRoot(compare.AddReferencePose());
        Assert.Throws<GraphValidationException>(() => compare.CreateInstance(skeleton));

        var ik = new AnimationGraph();
        ik.SetRoot(ik.AddTwoBoneIK(ik.AddReferencePose(), ik.AddFloatParameter("NotATarget", 3f), 0, 1, 2));
        Assert.Throws<GraphValidationException>(() => ik.CreateInstance(skeleton));
    }

    [Fact]
    public void Mirror_SwapsFootEventsAndMirrorsRootMotion()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var frame = new Pose(skeleton);
        frame.SetToReferencePose();
        var root = new[] { Transform3D.Identity, new Transform3D(new Float3(1f, 0f, 0f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.5f), Float3.One) };
        var clip = new AnimationClip(skeleton, new[] { frame, frame }, 1f, rootMotion: new RootMotion(root, 1f), events: new AnimationEvent[] { new FootEvent(FootPhase.LeftFootDown, 0.05f) });
        var g = new AnimationGraph();
        g.SetRoot(g.AddMirror(g.AddClip(clip)));
        AnimationGraphInstance instance = g.CreateInstance(avatar);

        instance.Update(0.1f);

        Assert.True(instance.RootMotionDelta.position.X < 0f);
        Assert.True(instance.RootMotionDelta.rotation.Y < 0f);
        FootEvent foot = Assert.Single(instance.Events.FootEvents());
        Assert.Equal(FootPhase.RightFootDown, foot.Phase);
        Assert.Same(clip.Events[0], foot.Mirrored);
    }
}
