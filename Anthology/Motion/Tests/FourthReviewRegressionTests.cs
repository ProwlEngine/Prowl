using Prowl.Vector;
using Prowl.Vector.Spatial;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

public class FourthReviewRegressionTests
{
    private const float Deg = MathF.PI / 180f;

    private static HumanPose Encode(Avatar avatar, Pose pose)
    {
        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, pose, human);
        return human;
    }

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

    [Theory]
    [InlineData(-0.5f)]
    [InlineData(-2f)]
    [InlineData(-5f)]
    public void SlightlyHyperextendedKnee_KeepsTheLegUntwisted(float degrees)
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose pose = BindPose(avatar);
        RotateLocal(avatar, pose, HumanBodyBone.LeftLowerLeg, Quaternion.AxisAngle(new Float3(1f, 0f, 0f), degrees * Deg));

        HumanPose human = Encode(avatar, pose);
        Assert.True(MathF.Abs(human.GetMuscle(HumanBodyBone.LeftUpperLeg, MuscleAxis.X)) < 0.05f);

        var decoded = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, decoded);
        decoded.CalculateModelSpaceTransforms();
        Assert.True(AngleDeg(ModelRot(avatar, decoded, HumanBodyBone.LeftUpperLeg), ModelRot(avatar, pose, HumanBodyBone.LeftUpperLeg)) < 1f);
    }

    [Theory]
    [InlineData(3f)]
    [InlineData(8f)]
    public void SplayedLegRig_BindEncodesWithoutTwist(float splayDegrees)
    {
        Avatar splayed = Rebuilt(new HumanoidTestRig(), (d, _, local, _) =>
        {
            int left = d.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg), right = d.GetSkeletonBoneIndex(HumanBodyBone.RightUpperLeg);
            local[left] = new Transform3D(local[left].position, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), -splayDegrees * Deg), Float3.One);
            local[right] = new Transform3D(local[right].position, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), splayDegrees * Deg), Float3.One);
        });

        HumanPose human = Encode(splayed, BindPose(splayed));
        Assert.True(MathF.Abs(human.GetMuscle(HumanBodyBone.LeftUpperLeg, MuscleAxis.X)) < 0.01f);
        Assert.True(MathF.Abs(human.GetMuscle(HumanBodyBone.LeftLowerLeg, MuscleAxis.X)) < 0.01f);
        Assert.True(MathF.Abs(human.GetMuscle(HumanBodyBone.LeftFoot, MuscleAxis.Y)) < 0.01f);
    }

    [Fact]
    public void ClavicleRoll_DoesNotSwingTheArm()
    {
        Avatar avatar = new HumanoidTestRig { Fingers = true }.BuildAvatar();
        Pose pose = BindPose(avatar);
        RotateLocal(avatar, pose, HumanBodyBone.LeftUpperArm, Quaternion.AxisAngle(new Float3(0f, 1f, 0f), -1.2f));
        RotateLocal(avatar, pose, HumanBodyBone.LeftShoulder, Quaternion.AxisAngle(new Float3(-1f, 0f, 0f), 25f * Deg));

        Pose back = Retarget(avatar, pose, avatar);

        Float3 Arm(Pose p) => ModelPos(avatar, p, HumanBodyBone.LeftLowerArm) - ModelPos(avatar, p, HumanBodyBone.LeftUpperArm);
        Assert.True(AngleDeg(Arm(pose), Arm(back)) < 0.5f);
        Assert.True(Float3.Distance(ModelPos(avatar, pose, HumanBodyBone.LeftHand), ModelPos(avatar, back, HumanBodyBone.LeftHand)) < 0.01f);
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

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(180f)]
    public void NearlyStraightArm_KeepsTheUpperArmRoll(float directionDegrees)
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose rolled = BindPose(avatar);
        RotateLocal(avatar, rolled, HumanBodyBone.LeftUpperArm, Quaternion.AxisAngle(new Float3(-1f, 0f, 0f), 40f * Deg));
        float straight = Encode(avatar, rolled).GetMuscle(HumanBodyBone.LeftUpperArm, MuscleAxis.X);

        float a = directionDegrees * Deg;
        RotateLocal(avatar, rolled, HumanBodyBone.LeftLowerArm, Quaternion.AxisAngle(new Float3(0f, MathF.Cos(a), MathF.Sin(a)), 0.05f * Deg));
        float bent = Encode(avatar, rolled).GetMuscle(HumanBodyBone.LeftUpperArm, MuscleAxis.X);

        Assert.True(MathF.Abs(bent - straight) < 0.01f, $"roll went from {straight:N3} to {bent:N3}");
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

    [Theory]
    [InlineData(10f)]
    [InlineData(30f)]
    public void Mirror_OnARigTurnedOffAxis_IsAReflection(float yawDegrees)
    {
        Quaternion yaw = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), yawDegrees * Deg);
        Avatar avatar = new HumanoidTestRig { ArmatureRotation = yaw }.BuildAvatar();
        Float3 normal = yaw * new Float3(1f, 0f, 0f);
        Pose pose = BindPose(avatar);
        RotateLocal(avatar, pose, HumanBodyBone.Spine, Quaternion.AxisAngle(yaw * new Float3(0f, 0f, 1f), 0.5f));
        RotateLocal(avatar, pose, HumanBodyBone.LeftUpperArm, Quaternion.AxisAngle(yaw * new Float3(0f, 0f, 1f), -0.6f));

        var mirrored = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, pose, mirrored);
        mirrored.CalculateModelSpaceTransforms();

        Float3 Reflect(Float3 v) => v - normal * (2f * Float3.Dot(v, normal));
        foreach (HumanBodyBone bone in Enum.GetValues<HumanBodyBone>())
        {
            HumanBodyBone other = HumanTrait.GetMirrorBone(bone);
            if (!avatar.Humanoid!.HasBone(bone) || !avatar.Humanoid.HasBone(other))
                continue;
            float miss = Float3.Distance(Reflect(ModelPos(avatar, pose, other)), ModelPos(avatar, mirrored, bone));
            Assert.True(miss < 0.01f, $"{bone} {miss:N4}");
        }
    }

    [Fact]
    public void MirrorInPlace_ReflectsAMovedRootOnce()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose pose = BindPose(avatar);
        pose.SetTransform(0, new Transform3D(new Float3(0.3f, 0f, 0.2f), Quaternion.Identity, Float3.One));
        pose.CalculateModelSpaceTransforms();
        Float3 hips = ModelPos(avatar, pose, HumanBodyBone.Hips);

        var copy = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, pose, copy);
        PoseMirror.Apply(avatar, pose, pose);
        pose.CalculateModelSpaceTransforms();
        copy.CalculateModelSpaceTransforms();

        Assert.True(Float3.Distance(ModelPos(avatar, copy, HumanBodyBone.Hips), ModelPos(avatar, pose, HumanBodyBone.Hips)) < 1e-4f);
        Assert.True(Float3.Distance(new Float3(-hips.X, hips.Y, hips.Z), ModelPos(avatar, copy, HumanBodyBone.Hips)) < 1e-3f);
    }
}
