using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Animation clips: sampling poses between frames.</summary>
public class AnimationClip_Tests
{
    private static Pose ReferencePose(Skeleton skeleton)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        return pose;
    }

    private static AnimationClip MakeReferenceClip(Skeleton skeleton, int frameCount, float duration = 1f)
    {
        var frames = new Pose[frameCount];
        for (int i = 0; i < frameCount; i++)
            frames[i] = ReferencePose(skeleton);
        return new AnimationClip(skeleton, frames, duration);
    }

    [Fact]
    public void GetFrameTime_MapsNormalizedTimeToFrame()
    {
        // 30 frames => 29 intervals; halfway => frame 14, 50% through.
        var ft = MakeReferenceClip(TestSkeletons.MakeChain(), 30).GetFrameTime(0.5f);
        Assert.Equal(14, ft.FrameIndex);
        Assert.Equal(0.5, (double)ft.Percentage, 4);
    }

    [Fact]
    public void GetPose_InterpolatesBetweenKeyFrames()
    {
        var skeleton = TestSkeletons.MakeChain();

        var f0 = new Pose(skeleton);
        f0.SetToReferencePose();
        f0.SetTransform(0, new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One));

        var f1 = new Pose(skeleton);
        f1.SetToReferencePose();
        f1.SetTransform(0, new Transform3D(new Float3(0f, 0f, 10f), Quaternion.Identity, Float3.One));

        var clip = new AnimationClip(skeleton, new[] { f0, f1 }, 1f);

        var result = new Pose(skeleton);
        clip.GetPose(0.5f, result);

        Assert.Equal(5.0, (double)result.GetTransform(0).position.Z, 4);
    }

    [Fact]
    public void GetPose_ProducesAValidPose()
    {
        var skeleton = TestSkeletons.MakeChain();
        var result = new Pose(skeleton);
        MakeReferenceClip(skeleton, 10).GetPose(0.5f, result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NaNTime_SamplesAFinitePose()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var pose = new Pose(skeleton);
        TestClips.Ramp(skeleton).GetPose(float.NaN, pose);

        Assert.True(float.IsFinite(pose.GetTransform(0).position.Z));
    }
}
