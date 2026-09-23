using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Orientation Warp node: bending a clip's travel toward a direction or by an angle.</summary>
public class N_OrientationWarp_Tests
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
    public void OrientationWarp_TurnsOncePerStartOnALoopingClip()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(TestClips.Ramp(skeleton, duration: 1f, rootTravel: 1f), loop: true);
        graph.SetRoot(graph.AddOrientationWarpAngle(clip, graph.AddConstFloat(90f)));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        // Facing after each of three loops: the turn is made in the first and never again.
        Quaternion facing = Quaternion.Identity;
        var yaws = new List<float>();
        for (int i = 0; i < 180; i++)
        {
            instance.Update(1f / 60f);
            facing = Quaternion.Normalize(facing * instance.RootMotionDelta.rotation);
            if (i % 60 != 59) continue;
            Float3 forward = facing * Float3.UnitZ;
            yaws.Add(MathF.Atan2(forward.X, forward.Z) * Maths.Rad2Deg);
        }

        Assert.All(yaws, yaw => Assert.Equal(90f, yaw, 0));
    }

    [Theory]
    [InlineData(0.03f)]
    [InlineData(0.07f)]
    public void OrientationWarp_KeepsTheWholeTurnOnTheFrameItWraps(float dt)
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        // The turn is spread over the second half, so its last part lands on the frame the clip wraps.
        int clip = graph.AddClip(TestClips.Ramp(skeleton, duration: 1f, rootTravel: 1f, events: new OrientationWarpEvent(0.5f, 0.5f)), loop: true);
        graph.SetRoot(graph.AddOrientationWarpAngle(clip, graph.AddConstFloat(90f)));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Quaternion facing = Quaternion.Identity;
        for (float time = 0f; time < 1.5f; time += dt)
        {
            instance.Update(dt);
            facing = Quaternion.Normalize(facing * instance.RootMotionDelta.rotation);
        }

        Float3 forward = facing * Float3.UnitZ;
        Assert.Equal(90f, MathF.Atan2(forward.X, forward.Z) * Maths.Rad2Deg, 0);
    }

    [Fact]
    public void OrientationWarp_WarpsAgainAfterAReseek()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int heading = graph.AddVectorParameter("Heading", new Float3(1f, 0f, 0f));
        int restart = graph.AddBoolParameter("Restart");
        int clip = graph.AddNode(new ClipNodeDefinition(TestClips.Ramp(skeleton, duration: 2f, rootTravel: 2f)) { Loop = false, ResetTimeNodeIndex = restart });
        graph.SetRoot(graph.AddOrientationWarp(clip, heading));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Quaternion facing = Quaternion.Identity;
        void Play(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                instance.Update(1f / 60f);
                facing = Quaternion.Normalize(facing * instance.RootMotionDelta.rotation);
            }
        }

        Play(30);
        // Now facing the way it was asked, straight ahead is where it should keep heading.
        instance.SetVector("Heading", new Float3(0f, 0f, 1f));
        instance.SetBool("Restart", true);
        Play(30);

        Float3 forward = facing * Float3.UnitZ;
        Assert.Equal(90f, MathF.Atan2(forward.X, forward.Z) * Maths.Rad2Deg, 0);
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
    public void OrientationWarp_RotatesTravelDirectionToTarget()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int dir = graph.AddVectorParameter("Dir", new Float3(1f, 0f, 0f)); // want to travel +X
        int clip = graph.AddClip(ForwardMover(skeleton, 1f), loop: false);  // clip travels +Z by 1
        int warp = graph.AddOrientationWarp(clip, dir);
        graph.SetRoot(warp);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        Float3 total = AccumulateRootMotion(instance, 12, 0.1f);

        Assert.Equal(1.0, (double)total.X, 2); // re-headed onto +X
        Assert.Equal(0.0, (double)total.Z, 2);
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
}
