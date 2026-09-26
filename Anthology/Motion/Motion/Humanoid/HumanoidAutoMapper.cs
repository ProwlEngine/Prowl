using System.Collections.Generic;
using System.Text;
using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>The outcome of a humanoid auto mapping: the mapping, whether it is humanoid, and the bones it could not find.</summary>
public sealed class HumanoidMapResult
{
    public required HumanDescription Description { get; init; }
    public required bool HasAllRequiredBones { get; init; }
    public required bool HierarchyValid { get; init; }

    /// <summary>True if this skeleton is a usable humanoid (all required bones found, valid hierarchy).</summary>
    public bool IsHumanoid => HasAllRequiredBones && HierarchyValid;

    /// <summary>True if the rest pose looks like a T pose. Other rests still map but retarget with an offset.</summary>
    public required bool IsRestPoseTPose { get; init; }

    /// <summary>The topmost unmapped bone of each missing chain, so a missing hand does not also list its fingers.</summary>
    public required IReadOnlyList<HumanBodyBone> UnmappedBones { get; init; }
}

/// <summary>
/// Recognises a humanoid rig in a skeleton: bone names are normalized, split into a side and a name,
/// matched against <see cref="HumanTrait.GetNameAliases"/>, then the topology is validated.
/// </summary>
public static class HumanoidAutoMapper
{
    private static readonly HashSet<string> s_leftTokens = new() { "left", "l", "lf", "lft", "lt" };
    private static readonly HashSet<string> s_rightTokens = new() { "right", "r", "rt", "rgt" };
    private static readonly HashSet<string> s_centerTokens = new() { "center", "centre", "cn", "ctr", "c", "m" };
    private static readonly char[] s_separators = { '_', '.', '-', ' ', '|', ':' };

    /// <summary>Cheap check: does this skeleton resolve to a full, topologically valid humanoid?</summary>
    public static bool IsLikelyHumanoid(Skeleton skeleton) => Map(skeleton).IsHumanoid;

    /// <summary>
    /// Attempts to build a humanoid mapping for the skeleton. Returns true and a populated
    /// <see cref="HumanDescription"/> if the rig is a humanoid; otherwise false (the description still
    /// holds any partial mapping found).
    /// </summary>
    public static bool TryMap(Skeleton skeleton, out HumanDescription description)
    {
        HumanoidMapResult result = Map(skeleton);
        description = result.Description;
        return result.IsHumanoid;
    }

    /// <summary>Maps the skeleton and returns the full result (mapping, humanoid flag, unmapped frontier).</summary>
    public static HumanoidMapResult Map(Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        var description = new HumanDescription();
        var assigned = new HashSet<HumanBodyBone>();

        int boneCount = skeleton.BoneCount;
        var info = new (BoneSide Side, List<string> Clean, string Fallback)[boneCount];
        for (int b = 0; b < boneCount; b++)
        {
            string? raw = skeleton.GetBoneID(b).DebugName;
            info[b] = string.IsNullOrEmpty(raw) ? (BoneSide.Center, new List<string>(), string.Empty) : Normalize(raw);
        }

        // Other bones prefer the hips' subtree, so look alikes elsewhere lose to the real bone.
        int hipsIndex = FindHips(info);

        bool TryAssign(BoneSide side, string candidate, int boneIndex, bool requireUnderHips)
        {
            if (candidate.Length == 0)
                return false;
            foreach (HumanBodyBone bone in HumanTrait.AllBones)
            {
                if (assigned.Contains(bone) || HumanTrait.GetSide(bone) != side)
                    continue;
                if (!ContainsExact(HumanTrait.GetNameAliases(bone), candidate))
                    continue;
                if (requireUnderHips && bone != HumanBodyBone.Hips && hipsIndex >= 0 && boneIndex != hipsIndex && !skeleton.IsChildBoneOf(hipsIndex, boneIndex))
                    continue;
                description.SetSkeletonBoneIndex(bone, boneIndex);
                assigned.Add(bone);
                return true;
            }
            return false;
        }

        var usedIndex = new bool[boneCount];
        void RunPass(bool useClean, bool requireUnderHips)
        {
            for (int b = 0; b < boneCount; b++)
            {
                if (usedIndex[b])
                    continue;
                if (useClean)
                {
                    foreach (string candidate in info[b].Clean)
                        if (TryAssign(info[b].Side, candidate, b, requireUnderHips)) { usedIndex[b] = true; break; }
                }
                else if (TryAssign(info[b].Side, info[b].Fallback, b, requireUnderHips))
                {
                    usedIndex[b] = true;
                }
            }
        }

        // Clean names (full core, then numeric-suffix-stripped) win before the prefix fallback; and
        // within each, bones under the hips win before bones on a separate branch.
        RunPass(useClean: true, requireUnderHips: true);
        RunPass(useClean: false, requireUnderHips: true);
        RunPass(useClean: true, requireUnderHips: false);
        RunPass(useClean: false, requireUnderHips: false);

        bool hasAll = description.HasAllRequiredBones;
        bool hierarchy = IsHierarchyValid(skeleton, description);
        IReadOnlyList<HumanBodyBone> unmapped = ComputeUnmappedFrontier(description);
        bool tPose = IsRestPoseTPose(skeleton, description);

        return new HumanoidMapResult
        {
            Description = description,
            HasAllRequiredBones = hasAll,
            HierarchyValid = hierarchy,
            UnmappedBones = unmapped,
            IsRestPoseTPose = tPose,
        };
    }

