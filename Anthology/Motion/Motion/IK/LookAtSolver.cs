using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// A weighted multi segment humanoid look at: turns the spine, neck and head (then the eyes) so the
/// head's forward ends up facing a model space target, split into yaw about the body up and pitch
/// about the body right so the head never rolls over.
/// </summary>
/// <remarks>
/// <para>The body frame comes from the shoulders and hips. Each bone's forward axis is calibrated from
/// the reference pose, where the head and eyes are assumed to face the body forward.</para>
/// <para>Weights: <c>weight</c> scales everything. <c>bodyWeight</c> is how much of the look the
/// spine takes, <c>headWeight</c> how much the head reaches in total (neck and head add what the body
/// did not), and <c>eyesWeight</c> how far the eyes turn the rest of the way.</para>
/// </remarks>
public static class LookAtSolver
{
    private const float Epsilon = 1e-5f;

    private static readonly (HumanBodyBone Bone, float Share)[] s_bodyChain =
    {
        (HumanBodyBone.Spine, 0.3f),
        (HumanBodyBone.Chest, 0.4f),
        (HumanBodyBone.UpperChest, 0.3f),
    };

    private static readonly (HumanBodyBone Bone, float Share)[] s_headChain =
    {
        (HumanBodyBone.Neck, 0.4f),
        (HumanBodyBone.Head, 0.6f),
    };

    /// <summary>Solves with an overall weight of 1.</summary>
    public static void Solve(Pose pose, HumanoidRig rig, Float3 target, float clampWeight, float bodyWeight, float headWeight, float eyesWeight)
        => Solve(pose, rig, target, 1f, clampWeight, bodyWeight, headWeight, eyesWeight);

