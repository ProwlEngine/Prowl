using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Pose blending: interpolative, masked and additive blends, and root motion blending.</summary>
public class Blender_Tests
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

    [Fact]
    public void Blend_OfTwoAdditivePoses_StaysAdditive()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var a = new Pose(skeleton);
        a.SetToZeroPose();
        var b = new Pose(skeleton);
        b.SetToZeroPose();
        var result = new Pose(skeleton);

        Blender.Blend(result, a, b, 0.5f);

        Assert.Equal(PoseState.AdditivePose, result.State);
    }

    [Fact]
    public void AdditiveBlend_RejectsAFullPoseAsTheAdditiveInput()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var full = new Pose(skeleton);
        full.SetToReferencePose();

        Assert.Throws<ArgumentException>(() => Blender.AdditiveBlend(new Pose(skeleton), full, full, 1f));
    }

    [Fact]
    public void AdditiveBlend_AddsTheScaleDelta()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var basePose = new Pose(skeleton);
        basePose.SetToReferencePose();
        var additive = new Pose(skeleton);
        additive.SetToZeroPose();
        additive.SetTransform(0, new Transform3D(Float3.Zero, Quaternion.Identity, new Float3(0.5f, 0f, 0f)));
        var result = new Pose(skeleton);

        Blender.AdditiveBlend(result, basePose, additive, 1f);

        Assert.Equal(1.5, (double)result.GetTransform(0).scale.X, 4);
        Assert.Equal(1.0, (double)result.GetTransform(1).scale.X, 4);
    }

    [Fact]
    public void AMaskedBlend_WeighsEachChannelByItsOwnWeight()
    {
        var skeleton = new Skeleton(new[] { new StringID("Root") }, new[] { Skeleton.InvalidIndex }, new[] { Transform3D.Identity },
            -1, new[] { new StringID("Smile"), new StringID("Blink") });
        var from = new Pose(skeleton);
        from.SetToReferencePose();
        var to = new Pose(skeleton);
        to.SetToReferencePose();
        to.SetFloat(0, 1f);
        to.SetFloat(1, 1f);

        var mask = new BoneMask(skeleton, 1f);
        mask.SetChannelWeight(0, 0f);
        var result = new Pose(skeleton);
        Blender.Blend(result, from, to, 1f, mask);

        Assert.Equal(0f, result.GetFloat(0));
        Assert.Equal(1f, result.GetFloat(1));
    }
}