    // Finds the skeleton bone that maps to the hips: a clean name first across all bones, then a
    // last-token fallback (for prefixed rigs like "Bip001-Pelvis").
    private static int FindHips((BoneSide Side, List<string> Clean, string Fallback)[] info)
    {
        IReadOnlyList<string> hipsAliases = HumanTrait.GetNameAliases(HumanBodyBone.Hips);

        for (int b = 0; b < info.Length; b++)
        {
            if (info[b].Side != BoneSide.Center)
                continue;
            foreach (string candidate in info[b].Clean)
                if (ContainsExact(hipsAliases, candidate))
                    return b;
        }
        for (int b = 0; b < info.Length; b++)
        {
            if (info[b].Side == BoneSide.Center && ContainsExact(hipsAliases, info[b].Fallback))
                return b;
        }
        return -1;
    }

    // Unmapped humanoid bones whose immediate humanoid parent is mapped (or which are the root): the
    // topmost missing bone in each broken chain, so descendants are not separately reported. A missing
    // required bone is also reported when only optional bones are missing above it.
    private static List<HumanBodyBone> ComputeUnmappedFrontier(HumanDescription description)
    {
        var frontier = new List<HumanBodyBone>();
        foreach (HumanBodyBone bone in HumanTrait.AllBones)
        {
            if (description.HasBone(bone))
                continue;

            HumanBodyBone? parent = HumanTrait.GetParentBone(bone);
            if (parent is null || description.HasBone(parent.Value) || (HumanTrait.IsRequired(bone) && !HasMissingRequiredAncestor(bone, description)))
                frontier.Add(bone);
        }
        return frontier;
    }

    private static bool HasMissingRequiredAncestor(HumanBodyBone bone, HumanDescription description)
    {
        for (HumanBodyBone? parent = HumanTrait.GetParentBone(bone); parent is not null; parent = HumanTrait.GetParentBone(parent.Value))
            if (HumanTrait.IsRequired(parent.Value) && !description.HasBone(parent.Value))
                return true;
        return false;
    }

    // Heuristic: in a T-pose the arms are roughly horizontal (perpendicular to the body's up axis).
    // Measures the arm's elevation off the horizontal; an A-pose (arms angled down) exceeds the threshold.
    private static bool IsRestPoseTPose(Skeleton skeleton, HumanDescription description)
    {
        if (!description.HasBone(HumanBodyBone.Hips) || !description.HasBone(HumanBodyBone.Head) ||
            !description.HasBone(HumanBodyBone.LeftUpperArm) || !description.HasBone(HumanBodyBone.RightUpperArm))
            return true; // not enough mapped to judge; do not warn

        Float3 BindPos(HumanBodyBone bone) => skeleton.GetBoneModelSpaceTransform(description.GetSkeletonBoneIndex(bone)).position;

        Float3 up = TransformOps.SafeNormalize(Sub(BindPos(HumanBodyBone.Head), BindPos(HumanBodyBone.Hips)));
        if (Float3.Length(up) < 1e-4f)
            up = new Float3(0f, 1f, 0f);

        const float maxElevation = 0.5f; // ~30 degrees off horizontal

        bool ArmHorizontal(HumanBodyBone upperArm, HumanBodyBone hand, HumanBodyBone lowerArm)
        {
            HumanBodyBone far = description.HasBone(hand) ? hand : (description.HasBone(lowerArm) ? lowerArm : upperArm);
            if (far == upperArm)
                return true;
            Float3 dir = TransformOps.SafeNormalize(Sub(BindPos(far), BindPos(upperArm)));
            float elevation = MathF.Abs(dir.X * up.X + dir.Y * up.Y + dir.Z * up.Z);
            return elevation <= maxElevation;
        }

        return ArmHorizontal(HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm)
            && ArmHorizontal(HumanBodyBone.RightUpperArm, HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm);
    }

