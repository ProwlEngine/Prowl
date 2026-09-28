// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Motion;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Builds a ragdoll onto a humanoid's bones: a rigidbody and collider per body part, and constraints
/// joining each part to the one above it with limits a human joint has. A Ragdoll graph node uses it to
/// make the bodies it drives, and it works just as well on any imported humanoid from script.
/// </summary>
public static class RagdollBuilder
{
    public sealed record Settings
    {
        public float TotalMass { get; init; } = 70f;
        public bool HandsAndFeet { get; init; }

        /// <summary>
        /// The humanoid avatar of the bones. When given, each joint's limits sit around the middle of the
        /// avatar's muscle ranges, rather than around the pose the bones are in.
        /// </summary>
        public Avatar? Avatar { get; init; }
    }

    private enum Shape : byte { Box, Capsule, Sphere }

    private enum Need : byte { Required, Optional, HandsAndFeet }

    // Limits in degrees for when the avatar gives none, mass as a share of the total, radius as a share of length.
    private readonly record struct PartSpec(HumanBodyBone Bone, HumanBodyBone? Parent, Shape Shape,
        float Swing, float Twist, float Mass, float Radius = 0f, Need Need = Need.Required);

    // Parents come before their children, and every animated bone between two parts is a part of its own.
    private static readonly PartSpec[] s_parts =
    {
        new(HumanBodyBone.Hips, null, Shape.Box, Swing: 0f, Twist: 0f, Mass: 0.14f),
        new(HumanBodyBone.Spine, HumanBodyBone.Hips, Shape.Box, Swing: 20f, Twist: 15f, Mass: 0.08f, Need: Need.Optional),
        new(HumanBodyBone.Chest, HumanBodyBone.Spine, Shape.Box, Swing: 20f, Twist: 15f, Mass: 0.1f),
        new(HumanBodyBone.UpperChest, HumanBodyBone.Chest, Shape.Box, Swing: 20f, Twist: 15f, Mass: 0.06f, Need: Need.Optional),
        new(HumanBodyBone.Neck, HumanBodyBone.UpperChest, Shape.Capsule, Swing: 30f, Twist: 30f, Mass: 0.025f, Radius: 0.35f, Need: Need.Optional),
        new(HumanBodyBone.Head, HumanBodyBone.Neck, Shape.Sphere, Swing: 40f, Twist: 40f, Mass: 0.065f),
        new(HumanBodyBone.LeftShoulder, HumanBodyBone.UpperChest, Shape.Capsule, Swing: 60f, Twist: 45f, Mass: 0.035f, Radius: 0.25f, Need: Need.Optional),
        new(HumanBodyBone.RightShoulder, HumanBodyBone.UpperChest, Shape.Capsule, Swing: 60f, Twist: 45f, Mass: 0.035f, Radius: 0.25f, Need: Need.Optional),
        new(HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftShoulder, Shape.Capsule, Swing: 80f, Twist: 60f, Mass: 0.03f, Radius: 0.2f),
        new(HumanBodyBone.RightUpperArm, HumanBodyBone.RightShoulder, Shape.Capsule, Swing: 80f, Twist: 60f, Mass: 0.03f, Radius: 0.2f),
        new(HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftUpperArm, Shape.Capsule, Swing: 140f, Twist: 90f, Mass: 0.02f, Radius: 0.18f),
        new(HumanBodyBone.RightLowerArm, HumanBodyBone.RightUpperArm, Shape.Capsule, Swing: 140f, Twist: 90f, Mass: 0.02f, Radius: 0.18f),
        new(HumanBodyBone.LeftUpperLeg, HumanBodyBone.Hips, Shape.Capsule, Swing: 70f, Twist: 30f, Mass: 0.11f, Radius: 0.16f),
        new(HumanBodyBone.RightUpperLeg, HumanBodyBone.Hips, Shape.Capsule, Swing: 70f, Twist: 30f, Mass: 0.11f, Radius: 0.16f),
        new(HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftUpperLeg, Shape.Capsule, Swing: 140f, Twist: 30f, Mass: 0.05f, Radius: 0.14f),
        new(HumanBodyBone.RightLowerLeg, HumanBodyBone.RightUpperLeg, Shape.Capsule, Swing: 140f, Twist: 30f, Mass: 0.05f, Radius: 0.14f),
        new(HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm, Shape.Box, Swing: 40f, Twist: 10f, Mass: 0.008f, Need: Need.HandsAndFeet),
        new(HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm, Shape.Box, Swing: 40f, Twist: 10f, Mass: 0.008f, Need: Need.HandsAndFeet),
        new(HumanBodyBone.LeftFoot, HumanBodyBone.LeftLowerLeg, Shape.Box, Swing: 30f, Twist: 10f, Mass: 0.015f, Need: Need.HandsAndFeet),
        new(HumanBodyBone.RightFoot, HumanBodyBone.RightLowerLeg, Shape.Box, Swing: 30f, Twist: 10f, Mass: 0.015f, Need: Need.HandsAndFeet),
    };

