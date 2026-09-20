using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Keeps a humanoid foot on the ground via two bone IK, weighted by a groundedness value
///. Drive the weight from a baked gravity
/// weight or a foot down event so the foot only locks while planted.
/// </summary>
/// <remarks>
/// Everything here is in model space. Callers holding world space raycast hits convert them with the
/// inverse of the character's world transform first. The ankle is kept at its reference pose height
/// above the sole (the reference pose is assumed to stand on the model origin), and the foot keeps its
/// animated orientation, tilted onto the ground normal.
/// </remarks>
public static class FootGrounding
{
    private static readonly Float3 Up = new(0f, 1f, 0f);

    /// <summary>
    /// Plants the foot on flat ground at model space height <paramref name="groundY"/>, keeping its
    /// current horizontal position, blended by <paramref name="weight"/> (0 no grounding, 1 fully planted).
    /// </summary>
    public static void Ground(Pose pose, HumanoidRig rig, HumanGoal foot, float groundY, float weight)
        => Ground(pose, rig, foot, new Float3(0f, groundY, 0f), Up, weight);

    /// <summary>
    /// Plants the foot on the ground plane through <paramref name="groundPoint"/> with
    /// <paramref name="groundNormal"/> (both model space), directly below the foot's current position.
    /// </summary>
    public static void Ground(Pose pose, HumanoidRig rig, HumanGoal foot, Float3 groundPoint, Float3 groundNormal, float weight)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(rig);
        if (!(weight > 0f))
            return;
        if (foot is not (HumanGoal.LeftFoot or HumanGoal.RightFoot))
            throw new ArgumentException("FootGrounding expects a foot goal.", nameof(foot));

        (HumanBodyBone upperBone, HumanBodyBone midBone, HumanBodyBone endBone) = HumanTrait.GetGoalChain(foot);
        if (!rig.HasBone(upperBone) || !rig.HasBone(midBone) || !rig.HasBone(endBone))
            return;

        Float3 normal = TransformOps.SafeNormalize(groundNormal);
        if (normal.Y < 1e-3f)
            return;
        weight = MathF.Min(weight, 1f);

        int end = rig.GetSkeletonBoneIndex(endBone);
        Transform3D footW = pose.GetModelSpaceTransform(end);
        float planeY = groundPoint.Y - (normal.X * (footW.position.X - groundPoint.X) + normal.Z * (footW.position.Z - groundPoint.Z)) / normal.Y;
        float ankleHeight = MathF.Max(0f, rig.Skeleton.GetBoneModelSpaceTransform(end).position.Y);
        Float3 target = new Float3(footW.position.X, planeY, footW.position.Z) + normal * ankleHeight;

        TwoBoneIK.Solve(pose, rig.GetSkeletonBoneIndex(upperBone), rig.GetSkeletonBoneIndex(midBone), end, target, weight);

        Quaternion grounded = TransformOps.FromToRotation(Up, normal) * footW.rotation;
        Quaternion footRotation = Quaternion.Slerp(pose.GetModelSpaceTransform(end).rotation, grounded, weight);
        int parent = rig.Skeleton.SanitizedParentIndices[end];
        Quaternion parentW = parent == Skeleton.InvalidIndex ? Quaternion.Identity : pose.GetModelSpaceTransform(parent).rotation;
        Transform3D local = pose.GetTransform(end);
        pose.SetTransform(end, new Transform3D(local.position, Quaternion.Inverse(parentW) * footRotation, local.scale));
    }
}