    private static Float3 Sub(Float3 a, Float3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static bool IsHierarchyValid(Skeleton skeleton, HumanDescription description)
    {
        // Build a skeleton-index -> humanoid-bone lookup.
        var skelToHuman = new Dictionary<int, HumanBodyBone>();
        foreach (HumanBodyBone bone in HumanTrait.AllBones)
            if (description.HasBone(bone))
                skelToHuman[description.GetSkeletonBoneIndex(bone)] = bone;

        // The first mapped ancestor of each mapped bone must be its humanoid parent, or there must be none.
        foreach (HumanBodyBone bone in HumanTrait.AllBones)
        {
            if (!description.HasBone(bone))
                continue;

            int p = skeleton.SanitizedParentIndices[description.GetSkeletonBoneIndex(bone)];
            while (p != Skeleton.InvalidIndex && !skelToHuman.ContainsKey(p))
                p = skeleton.SanitizedParentIndices[p];
            if (p == Skeleton.InvalidIndex)
                continue; // no mapped humanoid ancestor in the skeleton: separate branch, allowed

            HumanBodyBone? expected = NearestMappedAncestor(bone, description);
            if (expected is null || skelToHuman[p] != expected.Value)
                return false;
        }
        return true;
    }

    private static HumanBodyBone? NearestMappedAncestor(HumanBodyBone bone, HumanDescription description)
    {
        HumanBodyBone? parent = HumanTrait.GetParentBone(bone);
        while (parent is not null && !description.HasBone(parent.Value))
            parent = HumanTrait.GetParentBone(parent.Value);
        return parent;
    }

    private static bool ContainsExact(IReadOnlyList<string> aliases, string core)
    {
        for (int i = 0; i < aliases.Count; i++)
            if (aliases[i] == core)
                return true;
        return false;
    }

    private static (BoneSide side, List<string> clean, string fallback) Normalize(string name)
    {
        int colon = name.LastIndexOf(':');
        string s = (colon >= 0 ? name.Substring(colon + 1) : name).ToLowerInvariant();

        var tokens = new List<string>(s.Split(s_separators, StringSplitOptions.RemoveEmptyEntries));
        BoneSide side = BoneSide.Center;
        var empty = new List<string>();
        if (tokens.Count == 0)
            return (side, empty, string.Empty);

        // Remove a delimited side token from anywhere (prefix, suffix, or middle - e.g. "thigh_l",
        // "lf_thigh", "Bip001 L UpperArm").
        for (int i = 0; i < tokens.Count; i++)
        {
            if (TryClassifyToken(tokens[i], out BoneSide s2))
            {
                side = s2;
                tokens.RemoveAt(i);
                break;
            }
        }

        // Otherwise a camelCase merged side prefix on any token ("LeftUpLeg", "Character1_LeftUpLeg").
        for (int i = 0; i < tokens.Count && side == BoneSide.Center; i++)
        {
            string token = tokens[i];
            if (token.StartsWith("left", StringComparison.Ordinal) && token.Length > 4) { side = BoneSide.Left; tokens[i] = token.Substring(4); }
            else if (token.StartsWith("right", StringComparison.Ordinal) && token.Length > 5) { side = BoneSide.Right; tokens[i] = token.Substring(5); }
        }

        // Keep only alphanumerics per token.
        for (int i = 0; i < tokens.Count; i++)
            tokens[i] = StripNonAlphanumeric(tokens[i]);
        tokens.RemoveAll(static t => t.Length == 0);
        if (tokens.Count == 0)
            return (side, empty, string.Empty);

        var clean = new List<string>();
        void Add(string c) { if (c.Length > 0 && !clean.Contains(c)) clean.Add(c); }

        Add(string.Concat(tokens)); // full core: handles "spine02", "upperarm"

        // Core without an exporter-appended trailing numeric token ("hips_64" -> "hips", "spine1_52" -> "spine1").
        if (tokens.Count >= 2 && IsPureNumeric(tokens[^1]))
            Add(string.Concat(tokens.GetRange(0, tokens.Count - 1)));

        // Fallback: last meaningful (non-numeric) token, to tolerate prefix noise ("Bip001 Pelvis" -> "pelvis").
        string fallback = string.Empty;
        for (int i = tokens.Count - 1; i >= 0; i--)
            if (!IsPureNumeric(tokens[i])) { fallback = tokens[i]; break; }
        if (clean.Contains(fallback))
            fallback = string.Empty; // already covered by a clean candidate

        return (side, clean, fallback);
    }

    private static string StripNonAlphanumeric(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (char c in token)
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                sb.Append(c);
        return sb.ToString();
    }

    private static bool IsPureNumeric(string token)
    {
        if (token.Length == 0)
            return false;
        foreach (char c in token)
            if (c < '0' || c > '9')
                return false;
        return true;
    }

    private static bool TryClassifyToken(string token, out BoneSide side)
    {
        if (s_leftTokens.Contains(token)) { side = BoneSide.Left; return true; }
        if (s_rightTokens.Contains(token)) { side = BoneSide.Right; return true; }
        if (s_centerTokens.Contains(token)) { side = BoneSide.Center; return true; }
        side = BoneSide.Center;
        return false;
    }
}