    /// <summary>
    /// The humanoid bones of an animator's rig, found in its hierarchy. Null with a reason when the rig
    /// is not humanoid or a bone the ragdoll needs is missing.
    /// </summary>
    public static Dictionary<HumanBodyBone, Transform>? FindBones(Animator animator, out string problem)
    {
        Avatar? avatar = animator.Avatar.Res;
        HumanoidRig? rig = avatar.IsValid() ? avatar!.Runtime?.Humanoid : null;
        if (rig == null || avatar!.Skeleton == null)
        {
            problem = "The animator's avatar is not humanoid.";
            return null;
        }
        return FindBones(rig, new AnimatorBinding(animator.Transform, avatar.Skeleton), out problem);
    }

    /// <summary>The humanoid bones of a rig, as bound to a hierarchy.</summary>
    internal static Dictionary<HumanBodyBone, Transform>? FindBones(HumanoidRig rig, AnimatorBinding binding, out string problem)
    {
        problem = string.Empty;
        var bones = new Dictionary<HumanBodyBone, Transform>();
        foreach (HumanBodyBone bone in Enum.GetValues<HumanBodyBone>())
        {
            if (!rig.HasBone(bone)) continue;
            Transform? transform = binding.BoneTransform(rig.GetSkeletonBoneIndex(bone));
            if (transform != null) bones[bone] = transform;
        }

        if (!bones.ContainsKey(HumanBodyBone.Chest) && bones.TryGetValue(HumanBodyBone.Spine, out Transform? spine))
            bones[HumanBodyBone.Chest] = spine;

        foreach (PartSpec part in s_parts)
            if (part.Need == Need.Required && !bones.ContainsKey(part.Bone))
            {
                problem = $"The rig has no {part.Bone} bone under the animator.";
                return null;
            }
        return bones;
    }

    /// <summary>True when any bone a ragdoll would use already has a rigidbody.</summary>
    public static bool HasRagdoll(IReadOnlyDictionary<HumanBodyBone, Transform> bones)
    {
        foreach (PartSpec part in s_parts)
            if (bones.TryGetValue(part.Bone, out Transform? bone) && bone.GameObject.GetComponent<Rigidbody3D>().IsValid())
                return true;
        return false;
    }

    /// <summary>Adds the ragdoll's components and returns the rigidbody made for each part.</summary>
    public static Dictionary<HumanBodyBone, Rigidbody3D> Build(Transform root, IReadOnlyDictionary<HumanBodyBone, Transform> bones, Settings settings)
    {
        var bodies = new Dictionary<HumanBodyBone, Rigidbody3D>();
        Float3 forward = Float3.Normalize(root.Forward);

        float massTotal = 0f;
        foreach (PartSpec part in s_parts)
            if (Included(part, bones, settings)) massTotal += part.Mass;

        Prowl.Motion.Avatar? human = settings.Avatar.IsValid() ? settings.Avatar!.Runtime : null;
        Pose? middle = human?.Humanoid != null ? MiddlePose(human) : null;

        foreach (PartSpec part in s_parts)
        {
            if (!Included(part, bones, settings)) continue;
            Transform bone = bones[part.Bone];
            GameObject go = bone.GameObject;

            Rigidbody3D body = go.AddComponent<Rigidbody3D>();
            body.Mass = settings.TotalMass * part.Mass / massTotal;
            bodies[part.Bone] = body;

            AddCollider(go, part, bone, bones, forward);

            if (IncludedParent(part, bones, settings) is not { } parentBone) continue;
            AddJoint(go, part, bone, bones, bodies[parentBone], parentBone, forward, human, middle);
        }
        return bodies;
    }

