using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Target Warp node: bending a clip's travel to land on a goal.</summary>
public class N_TargetWarp_Tests
{
    private static AnimationClip ForwardMover(Skeleton skeleton, float distance)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        var rootFrames = new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 0f, distance), Quaternion.Identity, Float3.One),
        };
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f, rootMotion: new RootMotion(rootFrames, 1f));
    }

    // Applies each delta in the frame of the accumulated root, so rotation in the deltas steers the travel.
    private static Float3 AccumulateRootMotion(AnimationGraphInstance instance, int steps, float dt)
    {
        Float3 position = default;
        Quaternion rotation = Quaternion.Identity;
        for (int i = 0; i < steps; i++)
        {
            instance.Update(dt);
            position += rotation * instance.RootMotionDelta.position;
            rotation *= instance.RootMotionDelta.rotation;
        }
        return position;
    }

    private static Transform3D At(float x, float y, float z) => new(new Float3(x, y, z), Quaternion.Identity, Float3.One);

    // Root travels +Z by distance over 1 second, sampled at frames + 1 evenly spaced frames.
    private static RootMotion StraightMotion(float distance, int frames)
    {
        var transforms = new Transform3D[frames + 1];
        for (int i = 0; i <= frames; i++)
            transforms[i] = At(0f, 0f, distance * i / frames);
        return new RootMotion(transforms, 1f);
    }

    private static AnimationClip MoverClip(Skeleton skeleton, RootMotion rootMotion, params AnimationEvent[] events)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        var poses = new Pose[rootMotion.FrameCount];
        for (int i = 0; i < poses.Length; i++)
            poses[i] = pose;
        return new AnimationClip(skeleton, poses, 1f, events: events, rootMotion: rootMotion);
    }

    private static Transform3D Integrate(Transform3D world, Transform3D delta)
        => new(world.position + world.rotation * delta.position, world.rotation * delta.rotation, Float3.One);

    [Fact]
    public void TargetWarp_WarpsOnceItsTargetIsSet()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int target = graph.AddTargetParameter("Goal");
        graph.SetRoot(graph.AddTargetWarp(graph.AddClip(TestClips.Ramp(skeleton, duration: 1f, rootTravel: 1f), loop: false), target));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f);
        instance.SetTarget("Goal", Target.FromWorld(new Transform3D(new Float3(0f, 0f, 3f), Quaternion.Identity, Float3.One)));

        float travelled = instance.RootMotionDelta.position.Z;
        for (int i = 0; i < 70; i++)
        {
            instance.Update(1f / 60f);
            travelled += instance.RootMotionDelta.position.Z;
        }

        Assert.Equal(3f, travelled, 1);
    }

    [Fact]
    public void TargetWarp_ReachesItsGoalInOnePassOfALoopingClip()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int goal = graph.AddConstVector(new Float3(0f, 0f, 2f));
        graph.SetRoot(graph.AddTargetWarp(graph.AddClip(TestClips.Ramp(skeleton, duration: 1f, rootTravel: 1f), loop: true), goal));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        // One warped pass covers the 2 asked for, the next plays the clip's own 1.
        float travelled = 0f;
        for (int i = 0; i < 120; i++)
        {
            instance.Update(1f / 60f);
            travelled += instance.RootMotionDelta.position.Z;
        }

        Assert.Equal(3f, travelled, 1);
    }

    [Fact]
    public void TargetWarp_SolvesAgainFromWhereTheCharacterIsAfterAReseek()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int goal = graph.AddTargetParameter("Goal", Target.FromWorld(new Transform3D(new Float3(0f, 0f, 2f), Quaternion.Identity, Float3.One)));
        int restart = graph.AddBoolParameter("Restart");
        int clip = graph.AddNode(new ClipNodeDefinition(TestClips.Ramp(skeleton, duration: 1f, rootTravel: 1f)) { Loop = false, ResetTimeNodeIndex = restart });
        graph.SetRoot(graph.AddTargetWarp(clip, goal));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        var world = Transform3D.Identity;
        void Play(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                instance.Update(1f / 60f, world);
                world = new Transform3D(world.position + instance.RootMotionDelta.position, world.rotation, world.scale);
            }
        }

        Play(30);
        instance.SetBool("Restart", true);
        Play(70);

        Assert.Equal(2f, world.position.Z, 1);
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
    public void TargetWarp_ReachesDesiredDisplacement()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int goal = graph.AddVectorParameter("Goal", new Float3(2f, 0f, 0f)); // end up 2 along +X
        int clip = graph.AddClip(ForwardMover(skeleton, 2f), loop: false);    // clip travels +Z by 2
        int warp = graph.AddTargetWarp(clip, goal);
        graph.SetRoot(warp);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        Float3 total = AccumulateRootMotion(instance, 12, 0.1f);

        Assert.Equal(2.0, (double)total.X, 2);
        Assert.Equal(0.0, (double)total.Z, 2);
    }

    [Fact]
    public void TargetWarp_WarpXYRuleWarpsTheHorizontalPlane()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int goal = g.AddVectorParameter("Goal", new Float3(1f, 0.5f, 4f));
        int clip = g.AddClip(MoverClip(skeleton, StraightMotion(2f, 10), new TargetWarpEvent(TargetWarpRule.WarpXY, 0f, 1f)), loop: false);
        g.SetRoot(g.AddTargetWarp(clip, goal));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        Transform3D world = Transform3D.Identity;
        for (int i = 0; i < 12; i++)
        {
            instance.Update(0.1f);
            world = Integrate(world, instance.RootMotionDelta);
        }

        Assert.Equal(1.0, (double)world.position.X, 2);
        Assert.Equal(0.0, (double)world.position.Y, 2);
        Assert.Equal(4.0, (double)world.position.Z, 2);
    }

    [Fact]
    public void TargetWarp_WarpZRuleWarpsOnlyTheVertical()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int goal = g.AddVectorParameter("Goal", new Float3(1f, 0.5f, 4f));
        int clip = g.AddClip(MoverClip(skeleton, StraightMotion(2f, 10), new TargetWarpEvent(TargetWarpRule.WarpZ, 0f, 1f)), loop: false);
        g.SetRoot(g.AddTargetWarp(clip, goal));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        Transform3D world = Transform3D.Identity;
        for (int i = 0; i < 12; i++)
        {
            instance.Update(0.1f);
            world = Integrate(world, instance.RootMotionDelta);
        }

        Assert.Equal(0.0, (double)world.position.X, 2);
        Assert.Equal(0.5, (double)world.position.Y, 2);
        Assert.Equal(2.0, (double)world.position.Z, 2);
    }

    [Fact]
    public void TargetWarp_LeadInBeforeTheEventWindowIsNotWarped()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int goal = g.AddVectorParameter("Goal", new Float3(0f, 0f, 4f));
        int clip = g.AddClip(MoverClip(skeleton, StraightMotion(2f, 10), new TargetWarpEvent(TargetWarpRule.WarpXYZ, 0.5f, 0.5f)), loop: false);
        g.SetRoot(g.AddTargetWarp(clip, goal));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        Transform3D world = Transform3D.Identity;
        for (int i = 0; i < 5; i++)
        {
            instance.Update(0.1f);
            world = Integrate(world, instance.RootMotionDelta);
        }
        Assert.Equal(1.0, (double)world.position.Z, 2);

        for (int i = 0; i < 7; i++)
        {
            instance.Update(0.1f);
            world = Integrate(world, instance.RootMotionDelta);
        }
        Assert.Equal(4.0, (double)world.position.Z, 2);
    }

    [Fact]
    public void TargetWarp_StartingMidClipOnlyCoversTheRemainingRange()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int goal = g.AddVectorParameter("Goal", new Float3(0f, 0f, 2f));
        int go = g.AddBoolParameter("Go");
        int lead = g.AddClip(MoverClip(skeleton, StraightMotion(2f, 10)), loop: false);
        int clip = g.AddClip(MoverClip(skeleton, StraightMotion(2f, 10)), loop: false);
        int warp = g.AddTargetWarp(clip, goal);
        int sm = g.AddStateMachine();
        int from = g.AddState(sm, lead);
        int to = g.AddState(sm, warp);
        g.AddTransition(sm, from, to, go, duration: 0f).Sync = TransitionSync.MatchSourceTime;
        g.SetRoot(sm);
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 5; i++)
            instance.Update(0.1f);
        instance.SetBool("Go", true);
        instance.Update(0.1f);
        instance.Update(0.1f);

        Assert.Equal(0.7, (double)instance.NormalizedTime, 3);
        Assert.Equal(0.2, (double)instance.RootMotionDelta.position.Z, 3);
    }
}
