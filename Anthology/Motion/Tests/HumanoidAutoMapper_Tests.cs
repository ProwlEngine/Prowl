using Prowl.Clay.Importer;
using Prowl.Vector.Spatial;
using Prowl.Vector;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

/// <summary>Mapping a skeleton to the human body from its bone names and shape.</summary>
public class HumanoidAutoMapper_Tests
{
    private static Skeleton Load(string path) => ClayModelLoader.BuildSkeleton(ModelImporter.Load(path));

    private static Skeleton LoadRumba() => ClayModelLoader.BuildSkeleton(ModelImporter.Load(TestAssets.RumbaDancing));

    private static Skeleton Named(params (string Name, int Parent)[] bones)
    {
        var ids = bones.Select(b => new StringID(b.Name)).ToArray();
        var parents = bones.Select(b => b.Parent).ToArray();
        var pose = bones.Select(_ => new Transform3D(Float3.Zero, Quaternion.Identity, Float3.One)).ToArray();
        return new Skeleton(ids, parents, pose);
    }

    private static Skeleton PrefixedRig(string prefix) => Named(
        (prefix + "Hips", -1), (prefix + "Spine", 0), (prefix + "Head", 1),
        (prefix + "LeftArm", 1), (prefix + "LeftForeArm", 3), (prefix + "LeftHand", 4),
        (prefix + "RightArm", 1), (prefix + "RightForeArm", 6), (prefix + "RightHand", 7),
        (prefix + "LeftUpLeg", 0), (prefix + "LeftLeg", 9), (prefix + "LeftFoot", 10),
        (prefix + "RightUpLeg", 0), (prefix + "RightLeg", 12), (prefix + "RightFoot", 13));