    private static bool Included(PartSpec part, IReadOnlyDictionary<HumanBodyBone, Transform> bones, Settings settings)
    {
        if (!bones.TryGetValue(part.Bone, out Transform? bone)) return false;
        if (part.Need == Need.HandsAndFeet) return settings.HandsAndFeet;
        if (part.Need == Need.Required) return true;

        // A rig missing a bone can map another part onto the same one, which needs only one body.
        foreach (PartSpec other in s_parts)
            if (other.Need == Need.Required && bones.TryGetValue(other.Bone, out Transform? shared) && ReferenceEquals(shared, bone))
                return false;
        return true;
    }

    // The nearest part up the chain the ragdoll has.
    private static HumanBodyBone? IncludedParent(PartSpec part, IReadOnlyDictionary<HumanBodyBone, Transform> bones, Settings settings)
    {
        for (HumanBodyBone? bone = part.Parent; bone is { } b; bone = Spec(b).Parent)
            if (Included(Spec(b), bones, settings)) return b;
        return null;
    }

    private static PartSpec Spec(HumanBodyBone bone) => Array.Find(s_parts, p => p.Bone == bone);

    private static void AddCollider(GameObject go, PartSpec part, Transform bone, IReadOnlyDictionary<HumanBodyBone, Transform> bones, Float3 forward)
    {
        float scale = Scale(bone);
        Float3 start = bone.Position;
        Float3 end = SegmentEnd(part.Bone, bone, bones, forward);
        Float3 along = end - start;
        float length = Float3.Length(along);
        Float3 localAlong = bone.InverseTransformDirection(Float3.Normalize(along));
        Float3 localCenter = bone.InverseTransformPoint((start + end) * 0.5f);

        switch (part.Shape)
        {
            case Shape.Capsule:
            {
                CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
                capsule.Radius = length * part.Radius / scale;
                capsule.Height = length / scale;
                capsule.Center = localCenter;
                capsule.Rotation = Quaternion.ToEuler(Quaternion.FromToRotation(Float3.UnitY, localAlong));
                return;
            }
            case Shape.Sphere:
            {
                SphereCollider sphere = go.AddComponent<SphereCollider>();
                sphere.Radius = length * 0.5f / scale;
                sphere.Center = localCenter;
                return;
            }
            default:
            {
                // A box stands along its segment and faces the character's forward.
                Float3 width = BoxWidth(part.Bone, bone, bones, length);
                BoxCollider box = go.AddComponent<BoxCollider>();
                Float3 localForward = bone.InverseTransformDirection(forward);
                box.Rotation = Quaternion.ToEuler(Quaternion.LookRotation(Reject(localForward, localAlong), localAlong));
                box.Size = new Float3(width.X, length, width.Z) / scale;
                box.Center = localCenter;
                return;
            }
        }
    }

    private static void AddJoint(GameObject go, PartSpec part, Transform bone, IReadOnlyDictionary<HumanBodyBone, Transform> bones,
        Rigidbody3D parent, HumanBodyBone parentBone, Float3 forward, Prowl.Motion.Avatar? human, Pose? middle)
    {
        Float3 along = Float3.Normalize(SegmentEnd(part.Bone, bone, bones, forward) - bone.Position);
        Float3 localAlong = bone.InverseTransformDirection(along);

        // The cone is centred on the middle of the part's muscle ranges and as wide as the widest of them.
        Float3 centre = parent.Transform.InverseTransformDirection(along);
        float swing = part.Swing;
        if (human?.Humanoid is { } rig && middle != null && rig.HasBone(part.Bone) && rig.HasBone(parentBone)
            && rig.GetSkeletonBoneIndex(part.Bone) != rig.GetSkeletonBoneIndex(parentBone))
        {
            Quaternion child = middle.GetModelSpaceTransform(rig.GetSkeletonBoneIndex(part.Bone)).rotation;
            Quaternion above = middle.GetModelSpaceTransform(rig.GetSkeletonBoneIndex(parentBone)).rotation;
            centre = Quaternion.Inverse(above) * child * localAlong;
            float range = MathF.Max(HalfRange(rig, part.Bone, MuscleAxis.Y), HalfRange(rig, part.Bone, MuscleAxis.Z));
            if (range > 0f) swing = range;
        }

        BallSocketConstraint socket = go.AddComponent<BallSocketConstraint>();
        socket.ConnectedBody = parent;
        socket.Anchor = Float3.Zero;

        ConeLimitConstraint cone = go.AddComponent<ConeLimitConstraint>();
        cone.ConnectedBody = parent;
        cone.Axis = localAlong;
        cone.ConnectedAxis = centre;
        cone.MaxAngle = swing;

        TwistAngleConstraint twist = go.AddComponent<TwistAngleConstraint>();
        twist.ConnectedBody = parent;
        twist.Axis1 = localAlong;
        twist.Axis2 = centre;
        twist.MinAngle = -part.Twist;
        twist.MaxAngle = part.Twist;
    }

