using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The State Machine node: states, transitions, their timing and synchronisation.</summary>
public class N_StateMachine_Tests
{
    private static AnimationClip Ramp(Skeleton skeleton, float rootTravel = 0f, params AnimationEvent[] events)
        => TestClips.Ramp(skeleton, rootTravel: rootTravel, events: events);

    private static (AnimationGraphInstance Instance, AnimationGraph Graph) BuildIdleWalk(Skeleton skeleton)
    {
        var graph = new AnimationGraph();
        int isWalking = graph.AddBoolParameter("IsWalking");
        int idle = graph.AddClip(TestClips.Const(skeleton, 0f));
        int walk = graph.AddClip(TestClips.Const(skeleton, 10f));
        int sm = graph.AddStateMachine();
        int idleState = graph.AddState(sm, idle, "Idle");
        int walkState = graph.AddState(sm, walk, "Walk");
        graph.AddTransition(sm, idleState, walkState, isWalking, duration: 0.2f);
        graph.SetStateMachineDefault(sm, idleState);
        graph.SetRoot(sm);
        return (graph.CreateInstance(skeleton), graph);
    }

    [Fact]
    public void EaseIn_ShapesBlendWeight()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int a = graph.AddClip(TestClips.Const(skeleton, 0f));
        int b = graph.AddClip(TestClips.Const(skeleton, 10f));
        int sm = graph.AddStateMachine();
        int sa = graph.AddState(sm, a);
        int sb = graph.AddState(sm, b);
        TransitionInfo tr = graph.AddTransition(sm, sa, sb, conditionNodeIndex: -1, duration: 0.2f);
        tr.Easing = TransitionEasing.EaseIn;
        graph.SetStateMachineDefault(sm, sa);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.1f); // raw weight 0.5 -> EaseIn squares it to 0.25 -> z = 2.5
        Assert.Equal(2.5, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void MatchSourceTime_SeedsTargetToSourcePhase()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int go = graph.AddBoolParameter("Go");
        int a = graph.AddClip(TestClips.Ramp(skeleton));
        int b = graph.AddClip(TestClips.Ramp(skeleton));
        int sm = graph.AddStateMachine();
        int sa = graph.AddState(sm, a);
        int sb = graph.AddState(sm, b);
        TransitionInfo tr = graph.AddTransition(sm, sa, sb, conditionNodeIndex: go, duration: 0f);
        tr.Sync = TransitionSync.MatchSourceTime;
        graph.SetStateMachineDefault(sm, sa);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        for (int i = 0; i < 5; i++)
            instance.Update(0.1f); // source advances to normalized 0.5

        instance.SetBool("Go", true);
        instance.Update(0.001f); // transition fires; target seeded to ~0.5, not 0
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 1);
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
    public void ATransitionFromABackwardState_FitsWhatIsLeftBehindIt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int go = graph.AddBoolParameter("Go");
        int reverse = graph.AddConstBool(true);
        int Backward() => graph.AddNode(new ClipNodeDefinition(TestClips.Ramp(skeleton, duration: 1f)) { Loop = true, PlayInReverseNodeIndex = reverse });

        int machine = graph.AddStateMachine();
        int a = graph.AddState(machine, Backward(), "A");
        int b = graph.AddState(machine, Backward(), "B");
        TransitionInfo transition = graph.AddTransition(machine, a, b, go, duration: 1f);
        transition.ClampToSource = true;
        graph.SetStateMachineDefault(machine, a);
        graph.SetRoot(machine);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        // A wraps round to its end and plays toward its start, so a tenth in, most of it is still to come.
        for (int i = 0; i < 6; i++) instance.Update(1f / 60f);
        instance.SetBool("Go", true);
        for (int i = 0; i < 18; i++) instance.Update(1f / 60f);

        var running = (PoseNodeInstance)instance.TryGetNodeInstance(machine)!;
        Assert.True(((IStateMachineState)running).IsTransitioning, "the blend was cut to what lay ahead of a backward clip");
        Assert.True(running.PlayingBackward, "the machine lost its direction during the blend");
    }

    [Fact]
    public void ATransitionStartedPlayingBackward_CountsItsFirstFrame()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int go = graph.AddBoolParameter("Go", true);
        int machine = graph.AddStateMachine();
        int a = graph.AddState(machine, graph.AddClip(TestClips.Const(skeleton, 0f)), "A");
        int b = graph.AddState(machine, graph.AddClip(TestClips.Const(skeleton, 10f)), "B");
        graph.AddTransition(machine, a, b, go, duration: 0.1f);
        graph.SetStateMachineDefault(machine, a);
        graph.SetRoot(machine);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(-0.05f);

        Assert.InRange(TestClips.RootZ(instance), 4f, 6f);
    }

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
    public void StartsInDefaultState()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        (AnimationGraphInstance instance, _) = BuildIdleWalk(skeleton);
        instance.Update(0.016f);
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void Transition_CrossFadesToTargetState()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        (AnimationGraphInstance instance, _) = BuildIdleWalk(skeleton);

        instance.SetBool("IsWalking", true);
        instance.Update(0.1f); // halfway through the 0.2s transition
        float mid = instance.Pose.GetTransform(0).position.Z;
        Assert.InRange(mid, 1f, 9f); // blending between idle (0) and walk (10)

        for (int i = 0; i < 10; i++)
            instance.Update(0.05f); // finish the transition

        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void AlwaysTransition_FiresImmediately()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int a = graph.AddClip(TestClips.Const(skeleton, 1f));
        int b = graph.AddClip(TestClips.Const(skeleton, 5f));
        int sm = graph.AddStateMachine();
        int sa = graph.AddState(sm, a);
        int sb = graph.AddState(sm, b);
        graph.AddTransition(sm, sa, sb, conditionNodeIndex: -1, duration: 0f); // always, instant
        graph.SetStateMachineDefault(sm, sa);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.016f);
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
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
