using Prowl.Vector;
using Prowl.Vector.Spatial;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

/// <summary>Regression tests for the humanoid retargeting, blending and mirroring findings.</summary>
public class HumanoidRegressionTests
{
    private static readonly Float3 Up = new(0f, 1f, 0f);

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

    private static IEnumerable<HumanBodyBone> AllMapped(Avatar avatar)
        => Enum.GetValues<HumanBodyBone>().Where(avatar.Humanoid!.HasBone);

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
    public void Blend_KeepsGoalPole()
    {
        var a = new HumanPose();
        a.SetGoal(HumanGoal.LeftFoot, new HumanGoalState { PositionWeight = 1f, Pole = new Float3(0f, 0.5f, 0.3f), HasPole = true });
        var result = new HumanPose();

        HumanPoseBlender.Blend(result, a, a, 0.5f);

        HumanGoalState goal = result.GetGoal(HumanGoal.LeftFoot);
        Assert.True(goal.HasPole);
        Assert.Equal(0.3, (double)goal.Pole.Z, 4);
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

    [Fact]
    public void AddLayerOfSubtract_ReproducesTheOriginalPose()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        HumanPose a = Encode(avatar, p =>
        {
            RotateLocal(avatar, p, HumanBodyBone.LeftUpperArm, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.8f));
            RotateLocal(avatar, p, HumanBodyBone.Spine, Quaternion.AxisAngle(Up, 0.3f));
            int hips = Index(avatar, HumanBodyBone.Hips);
            Transform3D t = p.GetTransform(hips);
            p.SetTransform(hips, new Transform3D(t.position + new Float3(0.2f, 0f, 0.4f), Quaternion.AxisAngle(Up, 0.5f), t.scale));
        });
        a.LookAtPosition = new Float3(1f, 2f, 3f);
        a.LookAtHeadWeight = 0.7f;
        HumanPose b = Encode(avatar, p =>
        {
            RotateLocal(avatar, p, HumanBodyBone.LeftUpperArm, Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.6f));
            RotateLocal(avatar, p, HumanBodyBone.Spine, Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f));
        });

        var delta = new HumanPose();
        HumanPoseBlender.Subtract(delta, a, b);
        var rebuilt = new HumanPose();
        rebuilt.CopyFrom(b);
        HumanPoseBlender.AddLayer(rebuilt, delta, 1f);

        Pose expected = Decode(avatar, a);
        Pose actual = Decode(avatar, rebuilt);
        for (int i = 0; i < avatar.Skeleton.BoneCount; i++)
        {
            Assert.True(AngleDeg(expected.GetModelSpaceTransform(i).rotation, actual.GetModelSpaceTransform(i).rotation) < 0.5f, $"bone {i} rotation differs");
            Assert.True(Float3.Distance(expected.GetModelSpaceTransform(i).position, actual.GetModelSpaceTransform(i).position) < 1e-3f, $"bone {i} position differs");
        }
        Assert.True(Float3.Distance(a.LookAtPosition, rebuilt.LookAtPosition) < 1e-4f);
        Assert.Equal((double)a.LookAtHeadWeight, (double)rebuilt.LookAtHeadWeight, 4);
        Float3 goalA = a.GetGoal(HumanGoal.LeftHand).Transform.position;
        Float3 goalR = rebuilt.GetGoal(HumanGoal.LeftHand).Transform.position;
        Assert.True(Float3.Distance(goalA, goalR) < 1e-4f, "hand goal was not layered");
    }

    [Fact]
    public void Blend_BlendsLookAt()
    {
        var a = new HumanPose { LookAtPosition = new Float3(0f, 0f, 0f), LookAtHeadWeight = 0f };
        var b = new HumanPose { LookAtPosition = new Float3(2f, 0f, 0f), LookAtHeadWeight = 1f };
        var result = new HumanPose { LookAtPosition = new Float3(9f, 9f, 9f) };

        HumanPoseBlender.Blend(result, a, b, 0.5f);

        Assert.Equal(1.0, (double)result.LookAtPosition.X, 4);
        Assert.Equal(0.5, (double)result.LookAtHeadWeight, 4);
    }

    [Fact]
    public void Mirror_ReflectsHipsTranslation()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose source = BindPose(avatar);
        int hips = Index(avatar, HumanBodyBone.Hips);
        Transform3D t = source.GetTransform(hips);
        source.SetTransform(hips, new Transform3D(t.position + new Float3(0.3f, 0f, 0f), t.rotation, t.scale));

        var mirrored = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, source, mirrored);
        mirrored.CalculateModelSpaceTransforms();

        Assert.Equal(-0.3, (double)ModelPos(avatar, mirrored, HumanBodyBone.Hips).X, 3);
    }

    [Fact]
    public void Mirror_OfTwistedSpine_IsATrueReflection()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose source = BindPose(avatar);
        RotateLocal(avatar, source, HumanBodyBone.Spine, Quaternion.AxisAngle(Up, 34f * MathF.PI / 180f));
        RotateLocal(avatar, source, HumanBodyBone.LeftUpperArm, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.7f));

        var mirrored = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, source, mirrored);
        mirrored.CalculateModelSpaceTransforms();

        Float3 left = ModelPos(avatar, source, HumanBodyBone.LeftLowerArm) - ModelPos(avatar, source, HumanBodyBone.LeftUpperArm);
        Float3 right = ModelPos(avatar, mirrored, HumanBodyBone.RightLowerArm) - ModelPos(avatar, mirrored, HumanBodyBone.RightUpperArm);
        float error = AngleDeg(new Float3(-left.X, left.Y, left.Z), right);
        Assert.True(error < 2f, $"mirrored right arm is {error:N1} degrees off the reflection");

        Float3 hand = ModelPos(avatar, source, HumanBodyBone.LeftHand);
        Float3 mirroredHand = ModelPos(avatar, mirrored, HumanBodyBone.RightHand);
        Assert.True(Float3.Distance(new Float3(-hand.X, hand.Y, hand.Z), mirroredHand) < 0.01f);
    }

    private static Skeleton Named(params (string Name, int Parent)[] bones)
    {
        var ids = bones.Select(b => new StringID(b.Name)).ToArray();
        var parents = bones.Select(b => b.Parent).ToArray();
        var pose = bones.Select(_ => new Transform3D(Float3.Zero, Quaternion.Identity, Float3.One)).ToArray();
        return new Skeleton(ids, parents, pose);
    }

    private static Skeleton PrefixedRig(string prefix) => Named(
        (prefix + "Hips", -1), (prefix + "Spine", 0), (prefix + "Head", 1),
        (prefix + "LeftArm", 1), (prefix + "LeftForeArm", 3), (prefix + "LeftHand", 4),
        (prefix + "RightArm", 1), (prefix + "RightForeArm", 6), (prefix + "RightHand", 7),
        (prefix + "LeftUpLeg", 0), (prefix + "LeftLeg", 9), (prefix + "LeftFoot", 10),
        (prefix + "RightUpLeg", 0), (prefix + "RightLeg", 12), (prefix + "RightFoot", 13));

    [Theory]
    [InlineData("Character1_")]
    [InlineData("mixamorig_")]
    public void AutoMapper_PeelsCamelCaseSideAfterAPrefixToken(string prefix)
    {
        HumanoidMapResult result = HumanoidAutoMapper.Map(PrefixedRig(prefix));
        Assert.True(result.IsHumanoid);
        Assert.Equal(9, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg));
        Assert.Equal(12, result.Description.GetSkeletonBoneIndex(HumanBodyBone.RightUpperLeg));
    }

    [Fact]
    public void AutoMapper_MapsUnrealMiddleFinger()
    {
        Skeleton skeleton = Named(
            ("pelvis", -1), ("spine_01", 0), ("head", 1),
            ("upperarm_l", 1), ("lowerarm_l", 3), ("hand_l", 4),
            ("upperarm_r", 1), ("lowerarm_r", 6), ("hand_r", 7),
            ("thigh_l", 0), ("calf_l", 9), ("foot_l", 10),
            ("thigh_r", 0), ("calf_r", 12), ("foot_r", 13),
            ("middle_01_l", 5), ("middle_02_l", 15), ("middle_03_l", 16));

        HumanoidMapResult result = HumanoidAutoMapper.Map(skeleton);

        Assert.True(result.IsHumanoid);
        Assert.Equal(15, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftMiddleProximal));
        Assert.Equal(17, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftMiddleDistal));
    }

    [Fact]
    public void UnmappedBones_ReportsRequiredBoneBehindMissingOptionalParent()
    {
        Skeleton skeleton = Named(
            ("Hips", -1), ("Spine", 0), ("Head", 1),
            ("Bone_A", 1), ("LeftForeArm", 3), ("LeftHand", 4),
            ("RightArm", 1), ("RightForeArm", 6), ("RightHand", 7),
            ("LeftUpLeg", 0), ("LeftLeg", 9), ("LeftFoot", 10),
            ("RightUpLeg", 0), ("RightLeg", 12), ("RightFoot", 13));

        HumanoidMapResult result = HumanoidAutoMapper.Map(skeleton);

        Assert.False(result.IsHumanoid);
        Assert.Contains(HumanBodyBone.LeftUpperArm, result.UnmappedBones);
    }

    [Fact]
    public void BuildHumanoid_WithoutRequiredBones_Throws()
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig().Build();
        description.SetSkeletonBoneIndex(HumanBodyBone.Hips, Skeleton.InvalidIndex);

        Assert.Throws<ArgumentException>(() => AvatarBuilder.BuildHumanoid(skeleton, description));
    }

    [Fact]
    public void BuildHumanoid_WithOutOfRangeBoneIndex_Throws()
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig().Build();
        description.SetSkeletonBoneIndex(HumanBodyBone.Jaw, skeleton.BoneCount + 5);

        Assert.Throws<ArgumentException>(() => AvatarBuilder.BuildHumanoid(skeleton, description));
    }

    [Fact]
    public void HumanoidRig_IsNotAffectedByLaterDescriptionEdits()
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig().Build();
        Avatar avatar = AvatarBuilder.BuildHumanoid(skeleton, description);
        int hips = description.GetSkeletonBoneIndex(HumanBodyBone.Hips);

        description.SetSkeletonBoneIndex(HumanBodyBone.Hips, Skeleton.InvalidIndex);
        description.LegStretch = 0.9f;

        Assert.Equal(hips, avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips));
        Assert.True(avatar.IsValid);
        Assert.NotEqual(0.9f, avatar.Humanoid!.Description.LegStretch);
    }

    [Fact]
    public void AutoMapper_MapsBipedToesAndFingers()
    {
        Skeleton skeleton = Named(
            ("Bip001 Pelvis", -1), ("Bip001 Spine", 0), ("Bip001 Head", 1),
            ("Bip001 L UpperArm", 1), ("Bip001 L Forearm", 3), ("Bip001 L Hand", 4),
            ("Bip001 R UpperArm", 1), ("Bip001 R Forearm", 6), ("Bip001 R Hand", 7),
            ("Bip001 L Thigh", 0), ("Bip001 L Calf", 9), ("Bip001 L Foot", 10),
            ("Bip001 R Thigh", 0), ("Bip001 R Calf", 12), ("Bip001 R Foot", 13),
            ("Bip001 L Toe0", 11),
            ("Bip001 L Finger0", 5), ("Bip001 L Finger01", 16), ("Bip001 L Finger02", 17),
            ("Bip001 L Finger1", 5), ("Bip001 L Finger11", 19),
            ("Bip001 L Finger4", 5));

        HumanoidMapResult result = HumanoidAutoMapper.Map(skeleton);

        Assert.True(result.IsHumanoid);
        Assert.Equal(15, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftToes));
        Assert.Equal(16, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftThumbProximal));
        Assert.Equal(17, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftThumbIntermediate));
        Assert.Equal(18, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftThumbDistal));
        Assert.Equal(19, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexProximal));
        Assert.Equal(20, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexIntermediate));
        Assert.Equal(21, result.Description.GetSkeletonBoneIndex(HumanBodyBone.LeftLittleProximal));
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
}
