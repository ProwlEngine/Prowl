using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class WarpRegressionTests
{
    private static readonly Float3 Forward = new(0f, 0f, 1f);

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

    private static float YawDegrees(Quaternion q)
    {
        Float3 f = q * Forward;
        return MathF.Atan2(f.X, f.Z) * 180f / MathF.PI;
    }

    [Fact]
    public void OrientationWarp_TurnsOnceInsteadOfSpinningEveryFrame()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int dir = g.AddVectorParameter("Dir", new Float3(1f, 0f, 0f));
        int clip = g.AddClip(MoverClip(skeleton, StraightMotion(1f, 10), new OrientationWarpEvent(0.2f, 0.3f)), loop: false);
        g.SetRoot(g.AddOrientationWarp(clip, dir));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        Transform3D world = Transform3D.Identity;
        for (int i = 0; i < 10; i++)
        {
            instance.Update(0.1f);
            world = Integrate(world, instance.RootMotionDelta);
            float yaw = YawDegrees(world.rotation);
            Assert.True(yaw > -0.5f && yaw < 90.5f, $"step {i} yaw {yaw}");
        }

        Assert.Equal(90.0, (double)YawDegrees(world.rotation), 1);
        Assert.Equal(0.5, (double)world.position.X, 2);
        Assert.Equal(0.5, (double)world.position.Z, 2);
    }

    [Fact]
    public void OrientationWarpAngle_TurnsTheTravelOnceByTheOffset()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int angle = g.AddFloatParameter("Angle", -90f);
        int clip = g.AddClip(MoverClip(skeleton, StraightMotion(1f, 10), new OrientationWarpEvent(0f, 0.5f)), loop: false);
        g.SetRoot(g.AddOrientationWarpAngle(clip, angle));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        Transform3D world = Transform3D.Identity;
        for (int i = 0; i < 10; i++)
        {
            instance.Update(0.1f);
            world = Integrate(world, instance.RootMotionDelta);
        }

        Assert.Equal(-90.0, (double)YawDegrees(world.rotation), 1);
        Assert.Equal(-0.5, (double)world.position.X, 2);
        Assert.Equal(0.5, (double)world.position.Z, 2);
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

    [Fact]
    public void WarpTrajectory_WarpsAnInPlaceClip()
    {
        var rm = new RootMotion(new[] { Transform3D.Identity, Transform3D.Identity, Transform3D.Identity }, 1f);
        Transform3D[] warped = RootMotionWarp.WarpTrajectory(rm, new Float3(0f, 0f, 2f));

        Assert.Equal(1.0, (double)warped[1].position.Z, 3);
        Assert.Equal(2.0, (double)warped[2].position.Z, 3);
    }

    [Fact]
    public void WarpTrajectory_KeepsTheJumpApexWhenStretchingDistance()
    {
        var rm = new RootMotion(new[] { At(0f, 0f, 0f), At(0f, 0.75f, 0.5f), At(0f, 1f, 1f), At(0f, 0.75f, 1.5f), At(0f, 0f, 2f) }, 1f);
        Transform3D[] warped = RootMotionWarp.WarpTrajectory(rm, new Float3(0f, 0f, 6f));

        Assert.Equal(1.0, (double)warped[2].position.Y, 3);
        Assert.Equal(3.0, (double)warped[2].position.Z, 3);
        Assert.Equal(6.0, (double)warped[4].position.Z, 3);
    }

    [Fact]
    public void WarpTrajectory_TurnsTheFrameRotationsWithThePath()
    {
        Transform3D[] warped = RootMotionWarp.WarpTrajectory(StraightMotion(2f, 4), new Float3(3f, 0f, 0f));

        Assert.Equal(90.0, (double)YawDegrees(warped[^1].rotation), 1);
    }

    [Fact]
    public void Override_HeadingReversalDoesNotFlipVerticalMotion()
    {
        var options = RootMotionOverrideOptions.Default;
        options.OverrideHeading = true;
        options.DesiredHeadingForward = new Float3(-1f, 0f, 0f);

        Transform3D result = RootMotionWarp.Override(At(0.1f, 0.02f, 0f), 1f / 30f, options);

        Assert.Equal(-0.1, (double)result.position.X, 4);
        Assert.Equal(0.02, (double)result.position.Y, 4);
        Assert.Equal(0.0, (double)result.position.Z, 4);
    }

    [Fact]
    public void OverrideOptions_ObjectInitializerKeepsUnitSpeedScale()
    {
        var options = new RootMotionOverrideOptions { MaxLinearSpeed = 10f };
        Transform3D result = RootMotionWarp.Override(At(0f, 0f, 0.1f), 1f / 30f, options);

        Assert.Equal(0.1, (double)result.position.Z, 4);
    }
}
