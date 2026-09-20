using Prowl.Clay.Importer;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class FingerTests
{
    private static Skeleton LoadRumba() => ClayModelLoader.BuildSkeleton(ModelImporter.Load(TestAssets.RumbaDancing));

    [Fact]
    public void Fingers_AreInTheHumanoidSet()
    {
        Assert.True(HumanTrait.IsOptional(HumanBodyBone.LeftThumbProximal));
        Assert.Equal(HumanBodyBone.LeftHand, HumanTrait.GetParentBone(HumanBodyBone.LeftThumbProximal));
        Assert.Equal(HumanBodyBone.LeftThumbProximal, HumanTrait.GetParentBone(HumanBodyBone.LeftThumbIntermediate));
        Assert.Equal(HumanBodyBone.LeftThumbIntermediate, HumanTrait.GetParentBone(HumanBodyBone.LeftThumbDistal));
    }

    [ModelFact]
    public void MixamoFingers_AreAutoMapped()
    {
        var result = HumanoidAutoMapper.Map(LoadRumba());
        Assert.True(result.IsHumanoid);
        // Mixamo names: LeftHandThumb1/2/3, LeftHandIndex1.. , LeftHandPinky1.. (-> Little).
        Assert.True(result.Description.HasBone(HumanBodyBone.LeftThumbProximal));
        Assert.True(result.Description.HasBone(HumanBodyBone.LeftIndexDistal));
        Assert.True(result.Description.HasBone(HumanBodyBone.RightLittleProximal)); // from "Pinky"
    }

    [ModelFact]
    public void RetargetRoundTrip_ReproducesAFingerRotation()
    {
        Skeleton skeleton = LoadRumba();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        int index = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexProximal);
        Assert.NotEqual(Skeleton.InvalidIndex, index);

        var source = new Pose(skeleton);
        source.SetToReferencePose();
        Transform3D bind = skeleton.GetBoneParentSpaceTransform(index);
        source.SetTransform(index, new Transform3D(bind.position, bind.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f), bind.scale));

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, source, human);
        var result = new Pose(skeleton);
        Retargeter.RetargetTo(avatar, human, result);

        float dot = MathF.Abs(Quaternion.Dot(source.GetTransform(index).rotation, result.GetTransform(index).rotation));
        Assert.True(dot > 0.999f);
    }

    [Fact]
    public void NoFingerRig_WarnsProximalsButNotDeeperPhalanges()
    {
        // A humanoid with hands but no finger bones: report the proximal (parent hand is mapped),
        // but NOT the intermediate/distal below it (non-recursive).
        var result = HumanoidAutoMapper.Map(TestSkeletons.MakeProportionedHumanoid());

        Assert.True(result.IsHumanoid); // fingers are optional
        Assert.Contains(HumanBodyBone.LeftThumbProximal, result.UnmappedBones);
        Assert.DoesNotContain(HumanBodyBone.LeftThumbIntermediate, result.UnmappedBones);
        Assert.DoesNotContain(HumanBodyBone.LeftThumbDistal, result.UnmappedBones);
    }
}
