using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>How two root motion deltas are combined.</summary>
public enum RootMotionBlendMode : byte
{
    /// <summary>Interpolate from source to target by the weight.</summary>
    Blend,
    /// <summary>Apply the target on top of the source, scaled by the weight.</summary>
    Additive,
    /// <summary>Keep the target only.</summary>
    IgnoreSource,
    /// <summary>Keep the source only.</summary>
    IgnoreTarget,
}

/// <summary>
/// Static pose blending operations: interpolative blends, masked blends, and additive blends, plus
/// root-motion delta blending.
/// A blend result is additive only when every input is additive.
/// </summary>
public static class Blender
{
    /// <summary>Interpolative blend: result = lerp(source, target, weight) per bone.</summary>
    public static void Blend(Pose result, Pose source, Pose target, float weight)
    {
        ValidateSameSize(result, source, target);
        int count = result.BoneCount;
        for (int b = 0; b < count; b++)
            result.WriteLocal(b, Transform3D.Lerp(source.GetTransform(b), target.GetTransform(b), weight));
        BlendFloats(result, source, target, weight);
        result.FinishWrite(CombinedState(source, target));
    }

    /// <summary>Interpolative blend restricted by a per-bone mask (effective weight = weight * mask).</summary>
    public static void Blend(Pose result, Pose source, Pose target, float weight, BoneMask mask)
    {
        ValidateSameSize(result, source, target);
        ArgumentNullException.ThrowIfNull(mask);
        int count = result.BoneCount;
        for (int b = 0; b < count; b++)
        {
            float w = weight * mask.GetWeight(b);
            result.WriteLocal(b, Transform3D.Lerp(source.GetTransform(b), target.GetTransform(b), w));
        }
        BlendFloats(result, source, target, weight, mask);
        result.FinishWrite(CombinedState(source, target));
    }

    /// <summary>Additive blend: result = base with the additive deltas applied by weight, per bone.</summary>
    /// <exception cref="ArgumentException">The additive input is not an additive pose.</exception>
    public static void AdditiveBlend(Pose result, Pose basePose, Pose additivePose, float weight)
    {
        ValidateSameSize(result, basePose, additivePose);
        ValidateAdditive(additivePose);
        int count = result.BoneCount;
        for (int b = 0; b < count; b++)
            result.WriteLocal(b, ApplyAdditive(basePose.GetTransform(b), additivePose.GetTransform(b), weight));
        AddFloats(result, basePose, additivePose, weight);
        result.FinishWrite(CombinedState(basePose, additivePose));
    }

    /// <summary>Additive blend restricted by a per-bone mask (effective weight = weight * mask).</summary>
    /// <exception cref="ArgumentException">The additive input is not an additive pose.</exception>
    public static void AdditiveBlend(Pose result, Pose basePose, Pose additivePose, float weight, BoneMask mask)
    {
        ValidateSameSize(result, basePose, additivePose);
        ValidateAdditive(additivePose);
        ArgumentNullException.ThrowIfNull(mask);
        int count = result.BoneCount;
        for (int b = 0; b < count; b++)
        {
            float w = weight * mask.GetWeight(b);
            result.WriteLocal(b, ApplyAdditive(basePose.GetTransform(b), additivePose.GetTransform(b), w));
        }
        AddFloats(result, basePose, additivePose, weight, mask);
        result.FinishWrite(CombinedState(basePose, additivePose));
    }

    /// <summary>
    /// Measures a pose against the skeleton's reference pose, giving the additive pose that turns one
    /// into the other.
    /// </summary>
    public static void MakeAdditive(Pose result, Pose pose) => MakeAdditive(result, pose, null);

    /// <summary>Measures a pose against another pose, giving the additive pose between them.</summary>
    public static void MakeAdditive(Pose result, Pose pose, Pose? reference)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(pose);
        if (result.BoneCount != pose.BoneCount || (reference is not null && reference.BoneCount != pose.BoneCount))
            throw new ArgumentException("All poses in a subtraction must share the same bone count.");

        IReadOnlyList<Transform3D> bind = pose.Skeleton.ParentSpaceReferencePose;
        for (int b = 0; b < result.BoneCount; b++)
        {
            Transform3D from = reference is null ? bind[b] : reference.GetTransform(b);
            Transform3D to = pose.GetTransform(b);
            result.WriteLocal(b, new Transform3D(
                to.position - from.position,
                Quaternion.Normalize(Quaternion.Inverse(from.rotation) * to.rotation),
                to.scale - from.scale));
        }

        for (int c = 0; c < result.FloatChannelCount && c < pose.FloatChannelCount; c++)
        {
            float from = reference is null ? 0f : reference.GetFloat(c);
            result.WriteFloat(c, pose.GetFloat(c) - from);
        }

