using ClayClip = Prowl.Clay.AnimationClip;
using Prowl.Clay.Importer;
using Prowl.Clay;
using Prowl.Vector.Spatial;
using Prowl.Vector;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

/// <summary>Retargeting humanoid poses between rigs of different proportions and layouts.</summary>
public class Retargeting_Tests
{
    private static Float3 Norm(Float3 v) { float l = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z); return l < 1e-6f ? v : new Float3(v.X / l, v.Y / l, v.Z / l); }

    private static float AngleBetween(Float3 a, Float3 b) => MathF.Acos(Math.Clamp(a.X * b.X + a.Y * b.Y + a.Z * b.Z, -1f, 1f)) * 180f / MathF.PI;

    // Minimal humanoid (all required bones) plus a left index finger along -X whose bones are rolled by roll.
    private static (Skeleton, HumanDescription) FingerRig(Quaternion roll)
    {
        Float3 f0 = new(-0.04f, 0f, 0f);
        Float3 f1 = Quaternion.Inverse(roll) * f0;
        var defs = new (HumanBodyBone Bone, HumanBodyBone? Parent, Float3 Local)[]
        {
            (HumanBodyBone.Hips, null, new Float3(0f, 1f, 0f)),
            (HumanBodyBone.Spine, HumanBodyBone.Hips, new Float3(0f, 0.25f, 0f)),
            (HumanBodyBone.Chest, HumanBodyBone.Spine, new Float3(0f, 0.2f, 0f)),
            (HumanBodyBone.Neck, HumanBodyBone.Chest, new Float3(0f, 0.2f, 0f)),
            (HumanBodyBone.Head, HumanBodyBone.Neck, new Float3(0f, 0.15f, 0f)),
            (HumanBodyBone.LeftShoulder, HumanBodyBone.Chest, new Float3(-0.05f, 0.1f, 0f)),
            (HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftShoulder, new Float3(-0.13f, 0f, 0f)),
            (HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftUpperArm, new Float3(-0.28f, 0f, 0f)),
            (HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm, new Float3(-0.25f, 0f, 0f)),
            (HumanBodyBone.RightShoulder, HumanBodyBone.Chest, new Float3(0.05f, 0.1f, 0f)),
            (HumanBodyBone.RightUpperArm, HumanBodyBone.RightShoulder, new Float3(0.13f, 0f, 0f)),
            (HumanBodyBone.RightLowerArm, HumanBodyBone.RightUpperArm, new Float3(0.28f, 0f, 0f)),
            (HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm, new Float3(0.25f, 0f, 0f)),
            (HumanBodyBone.LeftUpperLeg, HumanBodyBone.Hips, new Float3(-0.1f, 0f, 0f)),
            (HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftUpperLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.LeftFoot, HumanBodyBone.LeftLowerLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.RightUpperLeg, HumanBodyBone.Hips, new Float3(0.1f, 0f, 0f)),
            (HumanBodyBone.RightLowerLeg, HumanBodyBone.RightUpperLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.RightFoot, HumanBodyBone.RightLowerLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.LeftIndexProximal, HumanBodyBone.LeftHand, f0),
            (HumanBodyBone.LeftIndexIntermediate, HumanBodyBone.LeftIndexProximal, f1),
            (HumanBodyBone.LeftIndexDistal, HumanBodyBone.LeftIndexIntermediate, f1),
        };

        var indexOf = new Dictionary<HumanBodyBone, int>();
        for (int i = 0; i < defs.Length; i++) indexOf[defs[i].Bone] = i;
        var ids = new StringID[defs.Length];
        var parents = new int[defs.Length];
        var pose = new Transform3D[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            ids[i] = new StringID(defs[i].Bone.ToString());
            parents[i] = defs[i].Parent is { } p ? indexOf[p] : Skeleton.InvalidIndex;
            pose[i] = new Transform3D(defs[i].Local, defs[i].Bone == HumanBodyBone.LeftIndexProximal ? roll : Quaternion.Identity, Float3.One);
        }
        var skeleton = new Skeleton(ids, parents, pose);

        var description = new HumanDescription();
        for (int i = 0; i < defs.Length; i++)
            description.SetSkeletonBoneIndex(defs[i].Bone, i);
        return (skeleton, description);
    }

    // The angle between the hand->proximal direction and the proximal->intermediate direction.
    private static float ProximalBendDeg(Avatar avatar, Pose pose)
    {
        HumanoidRig rig = avatar.Humanoid!;
        Float3 hand = pose.GetModelSpaceTransform(rig.GetSkeletonBoneIndex(HumanBodyBone.LeftHand)).position;
        Float3 prox = pose.GetModelSpaceTransform(rig.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexProximal)).position;
        Float3 inter = pose.GetModelSpaceTransform(rig.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexIntermediate)).position;
        Float3 a = Norm(new Float3(prox.X - hand.X, prox.Y - hand.Y, prox.Z - hand.Z));
        Float3 b = Norm(new Float3(inter.X - prox.X, inter.Y - prox.Y, inter.Z - prox.Z));
        return AngleBetween(a, b);
    }

    private static Skeleton NeckUnderRootHumanoid(out int headIndex)
    {
        var names = new[]
        {
            "root", "pelvis", "spine_01",
            "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l",
            "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r",
            "thigh_l", "calf_l", "foot_l",
            "thigh_r", "calf_r", "foot_r",
            "neck_01", "head",
        };
        var parents = new[]
        {
            -1, 0, 1,
            2, 3, 4, 5,
            2, 7, 8, 9,
            1, 11, 12,
            1, 14, 15,
            0, 17, // neck under root (0), head under neck (17)
        };
        // Stack each bone 0.2 above its parent so model-space Y is meaningful.
        var ids = new StringID[names.Length];
        var local = new Transform3D[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            ids[i] = new StringID(names[i]);
            local[i] = new Transform3D(new Float3(0f, 0.2f, 0f), Quaternion.Identity, Float3.One);
        }
        headIndex = 18;
        return new Skeleton(ids, parents, local);
    }

    private static Skeleton LoadRumba() => ClayModelLoader.BuildSkeleton(ModelImporter.Load(TestAssets.RumbaDancing));

    // The test rig rebuilt with some bind transforms changed.
    private static Avatar Rebuilt(HumanoidTestRig spec, Action<HumanDescription, int[], Transform3D[], Skeleton> change)
    {
        (Skeleton s, HumanDescription d) = spec.Build();
        var ids = Enumerable.Range(0, s.BoneCount).Select(s.GetBoneID).ToArray();
        var parents = Enumerable.Range(0, s.BoneCount).Select(s.GetParentBoneIndex).ToArray();
        var local = Enumerable.Range(0, s.BoneCount).Select(s.GetBoneParentSpaceTransform).ToArray();
        change(d, parents, local, s);
        return AvatarBuilder.BuildHumanoid(new Skeleton(ids, parents, local), d);
    }

    private static Avatar Humanoid() => AvatarBuilder.BuildAutomatic(ClayModelLoader.LoadHumanoidSkeleton());

    private static Float3 BodyRelativeFoot(Avatar avatar, Pose pose)
    {
        int hips = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        int foot = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        Transform3D hipsModel = pose.GetModelSpaceTransform(hips);
        Float3 footModel = pose.GetModelSpaceTransform(foot).position;
        Float3 local = hipsModel.InverseTransformPoint(footModel);
        Float3 Bind(HumanBodyBone bone) => avatar.Skeleton.GetBoneModelSpaceTransform(avatar.Humanoid!.GetSkeletonBoneIndex(bone)).position;
        float scale = Float3.Distance(Bind(HumanBodyBone.LeftUpperLeg), Bind(HumanBodyBone.LeftLowerLeg)) + Float3.Distance(Bind(HumanBodyBone.LeftLowerLeg), Bind(HumanBodyBone.LeftFoot));
        return new Float3(local.X / scale, local.Y / scale, local.Z / scale);
    }

    private static Pose Sample(Skeleton skeleton, ClayClip clip, float time)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose(calculateModelSpace: false);
        foreach (AnimationBinding binding in clip.Bindings)
        {
            if (!skeleton.IsValidBoneIndex(binding.NodeIndex))
                continue;
            Transform3D local = pose.GetTransform(binding.NodeIndex);
            if (binding.Property == AnimatedProperty.Rotation)
                pose.SetTransform(binding.NodeIndex, new Transform3D(local.position, binding.Curve.EvaluateQuaternion(time), local.scale));
            else if (binding.Property == AnimatedProperty.Position)
                pose.SetTransform(binding.NodeIndex, new Transform3D(binding.Curve.EvaluateFloat3(time), local.rotation, local.scale));
        }
        pose.CalculateModelSpaceTransforms();
        return pose;
    }

    private static readonly HumanBodyBone[] s_limbs =
    {
        HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftLowerArm, HumanBodyBone.RightUpperArm, HumanBodyBone.RightLowerArm,
        HumanBodyBone.LeftUpperLeg, HumanBodyBone.LeftLowerLeg, HumanBodyBone.RightUpperLeg, HumanBodyBone.RightLowerLeg,
    };

    // A limb direction in the rig's current body frame (X right, Y up, Z forward).
    private static Float3 BodyDirection(Avatar avatar, Pose pose, HumanBodyBone bone)
    {
        HumanoidRig rig = avatar.Humanoid!;
        Float3 Reflect(Float3 v) => v;
        Quaternion ReflectQ(Quaternion q) => q;

        HumanBodyBone child = bone switch
        {
            HumanBodyBone.LeftUpperArm => HumanBodyBone.LeftLowerArm,
            HumanBodyBone.LeftLowerArm => HumanBodyBone.LeftHand,
            HumanBodyBone.RightUpperArm => HumanBodyBone.RightLowerArm,
            HumanBodyBone.RightLowerArm => HumanBodyBone.RightHand,
            HumanBodyBone.LeftUpperLeg => HumanBodyBone.LeftLowerLeg,
            HumanBodyBone.LeftLowerLeg => HumanBodyBone.LeftFoot,
            HumanBodyBone.RightUpperLeg => HumanBodyBone.RightLowerLeg,
            _ => HumanBodyBone.RightFoot,
        };
        int hips = Index(avatar, HumanBodyBone.Hips);
        Quaternion body = ReflectQ(pose.GetModelSpaceTransform(hips).rotation) * Quaternion.Inverse(ReflectQ(avatar.Skeleton.GetBoneModelSpaceTransform(hips).rotation)) * rig.BodyBindRotation;
        return Float3.Normalize(Quaternion.Inverse(body) * Reflect(ModelPos(avatar, pose, child) - ModelPos(avatar, pose, bone)));
    }

    private static float MaxBindError(Avatar avatar, Pose pose, IEnumerable<HumanBodyBone> bones)
    {
        float max = 0f;
        foreach (HumanBodyBone bone in bones)
        {
            if (!avatar.Humanoid!.HasBone(bone))
                continue;
            int i = Index(avatar, bone);
            max = MathF.Max(max, AngleDeg(avatar.Skeleton.GetBoneParentSpaceTransform(i).rotation, pose.GetTransform(i).rotation));
        }
        return max;
    }

    private static IEnumerable<HumanBodyBone> AllMapped(Avatar avatar)
        => Enum.GetValues<HumanBodyBone>().Where(avatar.Humanoid!.HasBone);

    private static readonly Float3 Up = new(0f, 1f, 0f);

    private static float MaxModelBindError(Avatar avatar, Pose pose, IEnumerable<HumanBodyBone> bones)
    {
        float max = 0f;
        foreach (HumanBodyBone bone in bones)
        {
            int i = Index(avatar, bone);
            max = MathF.Max(max, AngleDeg(avatar.Skeleton.GetBoneModelSpaceTransform(i).rotation, pose.GetModelSpaceTransform(i).rotation));
        }
        return max;
    }

    private static HumanPose Encode(Avatar avatar, Action<Pose> pose)
    {
        Pose p = BindPose(avatar);
        pose(p);
        p.CalculateModelSpaceTransforms();
        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, p, human);
        return human;
    }

    private static Pose Decode(Avatar avatar, HumanPose human)
    {
        var result = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, result);
        result.CalculateModelSpaceTransforms();
        return result;
    }

    // T-pose humanoid whose arm segments are scaled by armReach (1.0 = normal).
    private static Skeleton TPoseHumanoid(float armReach)
    {
        float upper = 0.18f * armReach;
        float fore = 0.28f * armReach;
        float hand = 0.25f * armReach;
        var defs = new (HumanBodyBone Bone, HumanBodyBone? Parent, Float3 Local)[]
        {
            (HumanBodyBone.Hips, null, new Float3(0f, 1f, 0f)),
            (HumanBodyBone.Spine, HumanBodyBone.Hips, new Float3(0f, 0.25f, 0f)),
            (HumanBodyBone.Head, HumanBodyBone.Spine, new Float3(0f, 0.45f, 0f)),
            (HumanBodyBone.LeftUpperArm, HumanBodyBone.Spine, new Float3(-upper, 0.15f, 0f)),
            (HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftUpperArm, new Float3(-fore, 0f, 0f)),
            (HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm, new Float3(-hand, 0f, 0f)),
            (HumanBodyBone.RightUpperArm, HumanBodyBone.Spine, new Float3(upper, 0.15f, 0f)),
            (HumanBodyBone.RightLowerArm, HumanBodyBone.RightUpperArm, new Float3(fore, 0f, 0f)),
            (HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm, new Float3(hand, 0f, 0f)),
            (HumanBodyBone.LeftUpperLeg, HumanBodyBone.Hips, new Float3(-0.1f, 0f, 0f)),
            (HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftUpperLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.LeftFoot, HumanBodyBone.LeftLowerLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.RightUpperLeg, HumanBodyBone.Hips, new Float3(0.1f, 0f, 0f)),
            (HumanBodyBone.RightLowerLeg, HumanBodyBone.RightUpperLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.RightFoot, HumanBodyBone.RightLowerLeg, new Float3(0f, -0.5f, 0f)),
        };

        var indexOf = new Dictionary<HumanBodyBone, int>();
        for (int i = 0; i < defs.Length; i++) indexOf[defs[i].Bone] = i;
        var ids = new StringID[defs.Length];
        var parents = new int[defs.Length];
        var pose = new Transform3D[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            ids[i] = new StringID(defs[i].Bone.ToString());
            parents[i] = defs[i].Parent is { } p ? indexOf[p] : Skeleton.InvalidIndex;
            pose[i] = new Transform3D(defs[i].Local, Quaternion.Identity, Float3.One);
        }
        return new Skeleton(ids, parents, pose);
    }

    private static (Avatar Avatar, Pose Pose) MakeStandingAvatar()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.CalculateModelSpaceTransforms();
        return (avatar, pose);
    }

    // Builds a humanoid whose left arm extends along leftArmDir (right arm mirrored). leftArmDir
    // (-1,0,0) is a T-pose; (-0.707,-0.707,0) is an A-pose.
    private static Skeleton ArmHumanoid(Float3 leftArmDir)
    {
        Float3 rightArmDir = new(-leftArmDir.X, leftArmDir.Y, leftArmDir.Z);
        var defs = new (HumanBodyBone Bone, HumanBodyBone? Parent, Float3 Local)[]
        {
            (HumanBodyBone.Hips, null, new Float3(0f, 1f, 0f)),
            (HumanBodyBone.Spine, HumanBodyBone.Hips, new Float3(0f, 0.25f, 0f)),
            (HumanBodyBone.Head, HumanBodyBone.Spine, new Float3(0f, 0.45f, 0f)),
            (HumanBodyBone.LeftUpperArm, HumanBodyBone.Spine, new Float3(-0.18f, 0.15f, 0f)),
            (HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftUpperArm, Mul(leftArmDir, 0.28f)),
            (HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm, Mul(leftArmDir, 0.25f)),
            (HumanBodyBone.RightUpperArm, HumanBodyBone.Spine, new Float3(0.18f, 0.15f, 0f)),
            (HumanBodyBone.RightLowerArm, HumanBodyBone.RightUpperArm, Mul(rightArmDir, 0.28f)),
            (HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm, Mul(rightArmDir, 0.25f)),
            (HumanBodyBone.LeftUpperLeg, HumanBodyBone.Hips, new Float3(-0.1f, 0f, 0f)),
            (HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftUpperLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.LeftFoot, HumanBodyBone.LeftLowerLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.RightUpperLeg, HumanBodyBone.Hips, new Float3(0.1f, 0f, 0f)),
            (HumanBodyBone.RightLowerLeg, HumanBodyBone.RightUpperLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.RightFoot, HumanBodyBone.RightLowerLeg, new Float3(0f, -0.5f, 0f)),
        };

        var indexOf = new Dictionary<HumanBodyBone, int>();
        for (int i = 0; i < defs.Length; i++) indexOf[defs[i].Bone] = i;
        var ids = new StringID[defs.Length];
        var parents = new int[defs.Length];
        var pose = new Transform3D[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            ids[i] = new StringID(defs[i].Bone.ToString());
            parents[i] = defs[i].Parent is { } p ? indexOf[p] : Skeleton.InvalidIndex;
            pose[i] = new Transform3D(defs[i].Local, Quaternion.Identity, Float3.One);
        }
        return new Skeleton(ids, parents, pose);
    }

    // Rotation taking unit vector 'from' onto unit vector 'to'.
    private static Quaternion FromTo(Float3 from, Float3 to)
    {
        float d = Math.Clamp(from.X * to.X + from.Y * to.Y + from.Z * to.Z, -1f, 1f);
        if (d > 0.9999f) return Quaternion.Identity;
        Float3 axis = new(from.Y * to.Z - from.Z * to.Y, from.Z * to.X - from.X * to.Z, from.X * to.Y - from.Y * to.X);
        return Quaternion.AxisAngle(new Float3(axis.X, axis.Y, axis.Z) / MathF.Sqrt(axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z), MathF.Acos(d));
    }

    private static Float3 Mul(Float3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);

    private static readonly Float3 X = new(1f, 0f, 0f);

    private static Pose Mirrored(Avatar avatar, Pose source)
    {
        var result = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, source, result);
        result.CalculateModelSpaceTransforms();
        return result;
    }

    private static Pose Decoded(Avatar avatar, HumanPose human)
    {
        var pose = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, pose);
        pose.CalculateModelSpaceTransforms();
        return pose;
    }

    private static HumanPose Encoded(Avatar avatar, Pose pose)
    {
        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, pose, human);
        return human;
    }

    private static Skeleton CyclicSkeleton(out HumanDescription description)
    {
        (Skeleton skeleton, HumanDescription desc) = new HumanoidTestRig().Build();
        int n = skeleton.BoneCount;
        var ids = new StringID[n + 2];
        var parents = new int[n + 2];
        var local = new Transform3D[n + 2];
        for (int i = 0; i < n; i++)
        {
            ids[i] = skeleton.GetBoneID(i);
            parents[i] = skeleton.GetParentBoneIndex(i);
            local[i] = skeleton.GetBoneParentSpaceTransform(i);
        }
        ids[n] = new StringID("CycleA");
        parents[n] = n + 1;
        local[n] = Transform3D.Identity;
        ids[n + 1] = new StringID("CycleB");
        parents[n + 1] = n;
        local[n + 1] = Transform3D.Identity;
        parents[desc.GetSkeletonBoneIndex(HumanBodyBone.Spine)] = n;
        description = desc;
        return new Skeleton(ids, parents, local);
    }

    [Fact]
    public void DeepKneeBend_DoesNotSpinTheFoot()
    {
        // Sweep one rig's knee through a deep bend (with hip twist to provoke the old singularity) and
        // retarget to a differently-proportioned rig; the target foot orientation must change smoothly.
        Avatar source = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f));
        Avatar target = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid(0.85f, 0.85f));

        int sUpper = source.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int sLower = source.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);
        int tFoot = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        Transform3D ub = source.Skeleton.GetBoneParentSpaceTransform(sUpper);
        Transform3D lb = source.Skeleton.GetBoneParentSpaceTransform(sLower);

        var human = new HumanPose();
        Quaternion prev = Quaternion.Identity;
        float maxJump = 0f;
        const int N = 90;
        for (int i = 0; i < N; i++)
        {
            float bend = (i / (float)(N - 1)) * 2.6f; // up to ~150 degrees of knee flexion
            var pose = new Pose(source.Skeleton);
            pose.SetToReferencePose();
            pose.SetTransform(sUpper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.6f) * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f), ub.scale));
            pose.SetTransform(sLower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -bend), lb.scale));
            pose.CalculateModelSpaceTransforms();

            Retargeter.RetargetFrom(source, pose, human);
            var result = new Pose(target.Skeleton);
            Retargeter.RetargetTo(target, human, result);
            result.CalculateModelSpaceTransforms();

            Quaternion foot = result.GetModelSpaceTransform(tFoot).rotation;
            if (i > 0)
                maxJump = MathF.Max(maxJump, AngleDeg(prev, foot));
            prev = foot;
        }

        // A smooth ~3 deg/step sweep must never produce a near-180 flip; well under 30 deg/step.
        Assert.True(maxJump < 30f, $"foot orientation jumped {maxJump:N1} deg between adjacent frames (spin)");
    }

    [Fact]
    public void FingerCurl_TransfersAcrossDifferentFingerOrientations()
    {
        // Both fingers point sideways (-X), but the target's finger bones are rolled into a completely
        // different local axis convention. Curl the source index finger; the target must curl by the same amount.
        const float curl = 1.0f; // radians of flexion at the proximal joint
        var (srcSk, srcDesc) = FingerRig(Quaternion.Identity);
        Avatar source = AvatarBuilder.BuildHumanoid(srcSk, srcDesc);
        var (tgtSk, tgtDesc) = FingerRig(Quaternion.Normalize(Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 1.9f) * Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.7f)));
        Avatar target = AvatarBuilder.BuildHumanoid(tgtSk, tgtDesc);

        int sProx = source.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexProximal);
        var pose = new Pose(srcSk);
        pose.SetToReferencePose();
        // Curl the finger by flexing the proximal about the axis perpendicular to both the finger (-X)
        // and the hand-up; for a sideways finger that is the Z axis.
        Transform3D pb = srcSk.GetBoneParentSpaceTransform(sProx);
        pose.SetTransform(sProx, new Transform3D(pb.position, pb.rotation * Quaternion.AxisAngle(new Float3(0f, 0f, 1f), curl), pb.scale));
        pose.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(source, pose, human);
        var result = new Pose(tgtSk);
        Retargeter.RetargetTo(target, human, result);
        result.CalculateModelSpaceTransforms();

        // The bend angle at the target's proximal joint should match the source curl.
        float targetBend = ProximalBendDeg(target, result);
        float expected = curl * 180f / MathF.PI;
        Assert.True(MathF.Abs(targetBend - expected) < 12f, $"target finger bent {targetBend:N1} deg, expected ~{expected:N1}");
    }

    [Fact]
    public void DisconnectedHead_FollowsTheBodyWhenHipsMove()
    {
        Skeleton skeleton = NeckUnderRootHumanoid(out int head);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        Assert.True(avatar.IsHuman);

        int hips = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        float bindHeadY = skeleton.GetBoneModelSpaceTransform(head).position.Y;

        // Source pose: drop the hips by 0.5 (a "bob"). Everything under the hips moves with it.
        var source = new Pose(skeleton);
        source.SetToReferencePose();
        Transform3D hipsLocal = source.GetTransform(hips);
        source.SetTransform(hips, new Transform3D(new Float3(hipsLocal.position.X, hipsLocal.position.Y - 0.5f, hipsLocal.position.Z), hipsLocal.rotation, hipsLocal.scale));
        source.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, source, human);
        var result = new Pose(skeleton);
        Retargeter.RetargetTo(avatar, human, result);
        result.CalculateModelSpaceTransforms();

        // The head should have dropped with the body, not stayed locked at its bind height.
        float resultHeadY = result.GetModelSpaceTransform(head).position.Y;
        Assert.True(resultHeadY < bindHeadY - 0.3f, $"head did not follow the body: bind={bindHeadY}, result={resultHeadY}");
    }

    [ModelFact]
    public void RetargetRoundTrip_ReproducesAFingerRotation()
    {
        Skeleton skeleton = LoadRumba();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        int index = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexProximal);
        Assert.NotEqual(Skeleton.InvalidIndex, index);

        var source = new Pose(skeleton);
        source.SetToReferencePose();
        Transform3D bind = skeleton.GetBoneParentSpaceTransform(index);
        source.SetTransform(index, new Transform3D(bind.position, bind.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f), bind.scale));

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, source, human);
        var result = new Pose(skeleton);
        Retargeter.RetargetTo(avatar, human, result);

        float dot = MathF.Abs(Quaternion.Dot(source.GetTransform(index).rotation, result.GetTransform(index).rotation));
        Assert.True(dot > 0.999f);
    }

    [Fact]
    public void NeckUnderTheRoot_TurnsWithTheBody()
    {
        Avatar normal = new HumanoidTestRig().BuildAvatar();
        Avatar disconnected = Rebuilt(new HumanoidTestRig(), (d, parents, local, s) =>
        {
            int neck = d.GetSkeletonBoneIndex(HumanBodyBone.Neck);
            local[neck] = new Transform3D(s.GetBoneModelSpaceTransform(neck).position, Quaternion.Identity, Float3.One);
            parents[neck] = 0;
        });
        Pose pose = BindPose(normal);
        RotateLocal(normal, pose, HumanBodyBone.Hips, Quaternion.AxisAngle(new Float3(0f, 1f, 0f), MathF.PI / 2f));
        RotateLocal(normal, pose, HumanBodyBone.Spine, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), -0.5f));

        Pose result = Retarget(normal, pose, disconnected);

        Assert.True(AngleDeg(ModelRot(normal, pose, HumanBodyBone.Head), ModelRot(disconnected, result, HumanBodyBone.Head)) < 2f);
    }

    [Fact]
    public void RigWithItsOriginAtTheHips_TravelsTheSameDistance()
    {
        Avatar feet = new HumanoidTestRig().BuildAvatar();
        Avatar hips = Rebuilt(new HumanoidTestRig(), (_, _, local, _) => local[0] = new Transform3D(new Float3(0f, -1f, 0f), local[0].rotation, local[0].scale));

        Float3 Travel(Avatar from, Avatar to)
        {
            Pose walk = BindPose(from);
            int index = Index(from, HumanBodyBone.Hips);
            Transform3D h = walk.GetTransform(index);
            walk.SetTransform(index, new Transform3D(h.position + new Float3(0f, 0f, 0.5f), h.rotation, h.scale));
            walk.CalculateModelSpaceTransforms();
            return ModelPos(to, Retarget(from, walk, to), HumanBodyBone.Hips) - ModelPos(to, BindPose(to), HumanBodyBone.Hips);
        }

        Assert.True(Float3.Distance(Travel(feet, hips), new Float3(0f, 0f, 0.5f)) < 0.01f);
        Assert.True(Float3.Distance(Travel(hips, feet), new Float3(0f, 0f, 0.5f)) < 0.01f);
    }

    [Fact]
    public void RigWithoutChestBones_PlacesTheBodyLikeTheFullRig()
    {
        Avatar full = new HumanoidTestRig().BuildAvatar();
        Avatar noChest = new HumanoidTestRig { Chest = false, UpperChest = false }.BuildAvatar();
        Assert.InRange(noChest.Humanoid!.Scale / full.Humanoid!.Scale, 0.97f, 1.03f);

        Pose lean = BindPose(full);
        RotateLocal(full, lean, HumanBodyBone.Spine, Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.6f));
        int hips = Index(full, HumanBodyBone.Hips);
        Transform3D h = lean.GetTransform(hips);
        lean.SetTransform(hips, new Transform3D(h.position + new Float3(0f, 0f, 1f), h.rotation, h.scale));
        lean.CalculateModelSpaceTransforms();

        Pose result = Retarget(full, lean, noChest);
        Assert.True(Float3.Distance(ModelPos(full, lean, HumanBodyBone.Hips), ModelPos(noChest, result, HumanBodyBone.Hips)) < 0.02f);
    }

    [Fact]
    public void UpsideDownSource_RetargetsUpsideDown()
    {
        Avatar source = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid());
        Avatar target = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid());

        int srcHips = source.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        int tgtHips = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        int tgtHead = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);

        // Flip the whole body upside-down by rotating the (root) hips 180 degrees about X.
        var src = new Pose(source.Skeleton);
        src.SetToReferencePose();
        Transform3D hipsLocal = src.GetTransform(srcHips);
        src.SetTransform(srcHips, new Transform3D(hipsLocal.position, hipsLocal.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), MathF.PI), hipsLocal.scale));
        src.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(source, src, human);
        var result = new Pose(target.Skeleton);
        Retargeter.RetargetTo(target, human, result);
        result.CalculateModelSpaceTransforms();

        float headY = result.GetModelSpaceTransform(tgtHead).position.Y;
        float hipsY = result.GetModelSpaceTransform(tgtHips).position.Y;
        Assert.True(headY < hipsY, $"body did not invert: head Y {headY} should be below hips Y {hipsY}");
    }

    [ModelFact]
    public void RetargetFrom_EncodesReferencePoseAsNeutralSpine()
    {
        var avatar = Humanoid();
        var sourcePose = new Pose(avatar.Skeleton);
        sourcePose.SetToReferencePose();

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, sourcePose, human);

        foreach (MuscleAxis axis in Enum.GetValues<MuscleAxis>())
            Assert.Equal(0.0, (double)human.GetMuscle(HumanBodyBone.Spine, axis), 3);
        Assert.All(human.Muscles.ToArray(), m => Assert.InRange(m, -1f, 1f));
    }

    [ModelFact]
    public void RetargetTo_ReconstructsASkeletonPose()
    {
        var avatar = Humanoid();
        var human = new HumanPose();
        var result = new Pose(avatar.Skeleton);

        Retargeter.RetargetTo(avatar, human, result);

        Assert.True(result.IsValid);
    }

    [ModelFact]
    public void RoundTrip_ReproducesBoneRotation()
    {
        // Muscle space is intentionally lossy on locked/limited axes (e.g. the elbow only bends one
        // way), so we test the upper arm, whose muscle ranges are wide and free, with a moderate
        // in-range rotation; it should round-trip closely through encode/decode.
        var avatar = Humanoid();
        var rig = avatar.Humanoid!;
        int armIndex = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);

        var source = new Pose(avatar.Skeleton);
        source.SetToReferencePose();
        Transform3D bind = avatar.Skeleton.GetBoneParentSpaceTransform(armIndex);
        var rotated = new Transform3D(
            bind.position,
            bind.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f),
            bind.scale);
        source.SetTransform(armIndex, rotated);

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, source, human);
        var result = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, result);

        Quaternion sourceRot = source.GetTransform(armIndex).rotation;
        Quaternion resultRot = result.GetTransform(armIndex).rotation;
        Assert.True(MathF.Abs(Quaternion.Dot(sourceRot, resultRot)) > 0.99f);
    }

    [ModelFact]
    public void RoundTrip_PreservesRootTranslation()
    {
        var avatar = Humanoid();
        var rig = avatar.Humanoid!;
        int hipsIndex = rig.GetSkeletonBoneIndex(HumanBodyBone.Hips);

        var source = new Pose(avatar.Skeleton);
        source.SetToReferencePose();
        Transform3D bind = avatar.Skeleton.GetBoneParentSpaceTransform(hipsIndex);
        source.SetTransform(hipsIndex, new Transform3D(new Float3(1f, 2f, 3f), bind.rotation, bind.scale));

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, source, human);
        var result = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, result);

        Float3 p = result.GetTransform(hipsIndex).position;
        Assert.Equal(1.0, (double)p.X, 3);
        Assert.Equal(2.0, (double)p.Y, 3);
        Assert.Equal(3.0, (double)p.Z, 3);
    }

    [Fact]
    public void GoalIK_PinsFeetToSameBodyRelativePosition_AcrossProportions()
    {
        // Short legs vs long legs, same naming so both auto-map to humanoid.
        var source = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f));
        var target = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid(0.85f, 0.85f));

        int sUpper = source.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int sLower = source.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);

        // Bend the left knee so the foot is raised into a non-trivial, bent configuration.
        var sourcePose = new Pose(source.Skeleton);
        sourcePose.SetToReferencePose();
        Transform3D ub = source.Skeleton.GetBoneParentSpaceTransform(sUpper);
        sourcePose.SetTransform(sUpper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.7f), ub.scale));
        Transform3D lb = source.Skeleton.GetBoneParentSpaceTransform(sLower);
        sourcePose.SetTransform(sLower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.2f), lb.scale));
        sourcePose.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(source, sourcePose, human);

        var targetPose = new Pose(target.Skeleton);
        Retargeter.RetargetTo(target, human, targetPose);
        targetPose.CalculateModelSpaceTransforms();

        Float3 sourceRel = BodyRelativeFoot(source, sourcePose);
        Float3 targetRel = BodyRelativeFoot(target, targetPose);

        // The foot's scale-normalized, body-relative position should match across the two rigs.
        Assert.Equal((double)sourceRel.X, (double)targetRel.X, 2);
        Assert.Equal((double)sourceRel.Y, (double)targetRel.Y, 2);
        Assert.Equal((double)sourceRel.Z, (double)targetRel.Z, 2);
    }

    [Fact]
    public void RetargetFrom_OnGenericAvatar_Throws()
    {
        var generic = AvatarBuilder.BuildGeneric(TestSkeletons.MakeChain());
        var pose = new Pose(generic.Skeleton);
        pose.SetToReferencePose();
        Assert.Throws<InvalidOperationException>(() => Retargeter.RetargetFrom(generic, pose, new HumanPose()));
    }

    [ModelTheory]
    [InlineData("Rumba Dancing.fbx")]
    [InlineData("Head Spinning.fbx")]
    public void ClipRoundTrip_KeepsJointsAndEndEffectors(string file)
    {
        Model model = ModelImporter.Load(Path.Combine(Path.GetDirectoryName(TestAssets.RumbaDancing)!, file));
        Skeleton skeleton = ClayModelLoader.BuildSkeleton(model);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        ClayClip clip = model.AnimationClips[0];

        HumanBodyBone[] ends = { HumanBodyBone.LeftHand, HumanBodyBone.RightHand, HumanBodyBone.LeftFoot, HumanBodyBone.RightFoot, HumanBodyBone.Head };
        for (int f = 0; f < 20; f++)
        {
            Pose source = Sample(skeleton, clip, clip.Duration * f / 20f);
            Pose result = Retarget(avatar, source, avatar);

            foreach (HumanBodyBone bone in Enum.GetValues<HumanBodyBone>().Where(b => b < HumanBodyBone.LeftThumbProximal && avatar.Humanoid!.HasBone(b)))
            {
                float miss = Float3.Distance(ModelPos(avatar, source, bone), ModelPos(avatar, result, bone));
                Assert.True(miss < 0.04f * avatar.Humanoid!.Scale, $"frame {f}: {bone} moved {miss:N3}");
            }
            foreach (HumanBodyBone bone in ends)
            {
                float error = AngleDeg(ModelRot(avatar, source, bone), ModelRot(avatar, result, bone));
                Assert.True(error < 6f, $"frame {f}: {bone} turned {error:N1} degrees");
            }
        }
    }

    [ModelFact]
    public void MixamoClip_OnUnrealRig_KeepsLimbDirections()
    {
        Model rumba = ModelImporter.Load(TestAssets.RumbaDancing);
        Skeleton sourceSkeleton = ClayModelLoader.BuildSkeleton(rumba);
        Avatar source = AvatarBuilder.BuildAutomatic(sourceSkeleton);
        Avatar target = AvatarBuilder.BuildAutomatic(ClayModelLoader.BuildSkeleton(ModelImporter.Load(TestAssets.Daredevil)));
        Assert.True(source.IsHuman && target.IsHuman);
        ClayClip clip = rumba.AnimationClips[0];

        for (int f = 0; f < 10; f++)
        {
            Pose pose = Sample(sourceSkeleton, clip, clip.Duration * f / 10f);
            var human = new HumanPose();
            Retargeter.RetargetFrom(source, pose, human);
            var result = new Pose(target.Skeleton);
            Retargeter.RetargetTo(target, human, result);
            result.CalculateModelSpaceTransforms();

            foreach (HumanBodyBone bone in s_limbs)
            {
                float error = AngleDeg(BodyDirection(source, pose, bone), BodyDirection(target, result, bone));
                Assert.True(error < 8f, $"frame {f}: {bone} points {error:N1} degrees away from the source");
            }
        }
    }

    [Fact]
    public void BindFromRigMissingOptionalBones_LeavesFullTargetAtBind()
    {
        Avatar source = new HumanoidTestRig { Chest = false, UpperChest = false, Neck = false, Shoulders = false, Toes = false }.BuildAvatar();
        Avatar target = new HumanoidTestRig().BuildAvatar();

        Pose result = Retarget(source, BindPose(source), target);

        float error = MaxBindError(target, result, AllMapped(target));
        Assert.True(error < 1f, $"target moved off bind by {error:N1} degrees");
    }

    [Fact]
    public void RigUnderRotatedArmature_RetargetsBindToBind()
    {
        Avatar source = new HumanoidTestRig().BuildAvatar();
        Avatar target = new HumanoidTestRig { ArmatureRotation = Quaternion.AxisAngle(Up, MathF.PI) }.BuildAvatar();

        Pose result = Retarget(source, BindPose(source), target);

        float error = MaxBindError(target, result, AllMapped(target));
        Assert.True(error < 1f, $"target moved off bind by {error:N1} degrees");
    }

    [Fact]
    public void RigUnderRotatedArmature_RootMotionFollowsTheCharactersForward()
    {
        Avatar source = new HumanoidTestRig().BuildAvatar();
        Avatar target = new HumanoidTestRig { ArmatureRotation = Quaternion.AxisAngle(Up, MathF.PI) }.BuildAvatar();

        Pose pose = BindPose(source);
        int hips = Index(source, HumanBodyBone.Hips);
        Transform3D t = pose.GetTransform(hips);
        pose.SetTransform(hips, new Transform3D(t.position + new Float3(0f, 0f, 0.5f), t.rotation, t.scale));

        Pose result = Retarget(source, pose, target);

        Float3 moved = ModelPos(target, result, HumanBodyBone.Hips) - target.Skeleton.GetBoneModelSpaceTransform(Index(target, HumanBodyBone.Hips)).position;
        Assert.True(moved.Z < -0.45f, $"target hips moved {moved} (expected about 0.5 along its own forward, model -Z)");
    }

    [Fact]
    public void FeetTransferBetweenRigWithAndWithoutToes()
    {
        Avatar withToes = new HumanoidTestRig().BuildAvatar();
        Avatar withoutToes = new HumanoidTestRig { Toes = false }.BuildAvatar();

        HumanBodyBone[] feet = { HumanBodyBone.LeftFoot, HumanBodyBone.RightFoot };
        Assert.True(MaxBindError(withoutToes, Retarget(withToes, BindPose(withToes), withoutToes), feet) < 1f);
        Assert.True(MaxBindError(withToes, Retarget(withoutToes, BindPose(withoutToes), withToes), feet) < 1f);
    }

    [Fact]
    public void NeckLean_DoesNotTiltLimbs()
    {
        Avatar source = new HumanoidTestRig { NeckLeanDegrees = 10f }.BuildAvatar();
        Avatar target = new HumanoidTestRig { NeckLeanDegrees = 60f }.BuildAvatar();

        Pose result = Retarget(source, BindPose(source), target);

        HumanBodyBone[] limbs =
        {
            HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftHand,
            HumanBodyBone.RightUpperArm, HumanBodyBone.RightLowerArm, HumanBodyBone.RightHand,
            HumanBodyBone.LeftUpperLeg, HumanBodyBone.LeftFoot, HumanBodyBone.RightFoot,
        };
        float error = MaxModelBindError(target, result, limbs);
        Assert.True(error < 0.5f, $"limbs tilted by {error:N1} degrees");
    }

    [Fact]
    public void GoalRotation_IsBodyRelativeAndAppliedWithRotationWeight()
    {
        Avatar source = new HumanoidTestRig().BuildAvatar();
        Avatar target = new HumanoidTestRig { ArmatureRotation = Quaternion.AxisAngle(Up, MathF.PI) }.BuildAvatar();

        Quaternion footTurn = Quaternion.AxisAngle(Up, 0.6f) * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.3f);
        HumanPose turned = Encode(source, p => RotateLocal(source, p, HumanBodyBone.LeftFoot, footTurn));
        HumanPose rest = Encode(source, _ => { });

        HumanGoalState goal = rest.GetGoal(HumanGoal.LeftFoot);
        goal.Transform = new Transform3D(goal.Transform.position, turned.GetGoal(HumanGoal.LeftFoot).Transform.rotation, Float3.One);
        goal.RotationWeight = 1f;
        rest.SetGoal(HumanGoal.LeftFoot, goal);

        Pose expected = Decode(target, turned);
        Pose actual = Decode(target, rest);

        float error = AngleDeg(ModelRot(target, expected, HumanBodyBone.LeftFoot), ModelRot(target, actual, HumanBodyBone.LeftFoot));
        Assert.True(error < 1f, $"goal rotation landed {error:N1} degrees off");
    }

    [Fact]
    public void LongArmedSourceRest_KeepsTargetArmsHorizontal()
    {
        // Source arms are twice as long as the target's; both rest in a T-pose (arms horizontal).
        Avatar source = AvatarBuilder.BuildAutomatic(TPoseHumanoid(2f));
        Avatar target = AvatarBuilder.BuildAutomatic(TPoseHumanoid(1f));
        Assert.True(source.IsHuman && target.IsHuman);

        var rest = new Pose(source.Skeleton);
        rest.SetToReferencePose();
        rest.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(source, rest, human);
        var result = new Pose(target.Skeleton);
        Retargeter.RetargetTo(target, human, result);
        result.CalculateModelSpaceTransforms();

        int upper = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        int lower = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerArm);
        Float3 u = result.GetModelSpaceTransform(upper).position;
        Float3 l = result.GetModelSpaceTransform(lower).position;
        Float3 arm = l - u;
        float len = Float3.Length(arm);
        float upFraction = len < 1e-5f ? 0f : arm.Y / len;

        // The target arm must stay roughly horizontal - it must not be flung up toward an unreachable
        // hand goal. (Before the fix it rose to ~+0.27 of its length.)
        Assert.True(upFraction < 0.2f, $"target arm rose upward (Y fraction {upFraction}); it should stay horizontal");
    }

    [Fact]
    public void RetargetTo_AppliesLookAtFromHumanPose()
    {
        var (avatar, _) = MakeStandingAvatar();
        var human = new HumanPose();
        // No source rotation (rest), but request a look-at.
        human.LookAtPosition = new Float3(10f, 1.6f, 0f);
        human.LookAtHeadWeight = 1f;
        human.LookAtBodyWeight = 0.3f;

        var result = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, result);

        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);
        Quaternion bind = avatar.Skeleton.GetBoneParentSpaceTransform(head).rotation;
        Assert.True(MathF.Abs(Quaternion.Dot(bind, result.GetTransform(head).rotation)) < 0.999f);
    }

    [Fact]
    public void RigFacingBackward_RetargetsTheSameAsOneFacingForward()
    {
        Avatar forward = new HumanoidTestRig().BuildAvatar();
        Avatar backward = new HumanoidTestRig { ArmatureRotation = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), MathF.PI) }.BuildAvatar();

        Pose pose = BindPose(forward);
        RotateLocal(forward, pose, HumanBodyBone.Spine, Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f));
        RotateLocal(forward, pose, HumanBodyBone.LeftUpperArm, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.5f));
        var human = new HumanPose();
        Retargeter.RetargetFrom(forward, pose, human);
        var twin = new HumanPose();
        Pose turned = Retarget(forward, pose, backward);
        Retargeter.RetargetFrom(backward, turned, twin);

        for (int m = 0; m < HumanTrait.MuscleCount; m++)
            Assert.True(MathF.Abs(human.GetMuscle(m) - twin.GetMuscle(m)) < 1e-3f, $"{HumanTrait.GetMuscleName(m)} {human.GetMuscle(m):N4} vs {twin.GetMuscle(m):N4}");
        Assert.True(Float3.Distance(human.BodyPosition, twin.BodyPosition) < 1e-3f);
        Assert.True(AngleDeg(human.BodyRotation, twin.BodyRotation) < 0.1f);
    }

    [Fact]
    public void ArmDirection_TransfersFromTPoseSourceToAPoseTarget()
    {
        // Source is a T-pose rig (arms horizontal), target is an A-pose rig (arms 45 deg down). Point the
        // source's left upper arm overhead, retarget, and the target's upper arm must also point overhead:
        // the geometric aim transfers regardless of each rig's rest pose.
        var overhead = new Float3(0f, 1f, 0f);
        float inv = 1f / MathF.Sqrt(2f);
        Float3 srcArmDir = new(-1f, 0f, 0f);
        Avatar source = AvatarBuilder.BuildAutomatic(ArmHumanoid(srcArmDir));
        Avatar target = AvatarBuilder.BuildAutomatic(ArmHumanoid(new Float3(-inv, -inv, 0f)));
        Assert.True(source.IsHuman && target.IsHuman);

        var pose = new Pose(source.Skeleton);
        pose.SetToReferencePose();
        int srcArm = source.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        Transform3D t = pose.GetTransform(srcArm);
        pose.SetTransform(srcArm, new Transform3D(t.position, FromTo(srcArmDir, overhead), t.scale));
        pose.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(source, pose, human);
        var result = new Pose(target.Skeleton);
        Retargeter.RetargetTo(target, human, result);
        result.CalculateModelSpaceTransforms();

        int tUpper = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        int tLower = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerArm);
        Float3 up = result.GetModelSpaceTransform(tUpper).position;
        Float3 low = result.GetModelSpaceTransform(tLower).position;
        Float3 dir = Norm(new Float3(low.X - up.X, low.Y - up.Y, low.Z - up.Z));

        Assert.True(dir.Y > 0.9f, $"target arm should point overhead, got ({dir.X:N2},{dir.Y:N2},{dir.Z:N2})");
    }

    [Fact]
    public void ParentCycleAmongUnmappedBones_RetargetsAndMirrorsWithoutRecursing()
    {
        Skeleton skeleton = CyclicSkeleton(out HumanDescription description);
        Assert.False(skeleton.IsValid);
        Avatar avatar = AvatarBuilder.BuildHumanoid(skeleton, description);

        var human = new HumanPose();
        human.SetMuscle(HumanTrait.GetMuscleIndex(HumanBodyBone.Spine, MuscleAxis.Z), 0.4f);
        Pose pose = Decoded(avatar, human);
        HumanPose back = Encoded(avatar, pose);
        Pose mirrored = Mirrored(avatar, pose);

        Assert.Equal(0.4, back.GetMuscle(HumanTrait.GetMuscleIndex(HumanBodyBone.Spine, MuscleAxis.Z)), 2);
        for (int i = 0; i < skeleton.BoneCount; i++)
            Assert.True(float.IsFinite(mirrored.GetModelSpaceTransform(i).position.X));

        Avatar automatic = AvatarBuilder.BuildAutomatic(skeleton);
        Assert.NotNull(automatic);
    }
}
