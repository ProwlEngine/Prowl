using System.Collections.Generic;
using System.Linq;

namespace Prowl.Motion;

/// <summary>Which side of the body a humanoid bone is on.</summary>
internal enum BoneSide : byte { Center, Left, Right }

/// <summary>
/// Static reference data about the standard humanoid rig: which bones are required vs optional,
/// the humanoid-space parent of each bone, canonical names, and the lowercase name aliases the
/// auto-mapper matches against.
/// </summary>
public static partial class HumanTrait
{
    private static readonly HumanBodyBone[] s_all =
        (HumanBodyBone[])Enum.GetValues(typeof(HumanBodyBone));

    private static readonly HashSet<HumanBodyBone> s_required = new()
    {
        HumanBodyBone.Hips,
        HumanBodyBone.LeftUpperLeg, HumanBodyBone.RightUpperLeg,
        HumanBodyBone.LeftLowerLeg, HumanBodyBone.RightLowerLeg,
        HumanBodyBone.LeftFoot, HumanBodyBone.RightFoot,
        HumanBodyBone.Spine,
        HumanBodyBone.Head,
        HumanBodyBone.LeftUpperArm, HumanBodyBone.RightUpperArm,
        HumanBodyBone.LeftLowerArm, HumanBodyBone.RightLowerArm,
        HumanBodyBone.LeftHand, HumanBodyBone.RightHand,
    };

    // Lowercase, side-stripped name fragments matched against normalized bone names.
    private static readonly Dictionary<HumanBodyBone, string[]> s_aliases = new()
    {
        [HumanBodyBone.Hips] = new[] { "hips", "pelvis" },
        [HumanBodyBone.Spine] = new[] { "spine", "spine0", "spine01" },
        [HumanBodyBone.Chest] = new[] { "chest", "spine1", "spine02" },
        [HumanBodyBone.UpperChest] = new[] { "upperchest", "spine2", "spine03" },
        [HumanBodyBone.Neck] = new[] { "neck", "neck01" },
        [HumanBodyBone.Head] = new[] { "head" },
        [HumanBodyBone.Jaw] = new[] { "jaw" },
        [HumanBodyBone.LeftEye] = new[] { "eye" },
        [HumanBodyBone.RightEye] = new[] { "eye" },
        [HumanBodyBone.LeftShoulder] = new[] { "shoulder", "clavicle", "collar" },
        [HumanBodyBone.RightShoulder] = new[] { "shoulder", "clavicle", "collar" },
        [HumanBodyBone.LeftUpperArm] = new[] { "upperarm", "arm" },
        [HumanBodyBone.RightUpperArm] = new[] { "upperarm", "arm" },
        [HumanBodyBone.LeftLowerArm] = new[] { "lowerarm", "forearm", "elbow" },
        [HumanBodyBone.RightLowerArm] = new[] { "lowerarm", "forearm", "elbow" },
        [HumanBodyBone.LeftHand] = new[] { "hand", "wrist" },
        [HumanBodyBone.RightHand] = new[] { "hand", "wrist" },
        [HumanBodyBone.LeftUpperLeg] = new[] { "upperleg", "upleg", "thigh" },
        [HumanBodyBone.RightUpperLeg] = new[] { "upperleg", "upleg", "thigh" },
        [HumanBodyBone.LeftLowerLeg] = new[] { "lowerleg", "leg", "calf", "shin", "knee" },
        [HumanBodyBone.RightLowerLeg] = new[] { "lowerleg", "leg", "calf", "shin", "knee" },
        [HumanBodyBone.LeftFoot] = new[] { "foot", "ankle" },
        [HumanBodyBone.RightFoot] = new[] { "foot", "ankle" },
        [HumanBodyBone.LeftToes] = new[] { "toes", "toe", "toebase", "ball", "toe0" },
        [HumanBodyBone.RightToes] = new[] { "toes", "toe", "toebase", "ball", "toe0" },
    };

    static HumanTrait()
    {
        AddFingerAliases();
        s_muscles = BuildMuscles();
        s_specs = BuildBoneSpecs();
    }

    // Finger aliases for the common conventions: canonical ("thumbproximal"), hand prefixed
    // ("handthumb1"), numbered ("thumb01", "thumb1"), numbered biped ("finger0", "finger01",
    // "finger02" for the thumb through "finger4" for the little finger), and "pinky" for the little finger.
    private static void AddFingerAliases()
    {
        string[] sides = { "Left", "Right" };
        string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        (string Suffix, int Num)[] phalanges = { ("Proximal", 1), ("Intermediate", 2), ("Distal", 3) };

        foreach (string side in sides)
        for (int fingerIndex = 0; fingerIndex < fingers.Length; fingerIndex++)
        foreach ((string suffix, int num) in phalanges)
        {
            string finger = fingers[fingerIndex];
            if (!Enum.TryParse(side + finger + suffix, out HumanBodyBone bone))
                continue;

            string f = finger.ToLowerInvariant();
            string s = suffix.ToLowerInvariant();
            string biped = "finger" + fingerIndex + (num == 1 ? string.Empty : (num - 1).ToString());
            var aliases = new List<string> { f + s, "hand" + f + num, f + num, f + "0" + num, biped };

            if (finger == "Little")
            {
                aliases.Add("pinky" + s);
                aliases.Add("handpinky" + num);
                aliases.Add("pinky" + num);
                aliases.Add("pinky0" + num);
            }

            s_aliases[bone] = aliases.ToArray();
        }
    }

    /// <summary>Total number of <see cref="HumanBodyBone"/> values.</summary>
    public static int BoneCount => s_all.Length;

