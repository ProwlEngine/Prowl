using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class MirrorTests
{
    [Fact]
    public void Mirror_SwapsLeftPoseOntoTheRightSide()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var rig = avatar.Humanoid!;

        // Raise the LEFT arm by rotating the left upper arm.
        var source = new Pose(skeleton);
        source.SetToReferencePose();
        int leftUpper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        Transform3D bind = skeleton.GetBoneParentSpaceTransform(leftUpper);
        source.SetTransform(leftUpper, new Transform3D(bind.position, bind.rotation * Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.6f), bind.scale));
        source.CalculateModelSpaceTransforms();

        var mirrored = new Pose(skeleton);
        PoseMirror.Apply(avatar, source, mirrored);
        mirrored.CalculateModelSpaceTransforms();

        int leftHand = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftHand);
        int rightHand = rig.GetSkeletonBoneIndex(HumanBodyBone.RightHand);

        Float3 sourceLeftHand = source.GetModelSpaceTransform(leftHand).position;
        Float3 mirroredRightHand = mirrored.GetModelSpaceTransform(rightHand).position;

        // The mirrored right hand should sit at the source left hand's position with X negated.
        Assert.Equal((double)(-sourceLeftHand.X), (double)mirroredRightHand.X, 2);
        Assert.Equal((double)sourceLeftHand.Y, (double)mirroredRightHand.Y, 2);
        Assert.Equal((double)sourceLeftHand.Z, (double)mirroredRightHand.Z, 2);
    }

    [Fact]
    public void Mirror_OfRestPose_IsStillRest()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);

        var source = new Pose(skeleton);
        source.SetToReferencePose();
        source.CalculateModelSpaceTransforms();

        var mirrored = new Pose(skeleton);
        PoseMirror.Apply(avatar, source, mirrored);
        mirrored.CalculateModelSpaceTransforms();

        int rightHand = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.RightHand);
        Float3 a = source.GetModelSpaceTransform(rightHand).position;
        Float3 b = mirrored.GetModelSpaceTransform(rightHand).position;
        Assert.True(Float3.Distance(a, b) < 1e-2f, $"rest mirror moved the right hand: {a} -> {b}");
    }
}
