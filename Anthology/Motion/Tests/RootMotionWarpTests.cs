using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class RootMotionWarpTests
{
    private static Transform3D Move(float x, float z) => new(new Float3(x, 0f, z), Quaternion.Identity, Float3.One);

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
}
