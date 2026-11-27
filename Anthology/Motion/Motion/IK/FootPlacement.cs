using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>The ground found under one foot, in character space.</summary>
public readonly struct FootGround
{
    public FootGround(float height, Float3 normal)
    {
        Found = true;
        Height = height;
        Normal = normal;
    }

    /// <summary>False when nothing was found, which the solver treats as the character's own floor.</summary>
    public bool Found { get; }

    /// <summary>The ground's height under the foot, relative to the character's floor.</summary>
    public float Height { get; }

    public Float3 Normal { get; }

    public static FootGround None => default;
}

/// <summary>
/// Humanoid foot placement: moves each foot by the height of the ground under it rather than pinning it
/// there, so a lifted foot stays lifted. The hips drop so the leg over lower ground can reach, planted
/// feet tilt onto the slope, and every offset is smoothed over time so steps do not pop.
/// </summary>
public sealed class FootPlacement
{

    private float _leftOffset, _rightOffset, _hipsOffset;
    private Float3 _leftNormal = Float3.UnitY, _rightNormal = Float3.UnitY;
    private bool _started;

    /// <summary>Whether the hips move down, or up, to keep both feet reachable.</summary>
    public bool AdjustHips { get; set; } = true;

    /// <summary>Seconds for a foot to cover half the distance to a new ground height. Zero snaps.</summary>
    public float FootSmoothing { get; set; } = 0.04f;

    /// <summary>Seconds for the hips to cover half the distance to a new height. Zero snaps.</summary>
    public float HipsSmoothing { get; set; } = 0.08f;

    /// <summary>The steepest a planted foot tilts to follow the ground, in degrees.</summary>
    public float MaxFootAngle { get; set; } = 45f;

    /// <summary>How high a foot lifts, in character units, before it stops tilting onto the ground.</summary>
    public float FootLiftHeight { get; set; } = 0.1f;

    /// <summary>Forgets the smoothed offsets, so the next solve starts on the ground it finds.</summary>
    public void Reset() => _started = false;

    /// <summary>
    /// Places both feet on the ground found under them, blended by <paramref name="weight"/>. The pose
    /// must have its model space transforms, and keeps them valid.
    /// </summary>
    public void Solve(Pose pose, HumanoidRig rig, FootGround left, FootGround right, float weight, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(rig);
        if (!HasLeg(rig, HumanGoal.LeftFoot) || !HasLeg(rig, HumanGoal.RightFoot) || !rig.HasBone(HumanBodyBone.Hips))
            return;

        float leftTarget = left.Found ? left.Height : 0f;
        float rightTarget = right.Found ? right.Height : 0f;
        Float3 leftNormal = left.Found ? Upright(left.Normal) : Float3.UnitY;
        Float3 rightNormal = right.Found ? Upright(right.Normal) : Float3.UnitY;
        float hipsTarget = MathF.Min(leftTarget, rightTarget);

        if (!_started)
        {
            (_leftOffset, _rightOffset, _hipsOffset) = (leftTarget, rightTarget, hipsTarget);
            (_leftNormal, _rightNormal) = (leftNormal, rightNormal);
            _started = true;
        }
        else
        {
            float foot = Maths.HalfLifeFactor(deltaTime, FootSmoothing), hipsShare = Maths.HalfLifeFactor(deltaTime, HipsSmoothing);
            _leftOffset += (leftTarget - _leftOffset) * foot;
            _rightOffset += (rightTarget - _rightOffset) * foot;
            _hipsOffset += (hipsTarget - _hipsOffset) * hipsShare;
            _leftNormal = TransformOps.SafeNormalize(Maths.Lerp(_leftNormal, leftNormal, foot));
            _rightNormal = TransformOps.SafeNormalize(Maths.Lerp(_rightNormal, rightNormal, foot));
        }

        weight = Maths.Clamp(weight, 0f, 1f);
        if (!(weight > 0f))
            return;

        int leftFoot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        int rightFoot = rig.GetSkeletonBoneIndex(HumanBodyBone.RightFoot);
        Transform3D leftAnimated = pose.GetModelSpaceTransform(leftFoot);
        Transform3D rightAnimated = pose.GetModelSpaceTransform(rightFoot);

        if (AdjustHips)
            MoveHips(pose, rig.GetSkeletonBoneIndex(HumanBodyBone.Hips), _hipsOffset * weight);

        PlaceFoot(pose, rig, HumanGoal.LeftFoot, leftAnimated, _leftOffset * weight, _leftNormal, weight);
        PlaceFoot(pose, rig, HumanGoal.RightFoot, rightAnimated, _rightOffset * weight, _rightNormal, weight);
        pose.CalculateModelSpaceTransforms();
    }

    private void PlaceFoot(Pose pose, HumanoidRig rig, HumanGoal goal, Transform3D animated, float offset, Float3 normal, float weight)
    {
        (HumanBodyBone upper, HumanBodyBone mid, HumanBodyBone end) = HumanTrait.GetGoalChain(goal);
        int foot = rig.GetSkeletonBoneIndex(end);

        Float3 target = animated.position + Float3.UnitY * offset;
        TwoBoneIK.Solve(pose, rig.GetSkeletonBoneIndex(upper), rig.GetSkeletonBoneIndex(mid), foot, target);

        // Only a foot near its floor tilts, so a foot swinging through the air keeps its animated angle.
        float ankleHeight = MathF.Max(0f, rig.Skeleton.GetBoneModelSpaceTransform(foot).position.Y);
        float lift = animated.position.Y - ankleHeight;
        float planted = FootLiftHeight > 0f ? 1f - Maths.Clamp(lift / FootLiftHeight, 0f, 1f) : 1f;

        Quaternion tilt = LimitedTilt(normal, MaxFootAngle);
        Quaternion rotation = Quaternion.Slerp(Quaternion.Identity, tilt, planted * weight) * animated.rotation;
        BoneWriter.SetModelRotation(pose, foot, rotation);
    }

    private static void MoveHips(Pose pose, int hips, float offset)
    {
        if (offset == 0f)
            return;

        Transform3D parent = BoneWriter.ParentModel(pose, hips);
        Transform3D local = pose.GetTransform(hips);
        Float3 model = pose.GetModelSpaceTransform(hips).position + Float3.UnitY * offset;
        Float3 position = TransformOps.InverseTransformPoint(parent, model);
        pose.SetTransform(hips, new Transform3D(position, local.rotation, local.scale));
    }

    private static Quaternion LimitedTilt(Float3 normal, float maxDegrees)
    {
        float angle = MathF.Acos(Maths.Clamp(Float3.Dot(Float3.UnitY, normal), -1f, 1f));
        Quaternion tilt = Quaternion.FromToRotation(Float3.UnitY, normal);
        float limit = MathF.Max(0f, maxDegrees) * Maths.Deg2Rad;
        return angle > limit && angle > 1e-5f ? Quaternion.Slerp(Quaternion.Identity, tilt, limit / angle) : tilt;
    }

    private static Float3 Upright(Float3 normal)
    {
        Float3 n = TransformOps.SafeNormalize(normal);
        return n.Y > 1e-3f ? n : Float3.UnitY;
    }

    private static bool HasLeg(HumanoidRig rig, HumanGoal goal)
    {
        (HumanBodyBone upper, HumanBodyBone mid, HumanBodyBone end) = HumanTrait.GetGoalChain(goal);
        return rig.HasBone(upper) && rig.HasBone(mid) && rig.HasBone(end);
    }
}
