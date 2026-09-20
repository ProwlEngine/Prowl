using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class ThirdReviewRegressionTests
{
    private sealed class Player : SimpleAnimator
    {
        public Player(Skeleton skeleton) : base(skeleton) { }
        protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) { }
    }

    [Fact]
    public void NegativeDeltaTime_ThroughABlend_StepsBackwardLikeTheBareClip()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P", 0f);
        AnimationClip walk = TestClips.Ramp(skeleton, rootTravel: 1f, events: new AnimationEvent[] { new IdEvent(new StringID("step"), 0.45f) });
        g.SetRoot(g.AddBlend1D(p, new[] { (g.AddClip(walk), 0f), (g.AddClip(walk), 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.5f);
        float travel = 0f;
        int steps = 0;
        for (int i = 0; i < 6; i++)
        {
            instance.Update(-1f / 60f);
            travel += instance.RootMotionDelta.position.Z;
            steps += TestClips.CountId(instance.Events, "step");
        }

        Assert.Equal(-0.1, (double)travel, 3);
        Assert.Equal(1, steps);
        Assert.Equal(4.0, (double)TestClips.RootZ(instance), 2);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.9f, 0.1f)]
    public void StaticPoseInABlend_DoesNotSpeedUpTheClip(float staticWeight, float expectedTravel)
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P", staticWeight);
        int walk = g.AddClip(TestClips.Ramp(skeleton, rootTravel: 1f, events: new AnimationEvent[] { new IdEvent(new StringID("step"), 0.5f) }));
        g.SetRoot(g.AddBlend1D(p, new[] { (walk, 0f), (g.AddReferencePose(), 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        float travel = 0f;
        int steps = 0;
        for (int i = 0; i < 10; i++)
        {
            instance.Update(0.1f);
            travel += instance.RootMotionDelta.position.Z;
            steps += TestClips.CountId(instance.Events, "step");
        }

        Assert.Equal(expectedTravel, (double)travel, 3);
        Assert.Equal(1, steps);
    }

    [Fact]
    public void SynchronizedTransition_FromAStaticState_PlaysTheTargetAtItsOwnRate()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int idle = g.AddState(sm, g.AddReferencePose());
        int walkClip = g.AddClip(TestClips.Ramp(skeleton, rootTravel: 1f));
        int walk = g.AddState(sm, walkClip);
        g.AddTransition(sm, idle, walk, go, 0.5f).Sync = TransitionSync.Synchronized;
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);
        instance.SetBool("Go", true);
        for (int i = 0; i < 5; i++)
            instance.Update(0.1f);

        var clip = (PoseNodeInstance)instance.GetNodeInstance(walkClip);
        Assert.Equal(0, clip.LoopCount);
        Assert.InRange(clip.NormalizedTime, 0.4f, 0.55f);
    }

    [Fact]
    public void ShortClipInABlend_KeepsEveryLoop()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P");
        AnimationClip shortClip = TestClips.Ramp(skeleton, duration: 0.01f, rootTravel: 1f);
        g.SetRoot(g.AddBlend1D(p, new[] { (g.AddClip(shortClip), 0f), (g.AddClip(shortClip), 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        float travel = 0f;
        for (int i = 0; i < 60; i++)
        {
            instance.Update(1f / 60f);
            travel += instance.RootMotionDelta.position.Z;
        }

        Assert.Equal(100.0, (double)travel, 1);
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

    [Fact]
    public void SimpleAnimator_NaNDeltaTime_DoesNotBreakTheCrossFade()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var player = new Player(skeleton);
        player.Play(TestClips.Const(skeleton, 0f));
        player.Update(0.1f);
        player.CrossFade(TestClips.Const(skeleton, 2f), 0.5f);
        player.Update(0.1f);
        player.Update(float.NaN);
        for (int i = 0; i < 10; i++)
            player.Update(0.1f);

        Assert.False(player.IsCrossFading);
        Assert.Equal(2.0, (double)player.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void ExternalGraph_PreviewedStandalone_StartsFreshInASlot()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var external = new AnimationGraph();
        external.SetRoot(external.AddClip(TestClips.Ramp(skeleton)));
        AnimationGraphInstance preview = external.CreateInstance(skeleton);

        var host = new AnimationGraph();
        host.SetRoot(host.AddExternalGraphSlot("slot"));
        AnimationGraphInstance instance = host.CreateInstance(skeleton);

        for (int i = 0; i < 5; i++)
            preview.Update(0.1f);
        instance.SetExternalGraph("slot", preview);
        instance.Update(0.1f);
        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 3);

        instance.SetExternalGraph("slot", null);
        instance.Update(0.1f);
        instance.SetExternalGraph("slot", preview);
        instance.Update(0.1f);
        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void SnapToFrame_CoversTheWrappedTailOfTheEvent()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip clip = TestClips.Ramp(skeleton, events: new AnimationEvent[] { new SnapToFrameEvent(FrameSnapMode.Floor, 0.85f, 0.3f) });
        var g = new AnimationGraph();
        g.SetRoot(g.AddClip(clip));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.87f);
        Assert.Equal(8.0, (double)TestClips.RootZ(instance), 3);
        instance.Update(0.18f);
        Assert.Equal(0.0, (double)TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void ControlParameter_AddedThroughAddNode_IsRegistered()
    {
        var g = new AnimationGraph();
        g.AddNode(new ControlParameterDefinition("Speed", AnimationValueType.Float, ParameterValue.FromFloat(2f)));
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        instance.Update(0.1f);

        Assert.Equal(0, instance.GetParameterIndex("Speed"));
        Assert.Equal(2.0, (double)instance.GetFloat("Speed"), 4);
    }

    [Fact]
    public void ControlParameter_RejectedByNameCollision_LeavesNothingBehind()
    {
        var g = new AnimationGraph();
        g.NameNode(g.AddReferencePose(), "Speed");

        Assert.Throws<ArgumentException>(() => g.AddFloatParameter("Speed"));

        Assert.Empty(g.Parameters);
        Assert.Equal(-1, g.GetParameterIndex("Speed"));
        Assert.Equal(1, g.NodeCount);
    }

    [Fact]
    public void WorldTarget_OnARotatedNonUniformlyScaledCharacter_ResolvesExactly()
    {
        var g = new AnimationGraph();
        int target = g.AddTargetParameter("T");
        int point = g.AddTargetPoint(target);
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        var world = new Transform3D(new Float3(3f, 0f, -2f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.7f), new Float3(2f, 1f, 0.5f));
        var local = new Float3(1f, 2f, 3f);
        instance.SetTarget("T", Target.FromWorld(new Transform3D(world.TransformPoint(local), Quaternion.Identity, Float3.One)));
        instance.Update(0.1f, world);

        Float3 resolved = instance.EvaluateValueNode(point).Vector;
        Assert.Equal(1.0, (double)resolved.X, 3);
        Assert.Equal(2.0, (double)resolved.Y, 3);
        Assert.Equal(3.0, (double)resolved.Z, 3);
    }

    [Fact]
    public void SynchronizedTransition_BetweenOffsetTracks_KeepsTheMachineTimeSmooth()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        g.AddState(sm, g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0.2f, 0.7f))));
        g.AddState(sm, g.AddClip(TestClips.Ramp(skeleton, 20f, 1.5f, syncTrack: TestClips.Track(0.2f, 0.7f))));
        g.AddTransition(sm, 0, 1, go, 0.5f).Sync = TransitionSync.Synchronized;
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 9; i++)
            instance.Update(1f / 30f);
        float previous = instance.NormalizedTime;
        instance.SetBool("Go", true);
        for (int i = 0; i < 25; i++)
        {
            instance.Update(1f / 30f);
            float step = instance.NormalizedTime - previous;
            if (step < -0.5f)
                step += 1f;
            Assert.InRange(step, 0f, 0.06f);
            previous = instance.NormalizedTime;
        }
    }
}
