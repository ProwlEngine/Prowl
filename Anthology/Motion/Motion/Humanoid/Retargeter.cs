using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Converts between a concrete skeleton <see cref="Pose"/> and the muscle space <see cref="HumanPose"/>
/// for humanoid avatars.
///
/// Encoding measures the body (the centre of mass and the frame of the hip and shoulder lines, relative
/// to the T pose), turns every mapped bone into muscles and stores the four goals relative to the body
/// frame and scale normalized. Decoding rebuilds every bone from the muscles (bones the target lacks are
/// skipped, bones the source lacked come out at zero), turns and moves the hips so the body lands where
/// the pose says, then solves the goals with two bone IK and applies the look at.
/// </summary>
public static class Retargeter
{
    private static readonly HumanGoal[] s_goals =
    {
        HumanGoal.LeftFoot, HumanGoal.RightFoot, HumanGoal.LeftHand, HumanGoal.RightHand
    };

    [ThreadStatic] private static Quaternion[]? s_world;
    [ThreadStatic] private static Float3[]? s_position;

    /// <summary>
    /// Encodes a source skeleton pose into the human pose. The avatar must be humanoid. Foot goals get
    /// position weight 1 so feet stay planted across proportions. Hand goals and all goal rotations are
    /// encoded with weight 0, ready for an opt in IK pass.
    /// </summary>
    public static void RetargetFrom(Avatar avatar, Pose sourcePose, HumanPose result)
    {
        ValidateHumanoid(avatar);
        ArgumentNullException.ThrowIfNull(sourcePose);
        ArgumentNullException.ThrowIfNull(result);

        HumanoidRig rig = avatar.Humanoid!;
        int count = avatar.Skeleton.BoneCount;
        float scale = rig.Scale;

        sourcePose.CalculateModelSpaceTransforms();
        result.Reset();

        Quaternion[] world = s_world is { } w && w.Length >= count ? w : s_world = new Quaternion[count];
        Float3[] position = s_position is { } p && p.Length >= count ? p : s_position = new Float3[count];
        for (int i = 0; i < count; i++)
        {
            Transform3D model = sourcePose.GetModelSpaceTransform(i);
            world[i] = model.rotation;
            position[i] = model.position;
        }

        var positions = new RigPositions(rig, position);
        Quaternion facing = rig.Facing;
        Quaternion body = HumanoidFrameBuilder.BodyFrame(positions) ?? rig.BodyBindRotation;
        result.BodyRotation = Quaternion.Normalize(Quaternion.Inverse(facing) * body * Quaternion.Inverse(rig.BodyBindRotation) * facing);
        result.BodyPosition = Quaternion.Inverse(facing) * (HumanoidFrameBuilder.CenterOfMass(positions, rig.SegmentMass) - rig.Floor) * (1f / scale);

        HumanMuscleSpace.Encode(rig, world, result);

        Quaternion bodyInverse = Quaternion.Inverse(body);
        Float3 origin = (positions.Get(HumanBodyBone.LeftUpperLeg) + positions.Get(HumanBodyBone.RightUpperLeg)) * 0.5f;
        float legLength = rig.LegLength;
        foreach (HumanGoal goal in s_goals)
        {
            (HumanBodyBone _, HumanBodyBone midBone, HumanBodyBone endBone) = HumanTrait.GetGoalChain(goal);
            int end = rig.GetSkeletonBoneIndex(endBone);
            result.SetGoal(goal, new HumanGoalState
            {
                Transform = new Transform3D(bodyInverse * (position[end] - origin) * (1f / legLength), Quaternion.Normalize(bodyInverse * world[end] * rig.GetAxisFrame(endBone)), Float3.One),
                PositionWeight = goal is HumanGoal.LeftFoot or HumanGoal.RightFoot ? 1f : 0f,
                RotationWeight = 0f,
                Pole = bodyInverse * (positions.Get(midBone) - origin) * (1f / legLength),
                HasPole = true,
            });
        }
    }

