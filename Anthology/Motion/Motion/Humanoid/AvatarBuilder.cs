using System.Collections.Generic;
using System.Linq;

namespace Prowl.Motion;

/// <summary>
/// Builds <see cref="Avatar"/> instances from a skeleton. The recommended on-load entry point is
/// <see cref="BuildAutomatic(Skeleton)"/>, which auto-detects whether the skeleton is humanoid and assigns
/// bones accordingly, falling back to a generic avatar.
/// </summary>
public static class AvatarBuilder
{
    /// <summary>Builds a generic avatar that plays back only on its own skeleton.</summary>
    public static Avatar BuildGeneric(Skeleton skeleton, int rootBoneIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        return new Avatar(skeleton, AvatarType.Generic, humanoid: null, rootBoneIndex);
    }

    /// <summary>
    /// Builds a humanoid avatar from an explicit mapping. The description is copied. Throws
    /// <see cref="ArgumentException"/> if a required bone is missing, a bone index is outside the
    /// skeleton, or two humanoid bones share a skeleton bone.
    /// </summary>
    public static Avatar BuildHumanoid(Skeleton skeleton, HumanDescription description)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(description);
        Validate(skeleton, description);

        var rig = new HumanoidRig(skeleton, description, HumanoidFrameBuilder.Build(skeleton, description));

        return new Avatar(skeleton, AvatarType.Humanoid, rig, description.GetSkeletonBoneIndex(HumanBodyBone.Hips));
    }

    private static void Validate(Skeleton skeleton, HumanDescription description)
    {
        var missing = HumanTrait.AllBones.Where(b => HumanTrait.IsRequired(b) && !description.HasBone(b)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"The humanoid description is missing required bones: {string.Join(", ", missing)}.", nameof(description));

        var used = new Dictionary<int, HumanBodyBone>();
        foreach (HumanBodyBone bone in HumanTrait.AllBones)
        {
            if (!description.HasBone(bone))
                continue;
            int index = description.GetSkeletonBoneIndex(bone);
            if (!skeleton.IsValidBoneIndex(index))
                throw new ArgumentException($"{bone} maps to bone index {index}, outside the skeleton.", nameof(description));
            if (used.TryGetValue(index, out HumanBodyBone other))
                throw new ArgumentException($"{bone} and {other} both map to bone index {index}.", nameof(description));
            used[index] = bone;
        }
    }

    /// <summary>
    /// Tries to build a humanoid avatar by auto-mapping the skeleton. Returns false (and a null
    /// avatar) if the skeleton is not recognised as humanoid.
    /// </summary>
    public static bool TryBuildHumanoid(Skeleton skeleton, out Avatar? avatar)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        HumanoidMapResult report = HumanoidAutoMapper.Map(skeleton);
        if (report.IsHumanoid)
        {
            avatar = BuildHumanoid(skeleton, report.Description);
            avatar.MappingReport = report;
            return true;
        }

        avatar = null;
        return false;
    }

    /// <summary>
    /// The on-load entry point: detects humanoid vs generic and returns the appropriate avatar.
    /// Humanoid if <see cref="HumanoidAutoMapper"/> succeeds, otherwise a generic avatar. The
    /// resulting avatar's <see cref="Avatar.MappingReport"/> holds the unmapped-bone warnings.
    /// </summary>
    public static Avatar BuildAutomatic(Skeleton skeleton) => BuildAutomatic(skeleton, out _);

    /// <summary>As <see cref="BuildAutomatic(Skeleton)"/>, also returning the mapping report.</summary>
    public static Avatar BuildAutomatic(Skeleton skeleton, out HumanoidMapResult report)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        report = HumanoidAutoMapper.Map(skeleton);
        Avatar avatar = report.IsHumanoid ? BuildHumanoid(skeleton, report.Description) : BuildGeneric(skeleton);
        avatar.MappingReport = report;
        return avatar;
    }
}
