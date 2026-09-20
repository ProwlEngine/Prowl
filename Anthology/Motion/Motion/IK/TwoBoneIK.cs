using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Analytic two bone inverse kinematics (the leg or arm case). Given an upper, mid, and end bone
/// (each an ancestor of the next, usually a direct parent chain), it rotates the upper and mid bones
/// so the end bone reaches a model space target, keeping bone lengths fixed.
/// </summary>
/// <remarks>
/// The bend plane comes from the chain's own bend in the input pose, so the knee or elbow stays on
/// the same side however the target moves. A straight input chain falls back to the reference pose
/// bend, then to a fixed axis of the upper bone. A pole overrides all of these.
/// </remarks>
public static class TwoBoneIK
{
    private const float Epsilon = 1e-5f;

    /// <summary>
    /// Solves so <paramref name="end"/> reaches <paramref name="target"/> (model space).
    /// <paramref name="weight"/> blends the result, stretch included, with the input pose.
    /// <paramref name="pole"/> is a model space point the joint bends toward. Invalid chains, degenerate
    /// bones and non finite targets leave the pose untouched.
    /// </summary>
    public static void Solve(Pose pose, int upper, int mid, int end, Float3 target, float weight = 1f, Float3? pole = null, float stretch = 0f)
    {
        ArgumentNullException.ThrowIfNull(pose);
        if (!(weight > 0f) || !IsFinite(target) || (pole is { } p && !IsFinite(p)))
            return;

        Skeleton skeleton = pose.Skeleton;
        if (!IsValidChain(skeleton, upper, mid, end))
            return;
        weight = MathF.Min(weight, 1f);

        Transform3D upperW = pose.GetModelSpaceTransform(upper);
        Transform3D midW = pose.GetModelSpaceTransform(mid);
        Transform3D endW = pose.GetModelSpaceTransform(end);
        int upperParent = skeleton.SanitizedParentIndices[upper];
        int midParent = skeleton.SanitizedParentIndices[mid];
        Quaternion upperParentW = upperParent == Skeleton.InvalidIndex ? Quaternion.Identity : pose.GetModelSpaceTransform(upperParent).rotation;
        Quaternion midParentW = pose.GetModelSpaceTransform(midParent).rotation;

        Float3 a = upperW.position;
        Float3 b = midW.position;
        Float3 c = endW.position;

        float lab = Float3.Distance(a, b);
        float lcb = Float3.Distance(c, b);
        if (lab < Epsilon || lcb < Epsilon)
            return;

        float naturalReach = lab + lcb;
        float maxReach = naturalReach * (1f + MathF.Max(0f, stretch));
        float lat = Math.Clamp(Float3.Distance(a, target), Epsilon, maxReach - Epsilon);
        float stretchFactor = stretch > 0f && lat > naturalReach ? lat / naturalReach : 1f;
        float labS = lab * stretchFactor;
        float lcbS = lcb * stretchFactor;

        Float3 axis = TransformOps.SafeNormalize(target - a);
        float d = (labS * labS - lcbS * lcbS + lat * lat) / (2f * lat);
        float h = MathF.Sqrt(MathF.Max(0f, labS * labS - d * d));

        Float3 bendDir = BendDirection(pose, upper, mid, end, upperW.rotation, a, b, c, axis, pole);
        Float3 knee = a + axis * d + bendDir * h;

        Quaternion qUpper = TransformOps.FromToRotation(b - a, knee - a);
        Float3 endAfterUpper = knee + qUpper * (c - b);
        Quaternion qMid = TransformOps.FromToRotation(endAfterUpper - knee, target - knee);

        Quaternion newUpperW = Quaternion.Slerp(upperW.rotation, qUpper * upperW.rotation, weight);
        Quaternion newMidW = Quaternion.Slerp(midW.rotation, qMid * qUpper * midW.rotation, weight);
        Quaternion upperDelta = newUpperW * Quaternion.Inverse(upperW.rotation);

        Transform3D upperL = pose.GetTransform(upper);
        Transform3D midL = pose.GetTransform(mid);
        pose.SetTransform(upper, new Transform3D(upperL.position, Quaternion.Inverse(upperParentW) * newUpperW, upperL.scale));
        pose.SetTransform(mid, new Transform3D(midL.position, Quaternion.Inverse(upperDelta * midParentW) * newMidW, midL.scale));

        float lengthScale = 1f + (stretchFactor - 1f) * weight;
        if (lengthScale > 1f)
        {
            ScaleOffsets(pose, mid, upper, lengthScale);
            ScaleOffsets(pose, end, mid, lengthScale);
        }
    }

    /// <summary>True if the three bones exist, are distinct, and each is an ancestor of the next.</summary>
    public static bool IsValidChain(Skeleton skeleton, int upper, int mid, int end)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        return skeleton.IsValidBoneIndex(upper) && skeleton.IsValidBoneIndex(mid) && skeleton.IsValidBoneIndex(end)
            && skeleton.IsChildBoneOf(upper, mid) && skeleton.IsChildBoneOf(mid, end);
    }

    private static Float3 BendDirection(Pose pose, int upper, int mid, int end, Quaternion upperRotation, Float3 a, Float3 b, Float3 c, Float3 axis, Float3? pole)
    {
        float boneScale = Float3.Distance(a, b) * Float3.Distance(b, c);
        Float3 normal = default;
        if (pole is { } poleTarget)
            normal = Float3.Cross(poleTarget - a, axis);
        if (Float3.Length(normal) < 1e-4f * boneScale)
            normal = Float3.Cross(b - a, c - b);
        if (Float3.Length(normal) < 1e-4f * boneScale)
            normal = upperRotation * ReferenceBendNormal(pose.Skeleton, upper, mid, end, Quaternion.Inverse(upperRotation) * (b - a));

        Float3 bend = Float3.Cross(axis, normal);
        if (Float3.Length(bend) < Epsilon)
        {
            Float3 current = b - a;
            bend = current - axis * Float3.Dot(current, axis);
        }
        return Float3.Length(bend) < Epsilon ? TransformOps.AnyPerpendicular(axis) : Float3.Normalize(bend);
    }

    // The reference pose bend normal in the upper bone's local frame, or a fixed perpendicular when that is straight too.
    private static Float3 ReferenceBendNormal(Skeleton skeleton, int upper, int mid, int end, Float3 localBone)
    {
        Transform3D refUpper = skeleton.GetBoneModelSpaceTransform(upper);
        Float3 refA = refUpper.position;
        Float3 refB = skeleton.GetBoneModelSpaceTransform(mid).position;
        Float3 refC = skeleton.GetBoneModelSpaceTransform(end).position;
        Float3 normal = Float3.Cross(refB - refA, refC - refB);
        if (Float3.Length(normal) >= 1e-4f * Float3.Distance(refA, refB) * Float3.Distance(refB, refC))
            return Quaternion.Inverse(refUpper.rotation) * normal;

        Float3 bone = TransformOps.SafeNormalize(localBone);
        return Float3.Cross(TransformOps.AnyPerpendicular(bone), bone);
    }

    // Scales the local offsets of every bone from child up to (not including) ancestor.
    private static void ScaleOffsets(Pose pose, int child, int ancestor, float scale)
    {
        for (int bone = child; bone != ancestor && bone != Skeleton.InvalidIndex; bone = pose.Skeleton.SanitizedParentIndices[bone])
        {
            Transform3D local = pose.GetTransform(bone);
            pose.SetTransform(bone, new Transform3D(local.position * scale, local.rotation, local.scale));
        }
    }

    private static bool IsFinite(Float3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