    /// <summary>
    /// Aims the head at <paramref name="target"/> (model space), blended from the input pose by
    /// <paramref name="weight"/>. <paramref name="clampWeight"/> limits the yaw and pitch away from the
    /// body forward, from 180 degrees at 0 down to 30 degrees at 1.
    /// </summary>
    public static void Solve(Pose pose, HumanoidRig rig, Float3 target, float weight, float clampWeight, float bodyWeight, float headWeight, float eyesWeight)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(rig);
        weight = Clamp01(weight);
        bodyWeight = Clamp01(bodyWeight);
        headWeight = Clamp01(headWeight);
        eyesWeight = Clamp01(eyesWeight);
        if (weight <= 0f || (bodyWeight <= 0f && headWeight <= 0f && eyesWeight <= 0f))
            return;
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z) || !rig.HasBone(HumanBodyBone.Head))
            return;
        if (!TryGetBodyFrame(pose, rig, out BodyFrame frame) || !TryGetBodyFrame(null, rig, out BodyFrame referenceFrame))
            return;

        float maxAngle = Lerp(MathF.PI, MathF.PI / 6f, Clamp01(clampWeight));

        int head = rig.GetSkeletonBoneIndex(HumanBodyBone.Head);
        Float3 headForward = ReferenceLocalForward(rig, head, referenceFrame);
        Transform3D headW = pose.GetModelSpaceTransform(head);
        Float3 toTarget = target - headW.position;
        if (Float3.Length(toTarget) > Epsilon)
        {
            var from = frame.Angles(headW.rotation * headForward);
            var to = frame.Clamp(frame.Angles(toTarget), maxAngle);

            float bodyTarget = weight * bodyWeight;
            float headTarget = weight * MathF.Max(bodyWeight, headWeight);
            float reached = ApplyChain(pose, rig, frame, s_bodyChain, from, to, 0f, bodyTarget);
            ApplyChain(pose, rig, frame, s_headChain, from, to, reached, headTarget);
        }

        float eyes = weight * eyesWeight;
        if (eyes > 0f)
        {
            AimEye(pose, rig, frame, referenceFrame, HumanBodyBone.LeftEye, target, maxAngle, eyes);
            AimEye(pose, rig, frame, referenceFrame, HumanBodyBone.RightEye, target, maxAngle, eyes);
        }
    }

    // Spreads the look from fraction 'reached' to 'goal' over the present bones of the chain. Returns the fraction reached.
    private static float ApplyChain(Pose pose, HumanoidRig rig, BodyFrame frame, (HumanBodyBone Bone, float Share)[] chain, (float Yaw, float Pitch) from, (float Yaw, float Pitch) to, float reached, float goal)
    {
        if (goal <= reached)
            return reached;

        float present = 0f;
        foreach ((HumanBodyBone bone, float share) in chain)
            if (rig.HasBone(bone))
                present += share;
        if (present <= 0f)
            return reached;

        float yawDelta = WrapAngle(to.Yaw - from.Yaw);
        float pitchDelta = to.Pitch - from.Pitch;
        float span = goal - reached;
        foreach ((HumanBodyBone bone, float share) in chain)
        {
            if (!rig.HasBone(bone))
                continue;
            float next = reached + span * share / present;
            Quaternion before = frame.Rotation(from.Yaw + yawDelta * reached, from.Pitch + pitchDelta * reached);
            Quaternion after = frame.Rotation(from.Yaw + yawDelta * next, from.Pitch + pitchDelta * next);
            RotateBone(pose, rig.GetSkeletonBoneIndex(bone), after * Quaternion.Inverse(before));
            reached = next;
        }
        return reached;
    }

    private static void AimEye(Pose pose, HumanoidRig rig, BodyFrame frame, BodyFrame referenceFrame, HumanBodyBone eye, Float3 target, float maxAngle, float fraction)
    {
        if (!rig.HasBone(eye))
            return;

        int index = rig.GetSkeletonBoneIndex(eye);
        Transform3D eyeW = pose.GetModelSpaceTransform(index);
        Float3 toTarget = target - eyeW.position;
        if (Float3.Length(toTarget) <= Epsilon)
            return;

        var from = frame.Angles(eyeW.rotation * ReferenceLocalForward(rig, index, referenceFrame));
        var to = frame.Clamp(frame.Angles(toTarget), maxAngle);
        Quaternion before = frame.Rotation(from.Yaw, from.Pitch);
        Quaternion after = frame.Rotation(from.Yaw + WrapAngle(to.Yaw - from.Yaw) * fraction, from.Pitch + (to.Pitch - from.Pitch) * fraction);
        RotateBone(pose, index, after * Quaternion.Inverse(before));
    }

    private static void RotateBone(Pose pose, int index, Quaternion worldDelta)
    {
        int parent = pose.Skeleton.SanitizedParentIndices[index];
        Quaternion parentWorld = parent == Skeleton.InvalidIndex ? Quaternion.Identity : pose.GetModelSpaceTransform(parent).rotation;
        Quaternion newWorld = worldDelta * pose.GetModelSpaceTransform(index).rotation;

        Transform3D local = pose.GetTransform(index);
        pose.SetTransform(index, new Transform3D(local.position, Quaternion.Normalize(Quaternion.Inverse(parentWorld) * newWorld), local.scale));
    }

    // The bone's forward axis in its own local frame, taken as the body forward in the reference pose.
    private static Float3 ReferenceLocalForward(HumanoidRig rig, int index, BodyFrame referenceFrame)
        => Quaternion.Inverse(rig.Skeleton.GetBoneModelSpaceTransform(index).rotation) * referenceFrame.Forward;

    // A null pose reads the reference pose.
    private static bool TryGetBodyFrame(Pose? pose, HumanoidRig rig, out BodyFrame frame)
    {
        Float3 lu = Position(pose, rig, HumanBodyBone.LeftUpperLeg);
        Float3 ru = Position(pose, rig, HumanBodyBone.RightUpperLeg);
        Float3 la = Position(pose, rig, HumanBodyBone.LeftUpperArm);
        Float3 ra = Position(pose, rig, HumanBodyBone.RightUpperArm);

        Float3 up = (la + ra) * 0.5f - (lu + ru) * 0.5f;
        Float3 left = (lu - ru) + (la - ra);
        Float3 forward = Float3.Cross(up, left);
        if (Float3.Length(up) < Epsilon || Float3.Length(forward) < Epsilon)
        {
            frame = default;
            return false;
        }

        up = Float3.Normalize(up);
        forward = Float3.Normalize(forward);
        frame = new BodyFrame(up, forward, Float3.Cross(up, forward));
        return true;
    }

    private static Float3 Position(Pose? pose, HumanoidRig rig, HumanBodyBone bone)
    {
        int index = rig.GetSkeletonBoneIndex(bone);
        return pose is null ? rig.Skeleton.GetBoneModelSpaceTransform(index).position : pose.GetModelSpaceTransform(index).position;
    }

    private static float WrapAngle(float radians)
    {
        while (radians > MathF.PI) radians -= 2f * MathF.PI;
        while (radians < -MathF.PI) radians += 2f * MathF.PI;
        return radians;
    }

    private static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Clamp(v, 0f, 1f);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private readonly record struct BodyFrame(Float3 Up, Float3 Forward, Float3 Right)
    {
        public (float Yaw, float Pitch) Angles(Float3 direction)
        {
            Float3 d = TransformOps.SafeNormalize(direction);
            float pitch = MathF.Asin(Math.Clamp(Float3.Dot(d, Up), -1f, 1f));
            float yaw = MathF.Atan2(Float3.Dot(d, Right), Float3.Dot(d, Forward));
            return (yaw, pitch);
        }

        public (float Yaw, float Pitch) Clamp((float Yaw, float Pitch) angles, float maxAngle)
        {
            float maxPitch = MathF.Min(maxAngle, MathF.PI / 2f);
            return (Math.Clamp(angles.Yaw, -maxAngle, maxAngle), Math.Clamp(angles.Pitch, -maxPitch, maxPitch));
        }

        // Takes the body forward to the direction with this yaw and pitch, without roll.
        public Quaternion Rotation(float yaw, float pitch)
            => Quaternion.AxisAngle(Up, yaw) * Quaternion.AxisAngle(Right, -pitch);
    }
}