    // The skeleton posed with every muscle in the middle of its range.
    private static Pose MiddlePose(Prowl.Motion.Avatar human)
    {
        HumanoidRig rig = human.Humanoid!;
        var pose = new HumanPose();
        for (int muscle = 0; muscle < HumanTrait.MuscleCount; muscle++)
        {
            (float min, float max) = rig.GetMuscleRange(muscle);
            float middle = 0.5f * (min + max);
            float limit = middle >= 0f ? max : -min;
            pose.SetMuscle(muscle, limit > 0f ? middle / limit : 0f);
        }

        var result = new Pose(human.Skeleton);
        Retargeter.RetargetTo(human, pose, result);
        result.CalculateModelSpaceTransforms();
        return result;
    }

    private static float HalfRange(HumanoidRig rig, HumanBodyBone bone, MuscleAxis axis)
    {
        int muscle = HumanTrait.GetMuscleIndex(bone, axis);
        if (muscle < 0) return 0f;
        (float min, float max) = rig.GetMuscleRange(muscle);
        return 0.5f * (max - min);
    }

    // Where a part's segment ends: the next bone down, or a guess from its proportions when there is none.
    private static Float3 SegmentEnd(HumanBodyBone part, Transform bone, IReadOnlyDictionary<HumanBodyBone, Transform> bones, Float3 forward)
    {
        Float3 Of(HumanBodyBone b) => bones[b].Position;
        bool Has(HumanBodyBone b) => bones.ContainsKey(b);

        // The first of the bones further up the body the rig has somewhere else than this one.
        Float3 Next(params HumanBodyBone[] up)
        {
            foreach (HumanBodyBone b in up)
                if (Has(b) && Float3.LengthSquared(Of(b) - bone.Position) > 1e-8f) return Of(b);
            return bone.Position;
        }

        switch (part)
        {
            case HumanBodyBone.Hips: return Next(HumanBodyBone.Spine, HumanBodyBone.Chest, HumanBodyBone.UpperChest, HumanBodyBone.Neck, HumanBodyBone.Head);
            case HumanBodyBone.Spine: return Next(HumanBodyBone.Chest, HumanBodyBone.UpperChest, HumanBodyBone.Neck, HumanBodyBone.Head);
            case HumanBodyBone.Chest: return Next(HumanBodyBone.UpperChest, HumanBodyBone.Neck, HumanBodyBone.Head);
            case HumanBodyBone.UpperChest: return Next(HumanBodyBone.Neck, HumanBodyBone.Head);
            case HumanBodyBone.Neck: return Next(HumanBodyBone.Head);
            case HumanBodyBone.Head:
            {
                Float3 neck = Has(HumanBodyBone.Neck) ? Of(HumanBodyBone.Neck) : Has(HumanBodyBone.UpperChest) ? Of(HumanBodyBone.UpperChest) : Of(HumanBodyBone.Chest);
                float size = MathF.Max(Float3.Length(bone.Position - neck) * 2f, 0.1f);
                return bone.Position + Float3.Normalize(bone.Position - neck) * size;
            }
            case HumanBodyBone.LeftShoulder: return Of(HumanBodyBone.LeftUpperArm);
            case HumanBodyBone.RightShoulder: return Of(HumanBodyBone.RightUpperArm);
            case HumanBodyBone.LeftUpperArm: return Of(HumanBodyBone.LeftLowerArm);
            case HumanBodyBone.RightUpperArm: return Of(HumanBodyBone.RightLowerArm);
            case HumanBodyBone.LeftLowerArm: return Has(HumanBodyBone.LeftHand) ? Of(HumanBodyBone.LeftHand) : Extend(bone, Of(HumanBodyBone.LeftUpperArm), 0.9f);
            case HumanBodyBone.RightLowerArm: return Has(HumanBodyBone.RightHand) ? Of(HumanBodyBone.RightHand) : Extend(bone, Of(HumanBodyBone.RightUpperArm), 0.9f);
            case HumanBodyBone.LeftUpperLeg: return Of(HumanBodyBone.LeftLowerLeg);
            case HumanBodyBone.RightUpperLeg: return Of(HumanBodyBone.RightLowerLeg);
            case HumanBodyBone.LeftLowerLeg: return Has(HumanBodyBone.LeftFoot) ? Of(HumanBodyBone.LeftFoot) : Extend(bone, Of(HumanBodyBone.LeftUpperLeg), 1f);
            case HumanBodyBone.RightLowerLeg: return Has(HumanBodyBone.RightFoot) ? Of(HumanBodyBone.RightFoot) : Extend(bone, Of(HumanBodyBone.RightUpperLeg), 1f);
            case HumanBodyBone.LeftHand: return Extend(bone, Of(HumanBodyBone.LeftLowerArm), 0.4f);
            case HumanBodyBone.RightHand: return Extend(bone, Of(HumanBodyBone.RightLowerArm), 0.4f);
            case HumanBodyBone.LeftFoot: return Has(HumanBodyBone.LeftToes) ? Of(HumanBodyBone.LeftToes) : FootTip(bone, Of(HumanBodyBone.LeftLowerLeg), forward);
            case HumanBodyBone.RightFoot: return Has(HumanBodyBone.RightToes) ? Of(HumanBodyBone.RightToes) : FootTip(bone, Of(HumanBodyBone.RightLowerLeg), forward);
            default: return bone.Position;
        }
    }

