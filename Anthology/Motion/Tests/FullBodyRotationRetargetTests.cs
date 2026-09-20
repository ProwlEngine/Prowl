using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>
/// Whole-body rotations (flips, handstands) exceed the hips muscle's small range, so they must travel
/// through the root transform's orientation. A source posed fully upside-down should reconstruct
/// upside-down on the target (head below hips), not be clamped upright.
/// </summary>
public class FullBodyRotationRetargetTests
{
    [Fact]
    public void UpsideDownSource_RetargetsUpsideDown()
    {
        Avatar source = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid());
        Avatar target = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid());

        int srcHips = source.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        int tgtHips = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        int tgtHead = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);

        // Flip the whole body upside-down by rotating the (root) hips 180 degrees about X.
        var src = new Pose(source.Skeleton);
        src.SetToReferencePose();
        Transform3D hipsLocal = src.GetTransform(srcHips);
        src.SetTransform(srcHips, new Transform3D(hipsLocal.position, hipsLocal.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), MathF.PI), hipsLocal.scale));
        src.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(source, src, human);
        var result = new Pose(target.Skeleton);
        Retargeter.RetargetTo(target, human, result);
        result.CalculateModelSpaceTransforms();

        float headY = result.GetModelSpaceTransform(tgtHead).position.Y;
        float hipsY = result.GetModelSpaceTransform(tgtHips).position.Y;
        Assert.True(headY < hipsY, $"body did not invert: head Y {headY} should be below hips Y {hipsY}");
    }
}
