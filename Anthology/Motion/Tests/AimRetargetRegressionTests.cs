using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>
/// Regressions for the direction/aim retargeter: a deep knee bend must not spin the foot (the muscle
/// codec hit a 180-degree singularity there), and a finger curl must transfer across rigs whose fingers
/// have different bind orientations (the old bind-relative-delta path broke on differing conventions).
/// </summary>
public class AimRetargetRegressionTests
{
    private static Float3 Norm(Float3 v) { float l = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z); return l < 1e-6f ? v : new Float3(v.X / l, v.Y / l, v.Z / l); }
    private static float AngleDeg(Quaternion a, Quaternion b) => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), -1f, 1f)) * 180f / MathF.PI;
    private static float AngleBetween(Float3 a, Float3 b) => MathF.Acos(Math.Clamp(a.X * b.X + a.Y * b.Y + a.Z * b.Z, -1f, 1f)) * 180f / MathF.PI;

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
}
