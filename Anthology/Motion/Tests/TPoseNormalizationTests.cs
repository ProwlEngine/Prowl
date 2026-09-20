using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>
/// Muscle space uses a canonical T-pose reference (arms straightened horizontal). So a T-pose rig
/// encodes its rest near muscle 0, while an A-pose rig (arms angled down) encodes its rest as a
/// clearly non-zero "arms down" muscle - which is what lets a T-pose source animation reconstruct as
/// a T-pose on an A-pose target.
/// </summary>
public class TPoseNormalizationTests
{
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

    private static Float3 Norm(Float3 v) { float l = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z); return l < 1e-6f ? v : new Float3(v.X / l, v.Y / l, v.Z / l); }

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

    private static Float3 Mul(Float3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
}
