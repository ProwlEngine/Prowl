using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>The offset decay behind inertialized transitions.</summary>
public class InertializerTests
{
    private const float Step = 1f / 60f;

    private static Skeleton Rig() => TestSkeletons.MakeChain();

    private static Pose Turned(Skeleton skeleton, float degrees)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(1, new Transform3D(
            pose.GetTransform(1).position,
            Quaternion.AxisAngle(new Float3(0f, 0f, 1f), degrees * MathF.PI / 180f),
            Float3.One));
        return pose;
    }

    private static float AngleDeg(Quaternion a, Quaternion b)
        => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))), 0f, 1f)) * 180f / MathF.PI;

    // Feeds a still pose, jumps to another, and returns the output angle over time.
    private static (Inertializer Blender, Pose Result) Settled(Skeleton skeleton, Pose start, int frames = 4)
    {
        var blender = new Inertializer(skeleton);
        var result = new Pose(skeleton);
        for (int i = 0; i < frames; i++)
            blender.Apply(start, result, Step);
        return (blender, result);
    }

    [Fact]
    public void WithoutAJump_ThePoseComesThroughUntouched()
    {
        Skeleton skeleton = Rig();
        Pose source = Turned(skeleton, 30f);
        (Inertializer blender, Pose result) = Settled(skeleton, source);

        Assert.False(blender.IsBlending);
        Assert.Equal(0f, AngleDeg(source.GetTransform(1).rotation, result.GetTransform(1).rotation), 4);
    }

    // The first frame after the jump must still show the old pose, or the transition pops.
    [Fact]
    public void TheFirstFrameAfterAJump_StaysOnTheOldPose()
    {
        Skeleton skeleton = Rig();
        Pose old = Turned(skeleton, 40f);
        Pose jumped = Turned(skeleton, -20f);
        (Inertializer blender, Pose result) = Settled(skeleton, old);

        blender.Begin(jumped, 0.3f);
        blender.Apply(jumped, result, 0f);

        Assert.Equal(40f, AngleDeg(Quaternion.Identity, result.GetTransform(1).rotation), 1);
    }

    [Fact]
    public void TheGapIsGoneWhenTheBlendEnds()
    {
        Skeleton skeleton = Rig();
        Pose old = Turned(skeleton, 40f);
        Pose jumped = Turned(skeleton, -20f);
        (Inertializer blender, Pose result) = Settled(skeleton, old);

        blender.Begin(jumped, 0.25f);
        for (int i = 0; i < 16; i++)
            blender.Apply(jumped, result, Step);

        Assert.False(blender.IsBlending);
        Assert.Equal(0f, AngleDeg(jumped.GetTransform(1).rotation, result.GetTransform(1).rotation), 3);
    }

    // The point over a cross fade: the 60 degree gap is never taken in one frame, the motion eases in
    // and out instead of running at a constant rate, and it never swings past the target.
    [Fact]
    public void TheOutputEasesAcrossTheJumpWithoutOvershooting()
    {
        Skeleton skeleton = Rig();
        Pose old = Turned(skeleton, 40f);
        Pose jumped = Turned(skeleton, -20f);
        (Inertializer blender, Pose result) = Settled(skeleton, old);

        blender.Begin(jumped, 0.3f);
        var offsets = new List<float>();
        for (int i = 0; i < 20; i++)
        {
            blender.Apply(jumped, result, Step);
            offsets.Add(AngleDeg(jumped.GetTransform(1).rotation, result.GetTransform(1).rotation));
        }

        var steps = new List<float>();
        for (int i = 1; i < offsets.Count; i++)
        {
            Assert.True(offsets[i] <= offsets[i - 1] + 1e-3f, $"the gap grew at step {i}");
            steps.Add(offsets[i - 1] - offsets[i]);
        }

        Assert.True(steps[0] < 60f * 0.15f, $"first step took {steps[0]:N2} of the 60 degree gap");
        Assert.True(steps.Max() > steps[0] * 1.5f, "the blend never accelerates, so it is not easing in");
        for (int i = 1; i < steps.Count; i++)
            Assert.True(MathF.Abs(steps[i] - steps[i - 1]) < 3f, $"step {i} changed speed by {MathF.Abs(steps[i] - steps[i - 1]):N2} degrees per frame");
    }

    // A gap that is already closing should not be dragged backwards: velocity carries through.
    [Fact]
    public void AGapAlreadyClosing_KeepsItsDirection()
    {
        Skeleton skeleton = Rig();
        var blender = new Inertializer(skeleton);
        var result = new Pose(skeleton);

        for (int i = 0; i < 6; i++)
            blender.Apply(Turned(skeleton, 30f - i * 5f), result, Step);

        Pose target = Turned(skeleton, 0f);
        blender.Begin(target, 0.4f);
        float first = AngleDeg(Quaternion.Identity, result.GetTransform(1).rotation);
        blender.Apply(target, result, Step);
        float second = AngleDeg(Quaternion.Identity, result.GetTransform(1).rotation);

        Assert.True(second < first, $"the gap grew from {first:N2} to {second:N2}");
    }

    [Theory]
    [InlineData(1f / 30f)]
    [InlineData(1f / 144f)]
    public void TheBlendTakesTheSameWallClockTimeAtAnyFrameRate(float step)
    {
        Skeleton skeleton = Rig();
        Pose old = Turned(skeleton, 40f);
        Pose jumped = Turned(skeleton, 0f);
        var blender = new Inertializer(skeleton);
        var result = new Pose(skeleton);
        for (int i = 0; i < 4; i++)
            blender.Apply(old, result, step);

        blender.Begin(jumped, 0.25f);
        float elapsed = 0f;
        while (blender.IsBlending && elapsed < 1f)
        {
            blender.Apply(jumped, result, step);
            elapsed += step;
        }

        Assert.InRange(elapsed, 0.25f - step * 1.5f, 0.25f + step * 1.5f);
    }

    [Fact]
    public void PositionsAndFloatChannelsBlendToo()
    {
        var skeleton = new Skeleton(
            new[] { new StringID("Root") },
            new[] { Skeleton.InvalidIndex },
            new[] { Transform3D.Identity },
            -1,
            new[] { new StringID("Smile") });

        Pose Frame(float z, float smile)
        {
            var pose = new Pose(skeleton);
            pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
            pose.SetFloat(0, smile);
            return pose;
        }

        var blender = new Inertializer(skeleton);
        var result = new Pose(skeleton);
        Pose old = Frame(2f, 80f);
        for (int i = 0; i < 4; i++)
            blender.Apply(old, result, Step);

        Pose jumped = Frame(0f, 0f);
        blender.Begin(jumped, 0.25f);
        blender.Apply(jumped, result, Step);

        Assert.InRange(result.GetTransform(0).position.Z, 1.5f, 2f);
        Assert.InRange(result.GetFloat(0), 60f, 80f);

        for (int i = 0; i < 20; i++)
            blender.Apply(jumped, result, Step);

        Assert.Equal(0f, result.GetTransform(0).position.Z, 3);
        Assert.Equal(0f, result.GetFloat(0), 3);
    }

    [Fact]
    public void BeginningWithoutHistory_ShowsTheTargetStraightAway()
    {
        Skeleton skeleton = Rig();
        var blender = new Inertializer(skeleton);
        var result = new Pose(skeleton);
        Pose target = Turned(skeleton, 25f);

        blender.Begin(target, 0.3f);
        blender.Apply(target, result, Step);

        Assert.Equal(25f, AngleDeg(Quaternion.Identity, result.GetTransform(1).rotation), 2);
    }

    [Fact]
    public void ResetDropsARunningBlend()
    {
        Skeleton skeleton = Rig();
        Pose old = Turned(skeleton, 40f);
        Pose jumped = Turned(skeleton, 0f);
        (Inertializer blender, Pose result) = Settled(skeleton, old);
        blender.Begin(jumped, 0.3f);

        blender.Reset();
        blender.Apply(jumped, result, Step);

        Assert.False(blender.IsBlending);
        Assert.Equal(0f, AngleDeg(Quaternion.Identity, result.GetTransform(1).rotation), 3);
    }

    [Fact]
    public void ARunningBlendDoesNotAllocate()
    {
        Skeleton skeleton = Rig();
        Pose old = Turned(skeleton, 40f);
        Pose jumped = Turned(skeleton, 0f);
        (Inertializer blender, Pose result) = Settled(skeleton, old);
        blender.Begin(jumped, 10f);
        blender.Apply(jumped, result, Step);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
            blender.Apply(jumped, result, Step);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
