using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Root motion warping: overriding speed and heading, and warping a trajectory onto a goal.</summary>
public class RootMotionWarp_Tests
{
    private static Transform3D Move(float x, float z) => new(new Float3(x, 0f, z), Quaternion.Identity, Float3.One);

    private static Transform3D At(float x, float y, float z) => new(new Float3(x, y, z), Quaternion.Identity, Float3.One);

    private static readonly Float3 Forward = new(0f, 0f, 1f);

    // Root travels +Z by distance over 1 second, sampled at frames + 1 evenly spaced frames.
    private static RootMotion StraightMotion(float distance, int frames)
    {
        var transforms = new Transform3D[frames + 1];
        for (int i = 0; i <= frames; i++)
            transforms[i] = At(0f, 0f, distance * i / frames);
        return new RootMotion(transforms, 1f);
    }

    private static float YawDegrees(Quaternion q)
    {
        Float3 f = q * Forward;
        return MathF.Atan2(f.X, f.Z) * 180f / MathF.PI;
    }

    [Fact]
    public void Override_ScalesLinearSpeed()
    {
        var options = RootMotionOverrideOptions.Default;
        options.LinearSpeedScale = 2f;
        Transform3D result = RootMotionWarp.Override(Move(0f, 1f), 1f / 30f, options);
        Assert.Equal(2.0, (double)result.position.Z, 4);
    }

    [Fact]
    public void Override_ClampsLinearSpeed()
    {
        var options = RootMotionOverrideOptions.Default;
        options.MaxLinearSpeed = 3f; // units/sec
        float dt = 0.5f;             // so max move this frame = 1.5
        Transform3D result = RootMotionWarp.Override(Move(0f, 5f), dt, options);
        Assert.Equal(1.5, (double)result.position.Z, 3);
    }

    [Fact]
    public void Override_RedirectsHeading()
    {
        var options = RootMotionOverrideOptions.Default;
        options.OverrideHeading = true;
        options.DesiredHeadingForward = new Float3(1f, 0f, 0f); // want to move along +X
        Transform3D result = RootMotionWarp.Override(Move(0f, 2f), 1f / 30f, options); // was moving +Z by 2
        Assert.Equal(2.0, (double)result.position.X, 3);
        Assert.Equal(0.0, (double)result.position.Z, 3);
    }

    [Fact]
    public void WarpTrajectory_EndsAtTarget()
    {
        // Original motion: travels +Z by 2 over two frames.
        var rm = new RootMotion(new[] { Transform3D.Identity, Move(0f, 2f) }, 1f);
        Transform3D[] warped = RootMotionWarp.WarpTrajectory(rm, new Float3(3f, 0f, 0f)); // want to end at +X 3

        Float3 end = warped[^1].position;
        Assert.Equal(3.0, (double)end.X, 3);
        Assert.Equal(0.0, (double)end.Z, 3);
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