    /// <summary>Number of bones that must be present for a rig to qualify as humanoid.</summary>
    public static int RequiredBoneCount => s_required.Count;

    /// <summary>
    /// True if the bone must be present for a humanoid avatar. Required: hips, upper/lower legs,
    /// feet, spine, head, upper/lower arms, hands. Optional: chest, upper chest, neck, shoulders,
    /// toes, eyes, jaw.
    /// </summary>
    public static bool IsRequired(HumanBodyBone bone) => s_required.Contains(bone);

    public static bool IsOptional(HumanBodyBone bone) => !s_required.Contains(bone);

    /// <summary>The bone's parent in humanoid space, or null for the hips (the humanoid root).</summary>
    public static HumanBodyBone? GetParentBone(HumanBodyBone bone)
    {
        // Finger phalanges: Distal -> Intermediate -> Proximal -> Hand.
        string name = bone.ToString();
        if (name.EndsWith("Distal", StringComparison.Ordinal))
            return ParseBone(name[..^6] + "Intermediate");
        if (name.EndsWith("Intermediate", StringComparison.Ordinal))
            return ParseBone(name[..^12] + "Proximal");
        if (name.EndsWith("Proximal", StringComparison.Ordinal))
            return name.StartsWith("Left", StringComparison.Ordinal) ? HumanBodyBone.LeftHand : HumanBodyBone.RightHand;

        return GetBodyParentBone(bone);
    }

    private static HumanBodyBone? ParseBone(string name) => Enum.TryParse(name, out HumanBodyBone bone) ? bone : null;

    private static HumanBodyBone? GetBodyParentBone(HumanBodyBone bone) => bone switch
    {
        HumanBodyBone.Hips => null,
        HumanBodyBone.Spine => HumanBodyBone.Hips,
        HumanBodyBone.Chest => HumanBodyBone.Spine,
        HumanBodyBone.UpperChest => HumanBodyBone.Chest,
        HumanBodyBone.Neck => HumanBodyBone.UpperChest,
        HumanBodyBone.Head => HumanBodyBone.Neck,
        HumanBodyBone.LeftEye or HumanBodyBone.RightEye or HumanBodyBone.Jaw => HumanBodyBone.Head,
        HumanBodyBone.LeftShoulder or HumanBodyBone.RightShoulder => HumanBodyBone.UpperChest,
        HumanBodyBone.LeftUpperArm => HumanBodyBone.LeftShoulder,
        HumanBodyBone.RightUpperArm => HumanBodyBone.RightShoulder,
        HumanBodyBone.LeftLowerArm => HumanBodyBone.LeftUpperArm,
        HumanBodyBone.RightLowerArm => HumanBodyBone.RightUpperArm,
        HumanBodyBone.LeftHand => HumanBodyBone.LeftLowerArm,
        HumanBodyBone.RightHand => HumanBodyBone.RightLowerArm,
        HumanBodyBone.LeftUpperLeg or HumanBodyBone.RightUpperLeg => HumanBodyBone.Hips,
        HumanBodyBone.LeftLowerLeg => HumanBodyBone.LeftUpperLeg,
        HumanBodyBone.RightLowerLeg => HumanBodyBone.RightUpperLeg,
        HumanBodyBone.LeftFoot => HumanBodyBone.LeftLowerLeg,
        HumanBodyBone.RightFoot => HumanBodyBone.RightLowerLeg,
        HumanBodyBone.LeftToes => HumanBodyBone.LeftFoot,
        HumanBodyBone.RightToes => HumanBodyBone.RightFoot,
        _ => null
    };

    /// <summary>The canonical name of a humanoid bone (e.g. "Hips").</summary>
    public static string GetBoneName(HumanBodyBone bone) => bone.ToString();

    /// <summary>
    /// Lowercase name fragments used to recognise this bone on an arbitrary rig (e.g. for the hips:
    /// "hips", "pelvis"). Used by <see cref="HumanoidAutoMapper"/> after prefix/side normalization.
    /// </summary>
    public static IReadOnlyList<string> GetNameAliases(HumanBodyBone bone)
        => s_aliases.TryGetValue(bone, out var aliases) ? aliases : Array.Empty<string>();

    /// <summary>The (upper, mid, end) bone chain solved for a humanoid IK goal.</summary>
    public static (HumanBodyBone Upper, HumanBodyBone Mid, HumanBodyBone End) GetGoalChain(HumanGoal goal) => goal switch
    {
        HumanGoal.LeftFoot => (HumanBodyBone.LeftUpperLeg, HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftFoot),
        HumanGoal.RightFoot => (HumanBodyBone.RightUpperLeg, HumanBodyBone.RightLowerLeg, HumanBodyBone.RightFoot),
        HumanGoal.LeftHand => (HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftHand),
        HumanGoal.RightHand => (HumanBodyBone.RightUpperArm, HumanBodyBone.RightLowerArm, HumanBodyBone.RightHand),
        _ => throw new ArgumentOutOfRangeException(nameof(goal))
    };

    /// <summary>The end-effector bone for a humanoid IK goal.</summary>
    public static HumanBodyBone GetGoalEndBone(HumanGoal goal) => GetGoalChain(goal).End;

    /// <summary>All humanoid bones, in enum order.</summary>
    internal static IReadOnlyList<HumanBodyBone> AllBones => s_all;

    /// <summary>Whether a bone is left, right, or centre (inferred from its name).</summary>
    internal static BoneSide GetSide(HumanBodyBone bone)
    {
        string name = bone.ToString();
        if (name.StartsWith("Left", StringComparison.Ordinal)) return BoneSide.Left;
        if (name.StartsWith("Right", StringComparison.Ordinal)) return BoneSide.Right;
        return BoneSide.Center;
    }
}