    private static Skeleton BuildFromNames((HumanBodyBone Bone, HumanBodyBone? Parent, string Name)[] defs)
    {
        var indexOf = new Dictionary<HumanBodyBone, int>();
        for (int i = 0; i < defs.Length; i++)
            indexOf[defs[i].Bone] = i;

        var ids = new StringID[defs.Length];
        var parents = new int[defs.Length];
        var pose = new Transform3D[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            ids[i] = new StringID(defs[i].Name);
            parents[i] = defs[i].Parent is { } p ? indexOf[p] : Skeleton.InvalidIndex;
            // Rough upright placement so leg-length / hierarchy checks are sane.
            pose[i] = new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One);
        }
        return new Skeleton(ids, parents, pose);
    }

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

    [Fact]
    public void PrefixNaming_MapsHumanoid()
        => Assert.True(HumanoidAutoMapper.IsLikelyHumanoid(TestSkeletons.MakeMinimalHumanoid(TestSkeletons.SideNaming.Prefix)));

    [Fact]
    public void UnderscoreSuffixNaming_MapsHumanoid()
        => Assert.True(HumanoidAutoMapper.IsLikelyHumanoid(TestSkeletons.MakeMinimalHumanoid(TestSkeletons.SideNaming.SuffixUnderscore)));

    [Fact]
    public void BrokenArmHierarchy_IsRejectedDespiteCompleteNames()
    {
        // All required bone names are present, but the left hand is parented under the hips, so the
        // arm chain is not a real hierarchy. Topology validation must reject it.
        var skeleton = TestSkeletons.MakeMinimalHumanoid(TestSkeletons.SideNaming.Prefix, breakArmHierarchy: true);
        Assert.False(HumanoidAutoMapper.IsLikelyHumanoid(skeleton));
    }

    [Fact]
    public void UnderscoreSuffix_AssignsHandsCorrectly()
    {
        Assert.True(HumanoidAutoMapper.TryMap(TestSkeletons.MakeMinimalHumanoid(TestSkeletons.SideNaming.SuffixUnderscore), out var description));
        Assert.True(description.HasBone(HumanBodyBone.LeftHand));
        Assert.True(description.HasBone(HumanBodyBone.RightHand));
    }

    [ModelFact]
    public void MixamoModel_ScaleIsPositive()
    {
        var avatar = AvatarBuilder.BuildAutomatic(ClayModelLoader.LoadHumanoidSkeleton());
        Assert.True(avatar.Humanoid!.Scale > 0f);
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

    [Fact]
    public void SimpleChain_IsNotHumanoid()
        => Assert.False(HumanoidAutoMapper.IsLikelyHumanoid(TestSkeletons.MakeChain()));

    [Fact]
    public void SimpleChain_TryMap_Fails()
        => Assert.False(HumanoidAutoMapper.TryMap(TestSkeletons.MakeChain(), out _));

    [ModelFact]
    public void MixamoModel_IsHumanoid()
        => Assert.True(HumanoidAutoMapper.IsLikelyHumanoid(ClayModelLoader.LoadHumanoidSkeleton()));

    [ModelFact]
    public void MixamoModel_TryMap_Succeeds()
        => Assert.True(HumanoidAutoMapper.TryMap(ClayModelLoader.LoadHumanoidSkeleton(), out _));

    [ModelFact]
    public void MixamoModel_MapsHipsAndHead()
    {
        Assert.True(HumanoidAutoMapper.TryMap(ClayModelLoader.LoadHumanoidSkeleton(), out var description));
        Assert.True(description.HasBone(HumanBodyBone.Hips));
        Assert.True(description.HasBone(HumanBodyBone.Head));
    }

    [ModelFact]
    public void MixamoModel_MapsAllRequiredBones()
    {
        Assert.True(HumanoidAutoMapper.TryMap(ClayModelLoader.LoadHumanoidSkeleton(), out var description));
        Assert.True(description.HasAllRequiredBones);
    }

    [Theory]
    [InlineData("Character1_")]
    [InlineData("mixamorig_")]
    public void AutoMapper_PeelsCamelCaseSideAfterAPrefixToken(string prefix)
    {
        HumanoidMapResult result = HumanoidAutoMapper.Map(PrefixedRig(prefix));
        Assert.True(result.IsHumanoid);
        Assert.Equal(9, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg));
        Assert.Equal(12, result.Description.GetSkeletonBoneIndex(HumanBodyBone.RightUpperLeg));
    }

    [Fact]
    public void AutoMapper_MapsUnrealMiddleFinger()
    {
        Skeleton skeleton = Named(
            ("pelvis", -1), ("spine_01", 0), ("head", 1),
            ("upperarm_l", 1), ("lowerarm_l", 3), ("hand_l", 4),
            ("upperarm_r", 1), ("lowerarm_r", 6), ("hand_r", 7),
            ("thigh_l", 0), ("calf_l", 9), ("foot_l", 10),
            ("thigh_r", 0), ("calf_r", 12), ("foot_r", 13),
            ("middle_01_l", 5), ("middle_02_l", 15), ("middle_03_l", 16));

        HumanoidMapResult result = HumanoidAutoMapper.Map(skeleton);

        Assert.True(result.IsHumanoid);
        Assert.Equal(15, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftMiddleProximal));
        Assert.Equal(17, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftMiddleDistal));
    }

    [Fact]
    public void UnmappedBones_ReportsRequiredBoneBehindMissingOptionalParent()
    {
        Skeleton skeleton = Named(
            ("Hips", -1), ("Spine", 0), ("Head", 1),
            ("Bone_A", 1), ("LeftForeArm", 3), ("LeftHand", 4),
            ("RightArm", 1), ("RightForeArm", 6), ("RightHand", 7),
            ("LeftUpLeg", 0), ("LeftLeg", 9), ("LeftFoot", 10),
            ("RightUpLeg", 0), ("RightLeg", 12), ("RightFoot", 13));

        HumanoidMapResult result = HumanoidAutoMapper.Map(skeleton);

        Assert.False(result.IsHumanoid);
        Assert.Contains(HumanBodyBone.LeftUpperArm, result.UnmappedBones);
    }

    [Fact]
    public void AutoMapper_MapsBipedToesAndFingers()
    {
        Skeleton skeleton = Named(
            ("Bip001 Pelvis", -1), ("Bip001 Spine", 0), ("Bip001 Head", 1),
            ("Bip001 L UpperArm", 1), ("Bip001 L Forearm", 3), ("Bip001 L Hand", 4),
            ("Bip001 R UpperArm", 1), ("Bip001 R Forearm", 6), ("Bip001 R Hand", 7),
            ("Bip001 L Thigh", 0), ("Bip001 L Calf", 9), ("Bip001 L Foot", 10),
            ("Bip001 R Thigh", 0), ("Bip001 R Calf", 12), ("Bip001 R Foot", 13),
            ("Bip001 L Toe0", 11),
            ("Bip001 L Finger0", 5), ("Bip001 L Finger01", 16), ("Bip001 L Finger02", 17),
            ("Bip001 L Finger1", 5), ("Bip001 L Finger11", 19),
            ("Bip001 L Finger4", 5));

        HumanoidMapResult result = HumanoidAutoMapper.Map(skeleton);

        Assert.True(result.IsHumanoid);
        Assert.Equal(15, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftToes));
        Assert.Equal(16, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftThumbProximal));
        Assert.Equal(17, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftThumbIntermediate));
        Assert.Equal(18, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftThumbDistal));
        Assert.Equal(19, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexProximal));
        Assert.Equal(20, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexIntermediate));
        Assert.Equal(21, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftLittleProximal));
    }

    [Fact]
    public void MixamoWithExporterIndexSuffix_MapsHumanoid()
    {
        var defs = new (HumanBodyBone, HumanBodyBone?, string)[]
        {
            (HumanBodyBone.Hips, null, "mixamorig:Hips_64"),
            (HumanBodyBone.Spine, HumanBodyBone.Hips, "mixamorig:Spine_53"),
            (HumanBodyBone.Chest, HumanBodyBone.Spine, "mixamorig:Spine1_52"),
            (HumanBodyBone.Neck, HumanBodyBone.Chest, "mixamorig:Neck_2"),
            (HumanBodyBone.Head, HumanBodyBone.Neck, "mixamorig:Head_1"),
            (HumanBodyBone.LeftUpperArm, HumanBodyBone.Chest, "mixamorig:LeftArm_25"),
            (HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftUpperArm, "mixamorig:LeftForeArm_24"),
            (HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm, "mixamorig:LeftHand_23"),
            (HumanBodyBone.RightUpperArm, HumanBodyBone.Chest, "mixamorig:RightArm_49"),
            (HumanBodyBone.RightLowerArm, HumanBodyBone.RightUpperArm, "mixamorig:RightForeArm_48"),
            (HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm, "mixamorig:RightHand_47"),
            (HumanBodyBone.LeftUpperLeg, HumanBodyBone.Hips, "mixamorig:LeftUpLeg_58"),
            (HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftUpperLeg, "mixamorig:LeftLeg_57"),
            (HumanBodyBone.LeftFoot, HumanBodyBone.LeftLowerLeg, "mixamorig:LeftFoot_56"),
            (HumanBodyBone.RightUpperLeg, HumanBodyBone.Hips, "mixamorig:RightUpLeg_63"),
            (HumanBodyBone.RightLowerLeg, HumanBodyBone.RightUpperLeg, "mixamorig:RightLeg_62"),
            (HumanBodyBone.RightFoot, HumanBodyBone.RightLowerLeg, "mixamorig:RightFoot_61"),
        };
        HumanoidMapResult result = HumanoidAutoMapper.Map(BuildFromNames(defs));
        Assert.True(result.IsHumanoid);
        Assert.True(result.Description.HasBone(HumanBodyBone.Hips));
        Assert.True(result.Description.HasBone(HumanBodyBone.Chest)); // Spine1 -> Chest
    }

    [Fact]
    public void NeckAndHeadUnderRoot_NotSpine_StillMapsHumanoid()
    {
        // Some rigs (e.g. the Fortnite "emmy" export) parent the neck/head off the root control node
        // rather than under the spine. The bones are correctly named and the sub-chain is intact; it
        // should still detect as humanoid (the neck is just on a separate branch from the spine).
        var names = new[]
        {
            "root",                                   // 0 - non-humanoid control root
            "pelvis_88",                              // 1
            "spine_01_68",                            // 2
            "clavicle_l_30", "upperarm_l_29", "lowerarm_l_26", "hand_l_23",   // 3-6
            "clavicle_r_61", "upperarm_r_60", "lowerarm_r_57", "hand_r_54",   // 7-10
            "thigh_l_75", "calf_l_73", "foot_l_72",   // 11-13
            "thigh_r_82", "calf_r_80", "foot_r_79",   // 14-16
            "neck_01_127", "head_125",                // 17-18  (parented under root, not spine)
        };
        var parents = new[]
        {
            -1, 0, 1,
            2, 3, 4, 5,
            2, 7, 8, 9,
            1, 11, 12,
            1, 14, 15,
            0, 17, // neck under root (0), head under neck
        };

        var ids = new StringID[names.Length];
        var pose = new Transform3D[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            ids[i] = new StringID(names[i]);
            pose[i] = new Transform3D(new Float3(0f, i * 0.1f, 0f), Quaternion.Identity, Float3.One);
        }

        HumanoidMapResult result = HumanoidAutoMapper.Map(new Skeleton(ids, parents, pose));
        Assert.True(result.IsHumanoid);
        Assert.True(result.Description.HasBone(HumanBodyBone.Head));
        Assert.True(result.Description.HasBone(HumanBodyBone.Neck));
    }

    [Fact]
    public void BipedPrefixedWithMidSideLetter_MapsHumanoid()
    {
        var defs = new (HumanBodyBone, HumanBodyBone?, string)[]
        {
            (HumanBodyBone.Hips, null, "Bip001 Pelvis_01"),
            (HumanBodyBone.Spine, HumanBodyBone.Hips, "Bip001 Spine_02"),
            (HumanBodyBone.Chest, HumanBodyBone.Spine, "Bip001 Spine1_03"),
            (HumanBodyBone.Neck, HumanBodyBone.Chest, "Bip001 Neck_05"),
            (HumanBodyBone.Head, HumanBodyBone.Neck, "Bip001 Head_06"),
            (HumanBodyBone.LeftShoulder, HumanBodyBone.Chest, "Bip001 L Clavicle_038"),
            (HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftShoulder, "Bip001 L UpperArm_039"),
            (HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftUpperArm, "Bip001 L Forearm_040"),
            (HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm, "Bip001 L Hand_041"),
            (HumanBodyBone.RightShoulder, HumanBodyBone.Chest, "Bip001 R Clavicle_066"),
            (HumanBodyBone.RightUpperArm, HumanBodyBone.RightShoulder, "Bip001 R UpperArm_067"),
            (HumanBodyBone.RightLowerArm, HumanBodyBone.RightUpperArm, "Bip001 R Forearm_068"),
            (HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm, "Bip001 R Hand_069"),
            (HumanBodyBone.LeftUpperLeg, HumanBodyBone.Hips, "Bip001 L Thigh_096"),
            (HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftUpperLeg, "Bip001 L Calf_097"),
            (HumanBodyBone.LeftFoot, HumanBodyBone.LeftLowerLeg, "Bip001 L Foot_098"),
            (HumanBodyBone.RightUpperLeg, HumanBodyBone.Hips, "Bip001 R Thigh_0106"),
            (HumanBodyBone.RightLowerLeg, HumanBodyBone.RightUpperLeg, "Bip001 R Calf_0107"),
            (HumanBodyBone.RightFoot, HumanBodyBone.RightLowerLeg, "Bip001 R Foot_0108"),
        };
        HumanoidMapResult result = HumanoidAutoMapper.Map(BuildFromNames(defs));
        Assert.True(result.IsHumanoid);
        Assert.True(result.Description.HasBone(HumanBodyBone.Hips));   // "Bip001 Pelvis_01"
        Assert.True(result.Description.HasBone(HumanBodyBone.LeftUpperArm)); // mid-name "L"
    }
}
