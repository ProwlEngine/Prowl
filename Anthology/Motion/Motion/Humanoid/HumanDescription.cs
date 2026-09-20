using System.Collections.Generic;
using System.Linq;

namespace Prowl.Motion;

/// <summary>
/// The authorable definition of a humanoid mapping for a specific skeleton: which skeleton bone
/// plays each <see cref="HumanBodyBone"/>, per muscle range overrides, plus the tuning knobs (twist
/// distribution, IK stretch, feet spacing). Produced by the auto mapper or authored by hand, then consumed by
/// <see cref="AvatarBuilder"/>.
/// </summary>
public sealed class HumanDescription
{
    private readonly Dictionary<HumanBodyBone, int> _boneMap = new();
    private readonly Dictionary<int, (float Min, float Max)> _muscleRanges = new();

    /// <summary>Creates an empty mapping with default tuning values.</summary>
    public HumanDescription() { }

    /// <summary>Skeleton bone index playing the given humanoid bone, or <see cref="Skeleton.InvalidIndex"/>.</summary>
    public int GetSkeletonBoneIndex(HumanBodyBone bone)
        => _boneMap.TryGetValue(bone, out int index) ? index : Skeleton.InvalidIndex;

    /// <summary>Assigns a skeleton bone to a humanoid bone. Passing an invalid index clears the mapping.</summary>
    public void SetSkeletonBoneIndex(HumanBodyBone bone, int skeletonBoneIndex)
    {
        if (skeletonBoneIndex < 0)
            _boneMap.Remove(bone);
        else
            _boneMap[bone] = skeletonBoneIndex;
    }

    public bool HasBone(HumanBodyBone bone) => _boneMap.ContainsKey(bone);

    /// <summary>The humanoid bones that currently have a mapping.</summary>
    public IEnumerable<HumanBodyBone> MappedBones => _boneMap.Keys;

    /// <summary>True if every required humanoid bone is mapped.</summary>
    public bool HasAllRequiredBones
        => HumanTrait.AllBones.Where(HumanTrait.IsRequired).All(_boneMap.ContainsKey);

    /// <summary>
    /// The angle range in degrees a muscle spans from value -1 to +1: the override if one was set,
    /// otherwise the <see cref="HumanTrait"/> default.
    /// </summary>
    public (float Min, float Max) GetMuscleRange(int muscle)
    {
        if (_muscleRanges.TryGetValue(muscle, out var range))
            return range;
        return (HumanTrait.GetMuscleDefaultMin(muscle), HumanTrait.GetMuscleDefaultMax(muscle));
    }

    /// <summary>Overrides a muscle's angle range in degrees. Min must be zero or negative, max zero or positive.</summary>
    public void SetMuscleRange(int muscle, float minDegrees, float maxDegrees)
    {
        if ((uint)muscle >= (uint)HumanTrait.MuscleCount)
            throw new ArgumentOutOfRangeException(nameof(muscle));
        if (minDegrees > 0f || maxDegrees < 0f)
            throw new ArgumentException("A muscle range must contain zero (min <= 0 <= max).");
        _muscleRanges[muscle] = (minDegrees, maxDegrees);
    }

    /// <summary>Removes a muscle range override so the default applies again.</summary>
    public void ClearMuscleRange(int muscle) => _muscleRanges.Remove(muscle);

    /// <summary>A deep copy of this description.</summary>
    public HumanDescription Clone()
    {
        var copy = new HumanDescription
        {
            ArmStretch = ArmStretch,
            LegStretch = LegStretch,
            UpperArmTwist = UpperArmTwist,
            LowerArmTwist = LowerArmTwist,
            UpperLegTwist = UpperLegTwist,
            LowerLegTwist = LowerLegTwist,
            FeetSpacing = FeetSpacing,
        };
        foreach (var pair in _boneMap)
            copy._boneMap[pair.Key] = pair.Value;
        foreach (var pair in _muscleRanges)
            copy._muscleRanges[pair.Key] = pair.Value;
        return copy;
    }

    /// <summary>How far an arm may stretch past its length to reach a hand goal, as a fraction of the arm length.</summary>
    public float ArmStretch { get; set; } = 0.05f;

    /// <summary>How far a leg may stretch past its length to reach a foot goal, as a fraction of the leg length.</summary>
    public float LegStretch { get; set; } = 0.05f;

    /// <summary>Fraction of the upper arm twist shown on the upper arm. The forearm and hand turn with all of it.</summary>
    public float UpperArmTwist { get; set; } = 0.5f;

    /// <summary>Fraction of the forearm twist shown on the forearm. The hand turns with all of it.</summary>
    public float LowerArmTwist { get; set; } = 0.5f;

    /// <summary>Fraction of the upper leg twist shown on the upper leg. The lower leg and foot turn with all of it.</summary>
    public float UpperLegTwist { get; set; } = 0.5f;

    /// <summary>Fraction of the lower leg twist shown on the lower leg. The foot turns with all of it.</summary>
    public float LowerLegTwist { get; set; } = 0.5f;

    /// <summary>Extra sideways distance between the feet in units of leg length (half is added to each foot).</summary>
    public float FeetSpacing { get; set; } = 0.0f;
}
