using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>
/// Naming conventions seen in real exported GLBs: Mixamo bones with an exporter-appended "_index"
/// suffix (s.w.a.t. operator) and 3ds Max "Biped" bones with a "Bip001 " prefix and a mid-name side
/// letter (ZZZ models). Both must still auto-map to a humanoid.
/// </summary>
public class RigNamingTests
{
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