    /// <summary>Reconstructs a human pose onto the avatar's skeleton, then solves the goals and the look at.</summary>
    public static void RetargetTo(Avatar avatar, HumanPose humanPose, Pose result)
    {
        ValidateHumanoid(avatar);
        ArgumentNullException.ThrowIfNull(humanPose);
        ArgumentNullException.ThrowIfNull(result);

        HumanoidRig rig = avatar.Humanoid!;
        Skeleton skeleton = avatar.Skeleton;
        int hipsIndex = rig.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        int[] parents = skeleton.SanitizedParentIndices;

        Quaternion[] world = s_world is { } w && w.Length >= skeleton.BoneCount ? w : s_world = new Quaternion[skeleton.BoneCount];
        HumanMuscleSpace.Decode(rig, humanPose, TPoseWorld(rig, hipsIndex), world);
        WriteLocals(skeleton, world, result);

        // Turn the body so its frame matches the pose, then move the hips so the centre of mass lands where the pose puts it.
        var positions = new PosePositions(rig, result);
        Quaternion facing = rig.Facing;
        Quaternion desired = Quaternion.Normalize(facing * humanPose.BodyRotation * Quaternion.Inverse(facing) * rig.BodyBindRotation);
        Quaternion turn = HumanoidFrameBuilder.BodyFrame(positions) is { } current ? Quaternion.Normalize(desired * Quaternion.Inverse(current)) : Quaternion.Identity;
        Float3 hipsPosition = result.GetModelSpaceTransform(hipsIndex).position;
        Float3 center = HumanoidFrameBuilder.CenterOfMass(positions, rig.SegmentMass);
        hipsPosition = rig.Floor + facing * humanPose.BodyPosition * rig.Scale - turn * (center - hipsPosition);

        bool[] body = rig.BodyBones;
        for (int index = 0; index < skeleton.BoneCount; index++)
            if (body[index])
                world[index] = Quaternion.Normalize(turn * world[index]);
        WriteLocals(skeleton, world, result);

        int hipsParent = parents[hipsIndex];
        Transform3D parentModel = hipsParent == Skeleton.InvalidIndex ? Transform3D.Identity : result.GetModelSpaceTransform(hipsParent);
        Transform3D hipsLocal = result.GetTransform(hipsIndex);
        result.SetTransform(hipsIndex, new Transform3D(parentModel.InverseTransformPoint(hipsPosition), hipsLocal.rotation, hipsLocal.scale));
        result.CalculateModelSpaceTransforms();

        RepositionDisconnectedBones(avatar, result);
        SolveGoals(avatar, humanPose, result);

        if (humanPose.LookAtBodyWeight > 0f || humanPose.LookAtHeadWeight > 0f || humanPose.LookAtEyesWeight > 0f)
            LookAtSolver.Solve(result, rig, humanPose.LookAtPosition, humanPose.LookAtClampWeight, humanPose.LookAtBodyWeight, humanPose.LookAtHeadWeight, humanPose.LookAtEyesWeight);
    }

    private static void WriteLocals(Skeleton skeleton, Quaternion[] world, Pose result)
    {
        int[] parents = skeleton.SanitizedParentIndices;
        for (int index = 0; index < skeleton.BoneCount; index++)
        {
            Transform3D bind = skeleton.GetBoneParentSpaceTransform(index);
            int parent = parents[index];
            Quaternion parentWorld = parent == Skeleton.InvalidIndex ? Quaternion.Identity : world[parent];
            result.WriteLocal(index, new Transform3D(bind.position, Quaternion.Normalize(Quaternion.Inverse(parentWorld) * world[index]), bind.scale));
        }
        result.FinishWrite(PoseState.Pose);
        result.CalculateModelSpaceTransforms();
    }

    private static Quaternion TPoseWorld(HumanoidRig rig, int index)
    {
        int[] parents = rig.Skeleton.SanitizedParentIndices;
        Quaternion rotation = rig.GetTPoseLocal(index);
        for (int parent = parents[index]; parent != Skeleton.InvalidIndex; parent = parents[parent])
            rotation = rig.GetTPoseLocal(parent) * rotation;
        return Quaternion.Normalize(rotation);
    }

