using Prowl.Clay.Importer;

namespace Prowl.Motion.Tests;

/// <summary>
/// Real-world rigs with non-Mixamo naming: an Unreal Engine skeleton (numbered spine, _l/_r suffix)
/// and a Maya rig with cn_/lf_/rt_ prefixes and twist-decomposed limbs.
/// </summary>
public class AdvancedRigTests
{
    private static Skeleton Load(string path) => ClayModelLoader.BuildSkeleton(ModelImporter.Load(path));

    [ModelFact]
    public void UnrealSkeleton_IsDetectedHumanoid()
    {
        Avatar avatar = AvatarBuilder.BuildAutomatic(Load(TestAssets.Daredevil));
        Assert.True(avatar.IsHuman);
    }

    [ModelFact]
    public void UnrealSkeleton_MapsNumberedSpineAndSuffixSides()
    {
        var result = HumanoidAutoMapper.Map(Load(TestAssets.Daredevil));
        Assert.True(result.IsHumanoid);
        Assert.True(result.Description.HasBone(HumanBodyBone.Spine));   // spine_01
        Assert.True(result.Description.HasBone(HumanBodyBone.LeftUpperLeg));  // thigh_l
        Assert.True(result.Description.HasBone(HumanBodyBone.RightHand));     // hand_r
    }

    [ModelFact]
    public void MayaPrefixRig_MapsCenterAndSideTorsoBones()
    {
        // cn_/lf_/rt_ prefixes should be understood even though the limbs cannot be mapped.
        var result = HumanoidAutoMapper.Map(Load(TestAssets.Delbin));
        Assert.True(result.Description.HasBone(HumanBodyBone.Hips));  // cn_pelvis
        Assert.True(result.Description.HasBone(HumanBodyBone.Spine)); // cn_spine_01
        Assert.True(result.Description.HasBone(HumanBodyBone.Head));  // cn_head
        Assert.True(result.Description.HasBone(HumanBodyBone.LeftFoot));  // lf_foot
    }

    [ModelFact]
    public void TwistLimbRig_IsNotHumanoid_ButWarnsTheRightBones()
    {
        var result = HumanoidAutoMapper.Map(Load(TestAssets.Delbin));

        // The twist-decomposed limbs (lf_shoulder_twist_01, lf_hip_twist_01, ...) cannot be mapped.
        Assert.False(result.IsHumanoid);

        // The warning frontier reports the topmost missing bone in each chain...
        Assert.Contains(HumanBodyBone.LeftUpperArm, result.UnmappedBones);
        Assert.Contains(HumanBodyBone.LeftUpperLeg, result.UnmappedBones);

        // ...and NOT their descendants (non-recursive): the lower arm/leg are suppressed because
        // their parent is already reported.
        Assert.DoesNotContain(HumanBodyBone.LeftLowerArm, result.UnmappedBones);
        Assert.DoesNotContain(HumanBodyBone.LeftLowerLeg, result.UnmappedBones);
    }

    [ModelFact]
    public void Avatar_CarriesMappingReportWithWarnings()
    {
        Avatar avatar = AvatarBuilder.BuildAutomatic(Load(TestAssets.Delbin));
        Assert.NotNull(avatar.MappingReport);
        Assert.False(avatar.IsHuman);
        Assert.NotEmpty(avatar.MappingReport!.UnmappedBones);
    }
}
