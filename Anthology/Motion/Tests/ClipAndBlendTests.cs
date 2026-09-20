using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class AnimationClipTests
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
    public void FrameCountAndDuration_MatchInput()
    {
        var clip = MakeReferenceClip(TestSkeletons.MakeChain(), 30);
        Assert.Equal(30, clip.FrameCount);
        Assert.Equal(1.0, (double)clip.Duration, 5);
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
}

public class BlenderTests
{
    [Fact]
    public void Blend_Halfway_AveragesPositions()
    {
        var skeleton = TestSkeletons.MakeChain();

        var a = new Pose(skeleton);
        a.SetToReferencePose();
        a.SetTransform(0, new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One));

        var b = new Pose(skeleton);
        b.SetToReferencePose();
        b.SetTransform(0, new Transform3D(new Float3(0f, 0f, 8f), Quaternion.Identity, Float3.One));

        var result = new Pose(skeleton);
        Blender.Blend(result, a, b, 0.5f);

        Assert.True(result.IsValid);
        Assert.Equal(4.0, (double)result.GetTransform(0).position.Z, 4);
    }

    [Fact]
    public void AdditiveBlend_AddsWeightedDelta()
    {
        var skeleton = TestSkeletons.MakeChain();

        var basePose = new Pose(skeleton);
        basePose.SetToZeroPose();
        basePose.SetTransform(0, new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One));

        var additive = new Pose(skeleton);
        additive.SetToZeroPose(); // identity deltas (pos 0, rot identity, scale 1)
        additive.SetTransform(0, new Transform3D(new Float3(0f, 0f, 4f), Quaternion.Identity, Float3.One));

        var result = new Pose(skeleton);
        Blender.AdditiveBlend(result, basePose, additive, 0.5f);

        Assert.Equal(2.0, (double)result.GetTransform(0).position.Z, 4); // 0 + 4 * 0.5
    }

    [Fact]
    public void BlendRootMotion_WeightZero_ReturnsSource()
    {
        var src = new Transform3D(new Float3(1f, 0f, 0f), Quaternion.Identity, Float3.One);
        var dst = new Transform3D(new Float3(0f, 0f, 1f), Quaternion.Identity, Float3.One);
        Assert.Equal(1.0, (double)Blender.BlendRootMotionDeltas(src, dst, 0f).position.X, 5);
    }

    [Fact]
    public void BlendRootMotion_WeightOne_ReturnsTarget()
    {
        var src = new Transform3D(new Float3(1f, 0f, 0f), Quaternion.Identity, Float3.One);
        var dst = new Transform3D(new Float3(0f, 0f, 1f), Quaternion.Identity, Float3.One);
        Assert.Equal(1.0, (double)Blender.BlendRootMotionDeltas(src, dst, 1f).position.Z, 5);
    }

    [Fact]
    public void MaskedBlend_ZeroWeightBone_KeepsSource()
    {
        var skeleton = TestSkeletons.MakeChain();

        var a = new Pose(skeleton);
        a.SetToReferencePose();
        a.SetTransform(0, new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One));

        var b = new Pose(skeleton);
        b.SetToReferencePose();
        b.SetTransform(0, new Transform3D(new Float3(0f, 0f, 10f), Quaternion.Identity, Float3.One));

        var mask = new BoneMask(skeleton, 0f); // bone 0 fully masked out
        var result = new Pose(skeleton);
        Blender.Blend(result, a, b, 1f, mask);

        Assert.Equal(0.0, (double)result.GetTransform(0).position.Z, 4); // stays at source
    }
}

public class RootMotionTests
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
}