    // How wide and deep a box part is, across its segment.
    private static Float3 BoxWidth(HumanBodyBone part, Transform bone, IReadOnlyDictionary<HumanBodyBone, Transform> bones, float length)
    {
        switch (part)
        {
            case HumanBodyBone.Hips:
            case HumanBodyBone.Spine:
            case HumanBodyBone.Chest:
            case HumanBodyBone.UpperChest:
            {
                bool lower = part is HumanBodyBone.Hips or HumanBodyBone.Spine;
                (HumanBodyBone left, HumanBodyBone right) = lower
                    ? (HumanBodyBone.LeftUpperLeg, HumanBodyBone.RightUpperLeg)
                    : (HumanBodyBone.LeftUpperArm, HumanBodyBone.RightUpperArm);
                float width = Float3.Length(bones[left].Position - bones[right].Position) * (part == HumanBodyBone.Hips ? 1.6f : lower ? 1.4f : 1.1f);
                return new Float3(width, 0f, width * 0.6f);
            }
            default:
                return new Float3(length * 0.5f, 0f, length * 0.35f);
        }
    }

    private static Float3 Extend(Transform bone, Float3 from, float share)
        => bone.Position + (bone.Position - from) * share;

    private static Float3 FootTip(Transform foot, Float3 knee, Float3 forward)
        => foot.Position + forward * Float3.Length(foot.Position - knee) * 0.3f;

    private static Float3 Reject(Float3 v, Float3 axis)
    {
        Float3 flat = v - axis * Float3.Dot(v, axis);
        return Float3.LengthSquared(flat) > 1e-8f ? Float3.Normalize(flat) : Float3.Normalize(Float3.Cross(axis, Float3.UnitX));
    }

    private static float Scale(Transform bone)
    {
        Float3 lossy = bone.LossyScale;
        return MathF.Max(MathF.Max(MathF.Abs(lossy.X), MathF.Abs(lossy.Y)), MathF.Max(MathF.Abs(lossy.Z), 1e-6f));
    }
}