    // Some rigs parent a humanoid bone off a control node instead of its anatomical parent (e.g. the
    // neck/head under the root rather than the spine). Such bones do not inherit the body's motion, so
    // the head stays put while the body bobs and the neck stretches. For each bone whose nearest mapped
    // humanoid SKELETON ancestor is not its humanoid parent, re-seat it to rigidly follow that parent.
    internal static void RepositionDisconnectedBones(Avatar avatar, Pose result)
    {
        HumanoidRig rig = avatar.Humanoid!;
        Skeleton skeleton = avatar.Skeleton;
        int[] parents = skeleton.SanitizedParentIndices;

        for (int b = 0; b < HumanTrait.BoneCount; b++)
        {
            var bone = (HumanBodyBone)b;
            if (bone == HumanBodyBone.Hips || !rig.HasBone(bone))
                continue;

            int index = rig.GetSkeletonBoneIndex(bone);
            int humanParentIndex = rig.GetSkeletonBoneIndex(rig.GetPresentParent(bone));

            // First mapped humanoid bone walking up the skeleton from this bone.
            int firstMapped = parents[index];
            while (firstMapped != Skeleton.InvalidIndex && !rig.TryGetHumanBone(firstMapped, out _))
                firstMapped = parents[firstMapped];

            // Connected normally: the skeleton already routes this bone through its humanoid parent.
            if (firstMapped == humanParentIndex)
                continue;

            // Rigidly carry the bone with its humanoid parent's motion (bind -> current), keeping its
            // own already-retargeted rotation, then express in its actual skeleton parent's frame.
            Transform3D parentBind = skeleton.GetBoneModelSpaceTransform(humanParentIndex);
            Transform3D parentCurrent = result.GetModelSpaceTransform(humanParentIndex);
            Transform3D motion = TransformOps.Combine(parentCurrent, TransformOps.Inverse(parentBind));
            Float3 desiredWorld = motion.TransformPoint(skeleton.GetBoneModelSpaceTransform(index).position);

            int skelParent = parents[index];
            Transform3D skelParentCurrent = skelParent == Skeleton.InvalidIndex ? Transform3D.Identity : result.GetModelSpaceTransform(skelParent);
            Float3 localPos = skelParentCurrent.InverseTransformPoint(desiredWorld);

            Transform3D current = result.GetTransform(index);
            result.SetTransform(index, new Transform3D(localPos, current.rotation, current.scale));
        }
    }

    // Two bone IK so each weighted goal's end effector reaches its target, then the hand or foot is
    // turned toward the goal rotation by the rotation weight.
    private static void SolveGoals(Avatar avatar, HumanPose humanPose, Pose result)
    {
        HumanoidRig rig = avatar.Humanoid!;
        Skeleton skeleton = avatar.Skeleton;
        float scale = rig.LegLength;
        var positions = new PosePositions(rig, result);
        Quaternion body = HumanoidFrameBuilder.BodyFrame(positions) ?? rig.BodyBindRotation;
        Float3 origin = (positions.Get(HumanBodyBone.LeftUpperLeg) + positions.Get(HumanBodyBone.RightUpperLeg)) * 0.5f;

        foreach (HumanGoal goal in s_goals)
        {
            HumanGoalState state = humanPose.GetGoal(goal);
            if (state.PositionWeight <= 0f && state.RotationWeight <= 0f)
                continue;

            (HumanBodyBone upperBone, HumanBodyBone midBone, HumanBodyBone endBone) = HumanTrait.GetGoalChain(goal);
            int upper = rig.GetSkeletonBoneIndex(upperBone);
            int mid = rig.GetSkeletonBoneIndex(midBone);
            int end = rig.GetSkeletonBoneIndex(endBone);

            bool isLeg = goal is HumanGoal.LeftFoot or HumanGoal.RightFoot;
            Float3 offset = state.Transform.position;
            if (isLeg)
                offset += new Float3((goal == HumanGoal.LeftFoot ? -0.5f : 0.5f) * rig.FeetSpacing, 0f, 0f);

            // The retargeted world orientation of the hand or foot, captured before IK moves the chain.
            Quaternion endWorld = result.GetModelSpaceTransform(end).rotation;

            if (state.PositionWeight > 0f)
            {
                Float3 goalPosition = origin + body * (offset * scale);
                Float3? pole = state.HasPole ? origin + body * (state.Pole * scale) : null;
                TwoBoneIK.Solve(result, upper, mid, end, goalPosition, MathF.Min(state.PositionWeight, 1f), pole, isLeg ? rig.LegStretch : rig.ArmStretch);
            }

            if (state.RotationWeight > 0f)
            {
                Quaternion goalWorld = body * state.Transform.rotation * Quaternion.Inverse(rig.GetAxisFrame(endBone));
                endWorld = Quaternion.Slerp(endWorld, goalWorld, MathF.Min(state.RotationWeight, 1f));
            }

            int endParent = skeleton.SanitizedParentIndices[end];
            Quaternion endParentWorld = endParent == Skeleton.InvalidIndex ? Quaternion.Identity : result.GetModelSpaceTransform(endParent).rotation;
            Transform3D endLocal = result.GetTransform(end);
            result.SetTransform(end, new Transform3D(endLocal.position, Quaternion.Normalize(Quaternion.Inverse(endParentWorld) * endWorld), endLocal.scale));
        }
    }

    private static void ValidateHumanoid(Avatar avatar)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        if (!avatar.IsHuman)
            throw new InvalidOperationException("Retargeting requires a humanoid avatar.");
    }
}
