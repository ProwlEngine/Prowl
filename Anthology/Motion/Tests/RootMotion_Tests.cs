using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Root motion: deltas over spans of a clip.</summary>
public class RootMotion_Tests
{
    private static RootMotion Make()
    {
        var frames = new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 0f, 1f), Quaternion.Identity, Float3.One),
        };
        return new RootMotion(frames, durationSeconds: 1f);
    }

    [Fact]
    public void TotalDelta_IsEndMinusStart()
        => Assert.Equal(1.0, (double)Make().TotalDelta.position.Z, 4);

    [Fact]
    public void SampleDelta_FullRange_EqualsTotal()
        => Assert.Equal(1.0, (double)Make().SampleDelta(0f, 1f).position.Z, 4);

    [Fact]
    public void RootMotionDelta_IsExactForNonUniformScale()
    {
        var from = new Transform3D(Float3.Zero, Quaternion.AxisAngle(new Float3(0f, 1f, 0f), MathF.PI / 2f), new Float3(2f, 1f, 1f));
        var to = new Transform3D(new Float3(0f, 0f, 4f), from.rotation, from.scale);
        var rootMotion = new RootMotion(new[] { from, to }, 1f);

        Transform3D reached = RootMotionUtil.Apply(from, rootMotion.SampleDelta(0f, 1f));

        Assert.Equal(0.0, (double)reached.position.X, 4);
        Assert.Equal(4.0, (double)reached.position.Z, 4);
    }

    [Fact]
    public void RootMotionDelta_ZeroScaleStaysFinite()
    {
        var from = new Transform3D(Float3.Zero, Quaternion.Identity, new Float3(0f, 1f, 1f));
        var to = new Transform3D(new Float3(1f, 0f, 1f), Quaternion.Identity, Float3.One);
        Transform3D delta = new RootMotion(new[] { from, to }, 1f).SampleDelta(0f, 1f);

        Assert.True(float.IsFinite(delta.position.X) && float.IsFinite(delta.position.Z));
        Assert.True(float.IsFinite(delta.scale.X));
    }

    // The root motion track keeps its own duration, which need not be the clip's.
    [Fact]
    public void ARescaledRootMotionKeepsItsOwnDuration()
    {
        Avatar source = new HumanoidTestRig().BuildAvatar();
        Avatar tall = new HumanoidTestRig { LegScale = 2f }.BuildAvatar();
        var frames = new[] { Transform3D.Identity, new Transform3D(new Float3(0f, 0f, 1f), Quaternion.Identity, Float3.One) };
        var rootMotion = new RootMotion(frames, 4f);
        HumanoidClip clip = HumanoidClip.FromFrames(new[] { new HumanPose(), new HumanPose() }, 1f, source.Humanoid!.Scale, rootMotion);

        Assert.Equal(4f, clip.Bind(tall).RootMotion!.Duration, 3);
    }

    [Fact]
    public void RootMotionApply_MovesByDelta()
    {
        var world = new Transform3D(new Float3(1f, 0f, 0f), Quaternion.Identity, Float3.One);
        var delta = new Transform3D(new Float3(0f, 0f, 2f), Quaternion.Identity, Float3.One);
        Transform3D moved = RootMotionUtil.Apply(world, delta);
        Assert.Equal(1.0, (double)moved.position.X, 4);
        Assert.Equal(2.0, (double)moved.position.Z, 4);
    }
}