        result.FinishWrite(PoseState.AdditivePose);
    }

    /// <summary>Blends two root-motion deltas by weight (0 = source, 1 = target).</summary>
    public static Transform3D BlendRootMotionDeltas(Transform3D source, Transform3D target, float weight, RootMotionBlendMode mode = RootMotionBlendMode.Blend)
    {
        switch (mode)
        {
            case RootMotionBlendMode.IgnoreSource:
                return target;
            case RootMotionBlendMode.IgnoreTarget:
                return source;
            case RootMotionBlendMode.Additive:
                if (weight <= 0f)
                    return source;
                Quaternion rotation = Quaternion.Slerp(source.rotation, source.rotation * target.rotation, weight);
                return new Transform3D(source.position + target.position * weight, rotation, source.scale);
            default:
                if (weight <= 0f)
                    return source;
                if (weight >= 1f)
                    return target;
                return Transform3D.Lerp(source, target, weight);
        }
    }

    /// <summary>
    /// Synchronized blend: <paramref name="source"/> plays at <paramref name="referencePhase"/> (its
    /// own normalized time), and <paramref name="target"/> is time-warped to the same sync-event phase
    /// via its sync track so the two clips stay aligned (e.g. footfalls match). When the source has
    /// fewer sync events than the target it loops several times per target cycle;
    /// <paramref name="sourceLoop"/> says which of those loops is playing. The two scratch poses must
    /// match the result skeleton.
    /// </summary>
    public static void BlendSynchronized(Pose result, AnimationClipBase source, AnimationClipBase target, float referencePhase, float weight, Pose scratchSource, Pose scratchTarget, int sourceLoop = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        SyncTrackTime local = source.SyncTrack.GetTime(referencePhase);
        var syncPosition = new SyncTrackTime(local.EventIndex + sourceLoop * source.SyncTrack.EventCount, local.PercentageThrough);
        float targetTime = source.SyncTrack.RemapTo(syncPosition, target.SyncTrack);

        source.GetPose(referencePhase, scratchSource);
        target.GetPose(targetTime, scratchTarget);
        Blend(result, scratchSource, scratchTarget, weight);
    }

    // A channel belongs to no bone, so a mask weighs it by its own channel weight.
    private static void BlendFloats(Pose result, Pose source, Pose target, float weight, BoneMask? mask = null)
    {
        int count = result.FloatChannelCount;
        if (count == 0 || source.FloatChannelCount != count || target.FloatChannelCount != count)
            return;
        bool masked = mask is not null && mask.ChannelCount == count;
        for (int c = 0; c < count; c++)
        {
            float a = source.GetFloat(c);
            float w = masked ? weight * mask!.GetChannelWeight(c) : weight;
            result.WriteFloat(c, a + (target.GetFloat(c) - a) * w);
        }
    }

    private static void AddFloats(Pose result, Pose basePose, Pose additivePose, float weight, BoneMask? mask = null)
    {
        int count = result.FloatChannelCount;
        if (count == 0 || basePose.FloatChannelCount != count || additivePose.FloatChannelCount != count)
            return;
        bool masked = mask is not null && mask.ChannelCount == count;
        for (int c = 0; c < count; c++)
            result.WriteFloat(c, basePose.GetFloat(c) + additivePose.GetFloat(c) * (masked ? weight * mask!.GetChannelWeight(c) : weight));
    }

    // Rotation delta applied on top of the base, translation and scale deltas added (scaled by weight).
    private static Transform3D ApplyAdditive(in Transform3D basePose, in Transform3D additive, float weight)
    {
        Quaternion rotation = Quaternion.Slerp(basePose.rotation, basePose.rotation * additive.rotation, weight);
        return new Transform3D(basePose.position + additive.position * weight, rotation, basePose.scale + additive.scale * weight);
    }

    private static PoseState CombinedState(Pose a, Pose b) => IsAdditive(a) && IsAdditive(b) ? PoseState.AdditivePose : PoseState.Pose;

    private static bool IsAdditive(Pose pose) => pose.State is PoseState.AdditivePose or PoseState.ZeroPose;

    private static void ValidateAdditive(Pose additivePose)
    {
        if (!IsAdditive(additivePose))
            throw new ArgumentException($"The additive input must be an additive pose, but its state is {additivePose.State}.", nameof(additivePose));
    }

    private static void ValidateSameSize(Pose result, Pose a, Pose b)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (result.BoneCount != a.BoneCount || result.BoneCount != b.BoneCount)
            throw new ArgumentException("All poses in a blend must share the same bone count.");
    }
}
