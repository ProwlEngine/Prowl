using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class GraphRegressionTests
{
    private static AnimationClip Ramp(Skeleton skeleton, float rootTravel = 0f, params AnimationEvent[] events)
        => TestClips.Ramp(skeleton, rootTravel: rootTravel, events: events);

    [Fact]
    public void ReversePlayback_MovesRootBackwardOneStepAtATime()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int reverse = g.AddBoolParameter("Reverse", true);
        int clip = g.AddClip(Ramp(skeleton, 1f, new IdEvent(new StringID("a"), 0.25f), new IdEvent(new StringID("b"), 0.75f)));
        g.SetClipDrivers(clip, playInReverseNodeIndex: reverse);
        g.SetRoot(clip);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        int events = 0;
        for (int i = 0; i < 10; i++)
        {
            instance.Update(0.1f);
            Assert.Equal(-0.1, (double)instance.RootMotionDelta.position.Z, 3);
            int frameEvents = TestClips.CountId(instance.Events, "a") + TestClips.CountId(instance.Events, "b");
            Assert.InRange(frameEvents, 0, 1);
            events += frameEvents;
        }
        Assert.Equal(2, events);
    }

    [Fact]
    public void LongStep_CoversEveryLoopOfRootMotionAndEvents()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        g.SetRoot(g.AddClip(Ramp(skeleton, 1f, new IdEvent(new StringID("a"), 0.25f), new IdEvent(new StringID("b"), 0.75f))));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(2.5f);

        Assert.Equal(2.5, (double)instance.RootMotionDelta.position.Z, 3);
        Assert.Equal(3, TestClips.CountId(instance.Events, "a"));
        Assert.Equal(2, TestClips.CountId(instance.Events, "b"));
    }

    [Fact]
    public void SynchronizedTransition_TargetKeepsItsPhaseAfterTheBlend()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        int b = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        g.AddTransition(sm, a, b, go, 0.2f).Sync = TransitionSync.Synchronized;
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 6; i++)
            instance.Update(0.1f);
        instance.SetBool("Go", true);

        for (int expected = 7; expected <= 9; expected++)
        {
            instance.Update(0.1f);
            Assert.Equal(expected, (double)TestClips.RootZ(instance), 2);
        }
    }

    [Fact]
    public void ReenteredOneShotState_PlaysFromTheStart()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int again = g.AddBoolParameter("Again");
        int sm = g.AddStateMachine();
        int attack = g.AddState(sm, g.AddClip(Ramp(skeleton), loop: false));
        int idle = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, -5f)));
        g.AddTransition(sm, attack, idle, g.AddStateFinished(sm), 0f);
        g.AddTransition(sm, idle, attack, again, 0f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 12; i++)
            instance.Update(0.1f);
        Assert.Equal(-5.0, (double)TestClips.RootZ(instance), 3);

        instance.SetBool("Again", true);
        instance.Update(0.1f);
        instance.SetBool("Again", false);
        instance.Update(0.1f);

        Assert.InRange(TestClips.RootZ(instance), 0f, 2.01f);
    }

    [Fact]
    public void MatchSourceTime_DoesNotIntegrateTheJump()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton, 1f)));
        int bClip = g.AddClip(Ramp(skeleton, 1f, new IdEvent(new StringID("early"), 0.3f)));
        int b = g.AddState(sm, bClip);
        g.AddTransition(sm, a, b, go, 0f).Sync = TransitionSync.MatchSourceTime;
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 6; i++)
            instance.Update(0.1f);
        instance.SetBool("Go", true);
        instance.Update(0.1f);

        Assert.InRange(instance.RootMotionDelta.position.Z, -0.001f, 0.101f);
        Assert.Equal(0, TestClips.CountId(instance.Events, "early"));
        Assert.Equal(0.7, (double)((PoseNodeInstance)instance.GetNodeInstance(bClip)).NormalizedTime, 3);

        instance.Update(0.1f);
        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
    }

    [Fact]
    public void SelfTransition_RestartsTheStateOnce()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int restart = g.AddBoolParameter("Restart");
        int sm = g.AddStateMachine();
        int clip = g.AddClip(Ramp(skeleton));
        int a = g.AddState(sm, clip);
        g.AddTransition(sm, a, a, restart, 0.2f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);
        instance.SetBool("Restart", true);
        instance.Update(0.1f);
        Assert.Equal(0.1, (double)((PoseNodeInstance)instance.GetNodeInstance(clip)).NormalizedTime, 3);

        instance.SetBool("Restart", false);
        instance.Update(0.1f);
        Assert.Equal(0.2, (double)((PoseNodeInstance)instance.GetNodeInstance(clip)).NormalizedTime, 3);
    }

    [Fact]
    public void SharedPoseNode_IsRejected()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int clip = g.AddClip(Ramp(skeleton));
        int sm = g.AddStateMachine();
        g.AddState(sm, clip);
        g.AddState(sm, clip);
        g.SetRoot(sm);

        var ex = Assert.Throws<GraphValidationException>(() => g.CreateInstance(skeleton));
        Assert.Equal(clip, ex.NodeIndex);
    }

    [Fact]
    public void Blend1D_DifferentSyncEventCounts_PlaysTheWholeLongerClip()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int param = g.AddFloatParameter("P", 0.5f);
        int two = g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0f, 0.5f)));
        int four = g.AddClip(TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0f, 0.25f, 0.5f, 0.75f)));
        g.SetRoot(g.AddBlend1D(param, new[] { (two, 0f), (four, 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        float maxFour = 0f;
        for (int i = 0; i < 60; i++)
        {
            instance.Update(1f / 30f);
            maxFour = MathF.Max(maxFour, ((PoseNodeInstance)instance.GetNodeInstance(four)).NormalizedTime);
        }
        Assert.True(maxFour > 0.9f, $"four event clip only reached {maxFour}");
    }

    [Fact]
    public void Blend1D_UnusedChild_IsNotUpdatedAndEmitsNoEvents()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int speed = g.AddFloatParameter("Speed", 0f);
        int idle = g.AddClip(TestClips.Const(skeleton, 0f));
        int run = g.AddClip(Ramp(skeleton, 0f, new IdEvent(new StringID("step"), 0.5f)));
        g.SetRoot(g.AddBlend1D(speed, new[] { (idle, 0f), (run, 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        int steps = 0;
        for (int i = 0; i < 20; i++)
        {
            instance.Update(0.1f);
            steps += TestClips.CountId(instance.Events, "step");
        }
        Assert.Equal(0, steps);
    }

    [Fact]
    public void ResetDriver_RestartsWithoutAJump()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int reset = g.AddBoolParameter("Reset");
        int clip = g.AddClip(Ramp(skeleton, 1f), loop: false);
        g.SetClipDrivers(clip, resetTimeNodeIndex: reset);
        g.SetRoot(clip);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 8; i++)
            instance.Update(0.1f);
        instance.SetBool("Reset", true);
        instance.Update(0.1f);

        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 2);
    }

    [Fact]
    public void TimeInState_CountsFromTheStartOfTheTransitionIntoIt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        int b = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        g.AddTransition(sm, a, b, go, 0.5f);
        int time = g.AddStateQuery(sm, StateQuery.TimeInState);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 10; i++)
            instance.Update(0.1f);
        instance.SetBool("Go", true);
        instance.Update(0.1f);
        instance.Update(0.1f);
        Assert.Equal(0.1, (double)instance.EvaluateValueNode(time).AsFloat(), 3);

        for (int i = 0; i < 5; i++)
            instance.Update(0.1f);
        Assert.Equal(0.6, (double)instance.EvaluateValueNode(time).AsFloat(), 3);
    }

    [Fact]
    public void Transition_CanStartWhileAnotherIsRunning()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int toB = g.AddBoolParameter("ToB");
        int toC = g.AddBoolParameter("ToC");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, 0f)));
        int b = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, 10f)));
        int c = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, 20f)));
        g.AddTransition(sm, a, b, toB, 1f);
        g.AddTransition(sm, b, c, toC, 0.1f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);
        instance.SetBool("ToB", true);
        for (int i = 0; i < 5; i++)
            instance.Update(0.1f);
        instance.SetBool("ToC", true);
        for (int i = 0; i < 5; i++)
            instance.Update(0.1f);

        Assert.Equal(20.0, (double)TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void MatchSourceTime_IntoABlend_SeedsTheBlendClock()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int p = g.AddFloatParameter("P", 0.5f);
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        int blend = g.AddBlend1D(p, new[] { (g.AddClip(Ramp(skeleton)), 0f), (g.AddClip(Ramp(skeleton)), 1f) });
        int b = g.AddState(sm, blend);
        g.AddTransition(sm, a, b, go, 0f).Sync = TransitionSync.MatchSourceTime;
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 6; i++)
            instance.Update(0.1f);
        instance.SetBool("Go", true);
        instance.Update(0.1f);

        Assert.Equal(0.7, (double)((PoseNodeInstance)instance.GetNodeInstance(blend)).NormalizedTime, 3);
    }

    [Fact]
    public void Validation_RejectsMissingTransitionTarget()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        g.AddTransition(sm, a, 5);
        g.SetRoot(sm);

        Assert.Throws<GraphValidationException>(() => g.CreateInstance(skeleton));
    }

    [Fact]
    public void Validation_RejectsMissingDefaultState()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int sm = g.AddStateMachine();
        g.AddState(sm, g.AddClip(Ramp(skeleton)));
        g.SetStateMachineDefault(sm, 3);
        g.SetRoot(sm);

        Assert.Throws<GraphValidationException>(() => g.CreateInstance(skeleton));
    }

    [Fact]
    public void Validation_RejectsPoseNodeCycles()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var self = new AnimationGraph();
        self.SetRoot(self.AddPassthrough(0));
        Assert.Throws<GraphValidationException>(() => self.CreateInstance(skeleton));

        var loop = new AnimationGraph();
        int first = loop.AddPassthrough(1);
        loop.AddPassthrough(first);
        loop.SetRoot(first);
        Assert.Throws<GraphValidationException>(() => loop.CreateInstance(skeleton));
    }

    [Fact]
    public void Validation_RejectsASubGraphReferencingItself()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        g.SetRoot(g.AddReferencedGraph(g));

        Assert.Throws<GraphValidationException>(() => g.CreateInstance(skeleton));
    }

    [Fact]
    public void Validation_RejectsSubGraphLinksToMissingParameters()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var child = new AnimationGraph();
        child.AddFloatParameter("Speed");
        child.SetRoot(child.AddClip(Ramp(skeleton)));

        var g = new AnimationGraph();
        int value = g.AddFloatParameter("Value");
        int sub = g.AddReferencedGraph(child);
        g.LinkGraphParameter(sub, value, "Missing");
        g.SetRoot(sub);

        Assert.Throws<GraphValidationException>(() => g.CreateInstance(skeleton));
    }

    [Fact]
    public void EditingTheGraph_AfterCreatingAnInstance_DoesNotBreakIt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        int b = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);
        instance.Update(0.1f);

        g.AddTransition(sm, a, b, g.AddBoolParameter("Late"));
        instance.Update(0.1f);

        Assert.Equal(2.0, (double)TestClips.RootZ(instance), 2);
    }

    [Fact]
    public void Parameters_RejectTheWrongType()
    {
        var g = new AnimationGraph();
        g.AddBoolParameter("Flag");
        g.AddFloatParameter("Speed");
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        Assert.Throws<ArgumentException>(() => instance.SetFloat("Flag", 1f));
        Assert.Throws<ArgumentException>(() => instance.SetVector("Speed", new Float3(1f, 0f, 0f)));

        int speed = instance.GetParameterIndex("Speed");
        instance.SetFloat(speed, 3f);
        instance.SetInt(speed, 4);
        Assert.Equal(4.0, (double)instance.GetFloat("Speed"), 4);
    }

    [Fact]
    public void FootEventCondition_SeesEveryFootEventInTheFrame()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        g.SetRoot(g.AddClip(Ramp(skeleton, 0f, new FootEvent(FootPhase.LeftFootDown, 0.05f), new FootEvent(FootPhase.RightFootDown, 0.06f))));
        int right = g.AddFootEventCondition(FootPhaseCondition.RightFootDown);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.True(instance.EvaluateValueNode(right).AsBool());
    }

    [Fact]
    public void EventConditions_IgnoreEventsFromTheLosingSideOfATransition()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton, 0f, new IdEvent(new StringID("x"), 0.65f))));
        int b = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, 0f)));
        g.AddTransition(sm, a, b, go, 1f);
        g.SetRoot(sm);
        int activeOnly = g.AddIdEventCondition(new StringID("x"));
        int any = g.AddNode(new IdEventConditionDefinition(new[] { new StringID("x") }, false) { IncludeInactiveBranch = true });
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 6; i++)
            instance.Update(0.1f);
        instance.SetBool("Go", true);
        instance.Update(0.1f);

        Assert.False(instance.EvaluateValueNode(activeOnly).AsBool());
        Assert.True(instance.EvaluateValueNode(any).AsBool());
    }

    [Fact]
    public void NaNBlendParameter_KeepsTheLowestEntry()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P", float.NaN);
        g.SetRoot(g.AddBlend1D(p, new[] { (g.AddClip(TestClips.Const(skeleton, 1f)), 0f), (g.AddClip(TestClips.Const(skeleton, 5f)), 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void Update_DoesNotAllocateOnceWarm()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int speed = g.AddFloatParameter("Speed", 0.5f);
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddBlend1D(speed, new[] { (g.AddClip(Ramp(skeleton, 1f)), 0f), (g.AddClip(Ramp(skeleton, 2f)), 1f) }));
        int b = g.AddState(sm, g.AddClip(Ramp(skeleton, 1f, new IdEvent(new StringID("x"), 0.5f))));
        g.AddTransition(sm, a, b, go, 0.3f);
        g.AddTransition(sm, b, a, g.AddNot(go), 0.3f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        long lowest = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 120; i++)
            {
                instance.SetBool("Go", i % 20 < 10);
                instance.Update(1f / 30f);
            }
            lowest = Math.Min(lowest, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0, lowest);
    }

    [Fact]
    public void ExternalGraph_WithAnotherSkeleton_IsRejected()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var host = new AnimationGraph();
        host.SetRoot(host.AddExternalGraphSlot("slot"));
        AnimationGraphInstance instance = host.CreateInstance(skeleton);

        var other = new AnimationGraph();
        other.SetRoot(other.AddReferencePose());
        AnimationGraphInstance otherInstance = other.CreateInstance(TestSkeletons.MakeMinimalHumanoid());

        Assert.Throws<ArgumentException>(() => instance.SetExternalGraph("slot", otherInstance));
    }

    [Fact]
    public void NodeNames_MustBeUnique()
    {
        var g = new AnimationGraph();
        g.AddFloatParameter("slot");
        Assert.Throws<ArgumentException>(() => g.AddExternalGraphSlot("slot"));
    }

    [Fact]
    public void ResetGraphState_ReturnsToTheDefaultState()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int go = g.AddBoolParameter("Go");
        int sm = g.AddStateMachine();
        int a = g.AddState(sm, g.AddClip(Ramp(skeleton)));
        int b = g.AddState(sm, g.AddClip(TestClips.Const(skeleton, -5f)));
        g.AddTransition(sm, a, b, go, 0f);
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.SetBool("Go", true);
        instance.Update(0.1f);
        instance.Update(0.1f);
        Assert.Equal(-5.0, (double)TestClips.RootZ(instance), 3);

        instance.SetBool("Go", false);
        instance.ResetGraphState();
        instance.Update(0.1f);

        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 2);
    }
}
