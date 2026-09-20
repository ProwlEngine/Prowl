using Prowl.Vector;
using Prowl.Vector.Spatial;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

/// <summary>The muscle space encoding: limits, neutral pose, range overrides, missing bones and mirroring.</summary>
public class HumanoidMuscleTests
{
    private static readonly Float3 Left = new(1f, 0f, 0f);
    private static readonly Float3 Forward = new(0f, 0f, 1f);
    private const float Deg = MathF.PI / 180f;

    private static HumanPose Encode(Avatar avatar, Pose pose)
    {
        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, pose, human);
        return human;
    }

    private static int Muscle(HumanBodyBone bone, MuscleAxis axis) => HumanTrait.GetMuscleIndex(bone, axis);

    [Fact]
    public void MuscleTable_IsSymmetricAndContainsZero()
    {
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
        {
            Assert.InRange(HumanTrait.GetMuscleDefaultMin(i), -360f, 0f);
            Assert.InRange(HumanTrait.GetMuscleDefaultMax(i), 0f, 360f);

            int mirror = HumanTrait.GetMirrorMuscle(i);
            Assert.Equal(i, HumanTrait.GetMirrorMuscle(mirror));
            Assert.Equal(HumanTrait.GetMirrorBone(HumanTrait.GetMuscleBone(i)), HumanTrait.GetMuscleBone(mirror));
            float sign = HumanTrait.MuscleFlipsWhenMirrored(i) ? -1f : 1f;
            (float min, float max) = sign > 0f
                ? (HumanTrait.GetMuscleDefaultMin(mirror), HumanTrait.GetMuscleDefaultMax(mirror))
                : (-HumanTrait.GetMuscleDefaultMax(mirror), -HumanTrait.GetMuscleDefaultMin(mirror));
            Assert.Equal(HumanTrait.GetMuscleDefaultMin(i), min);
            Assert.Equal(HumanTrait.GetMuscleDefaultMax(i), max);
        }
    }

    [Fact]
    public void RotationBeyondTheLimit_EncodesPastOneAndDecodesBack()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose pose = BindPose(avatar);
        RotateLocal(avatar, pose, HumanBodyBone.Head, Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 60f * Deg));

        HumanPose human = Encode(avatar, pose);
        int turn = Muscle(HumanBodyBone.Head, MuscleAxis.X);
        Assert.Equal(60.0 / HumanTrait.GetMuscleDefaultMax(turn), (double)MathF.Abs(human.GetMuscle(turn)), 3);

        var result = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, result);
        result.CalculateModelSpaceTransforms();
        Assert.True(AngleDeg(ModelRot(avatar, pose, HumanBodyBone.Head), ModelRot(avatar, result, HumanBodyBone.Head)) < 0.1f);
    }

    [Fact]
    public void TPoseRest_SitsAboveAndBehindTheZeroPose()
    {
        Avatar tPose = new HumanoidTestRig().BuildAvatar();
        HumanPose human = Encode(tPose, BindPose(tPose));

        Assert.Equal(0.4, (double)human.GetMuscle(Muscle(HumanBodyBone.LeftUpperArm, MuscleAxis.Z)), 2);
        Assert.Equal(0.3, (double)human.GetMuscle(Muscle(HumanBodyBone.LeftUpperArm, MuscleAxis.Y)), 2);
        Assert.Equal(1.0, (double)human.GetMuscle(Muscle(HumanBodyBone.LeftLowerArm, MuscleAxis.Z)), 2);
        Assert.Equal(1.0, (double)human.GetMuscle(Muscle(HumanBodyBone.LeftLowerLeg, MuscleAxis.Z)), 2);
    }

    [Fact]
    public void TPose_RetargetsAsATPoseOntoAnAPoseRig()
    {
        Avatar tPose = new HumanoidTestRig().BuildAvatar();
        Avatar aPose = new HumanoidTestRig { ArmDownDegrees = 45f }.BuildAvatar();

        Pose result = Retarget(tPose, BindPose(tPose), aPose);

        Float3 arm = ModelPos(aPose, result, HumanBodyBone.LeftHand) - ModelPos(aPose, result, HumanBodyBone.LeftUpperArm);
        Assert.True(AngleDeg(arm, -Left) < 1f, $"arm is {AngleDeg(arm, -Left):N1} degrees off horizontal");
    }

    [Fact]
    public void ZeroMuscles_GiveTheSameArmOnTPoseAndAPoseRigs()
    {
        Float3 ZeroArm(Avatar avatar)
        {
            var result = new Pose(avatar.Skeleton);
            Retargeter.RetargetTo(avatar, new HumanPose(), result);
            result.CalculateModelSpaceTransforms();
            return ModelPos(avatar, result, HumanBodyBone.LeftLowerArm) - ModelPos(avatar, result, HumanBodyBone.LeftUpperArm);
        }

        Float3 t = ZeroArm(new HumanoidTestRig().BuildAvatar());
        Float3 a = ZeroArm(new HumanoidTestRig { ArmDownDegrees = 45f }.BuildAvatar());
        Assert.True(AngleDeg(t, a) < 1f, $"zero arms differ by {AngleDeg(t, a):N1} degrees");
    }

    [Fact]
    public void MuscleRangeOverride_ScalesTheDecodedAngle()
    {
        int armDownUp = Muscle(HumanBodyBone.LeftUpperArm, MuscleAxis.Z);
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig().Build();
        description.SetMuscleRange(armDownUp, -50f, 55f);
        Avatar overridden = AvatarBuilder.BuildHumanoid(skeleton, description);
        Avatar standard = new HumanoidTestRig().BuildAvatar();

        float Raise(Avatar avatar)
        {
            Float3 Arm(float value)
            {
                var human = new HumanPose();
                human.SetMuscle(armDownUp, value);
                var result = new Pose(avatar.Skeleton);
                Retargeter.RetargetTo(avatar, human, result);
                result.CalculateModelSpaceTransforms();
                return ModelPos(avatar, result, HumanBodyBone.LeftLowerArm) - ModelPos(avatar, result, HumanBodyBone.LeftUpperArm);
            }
            return AngleDeg(Arm(0f), Arm(0.5f));
        }

        Assert.InRange(Raise(standard), 0.5f * HumanTrait.GetMuscleDefaultMax(armDownUp) - 0.5f, 0.5f * HumanTrait.GetMuscleDefaultMax(armDownUp) + 0.5f);
        Assert.InRange(Raise(overridden), 27.5f - 0.5f, 27.5f + 0.5f);
    }

    [Fact]
    public void SpineBendOnRigWithoutChest_GoesToTheSpineMuscle()
    {
        Avatar source = new HumanoidTestRig { Chest = false, UpperChest = false }.BuildAvatar();
        Avatar target = new HumanoidTestRig().BuildAvatar();

        Pose pose = BindPose(source);
        RotateLocal(source, pose, HumanBodyBone.Spine, Quaternion.AxisAngle(Left, 30f * Deg));
        HumanPose human = Encode(source, pose);

        int spine = Muscle(HumanBodyBone.Spine, MuscleAxis.Z);
        Assert.Equal(30.0 / HumanTrait.GetMuscleDefaultMax(spine), (double)MathF.Abs(human.GetMuscle(spine)), 3);
        Assert.Equal(0.0, (double)human.GetMuscle(Muscle(HumanBodyBone.Chest, MuscleAxis.Z)), 4);

        var result = new Pose(target.Skeleton);
        Retargeter.RetargetTo(target, human, result);
        result.CalculateModelSpaceTransforms();
        float error = AngleDeg(ModelRot(source, pose, HumanBodyBone.Head), ModelRot(target, result, HumanBodyBone.Head));
        Assert.True(error < 1f, $"head differs by {error:N1} degrees");
    }

    [Fact]
    public void MirrorHumanPose_SwapsSidesAndFlipsCentreLateralMuscles()
    {
        var pose = new HumanPose();
        pose.SetMuscle(Muscle(HumanBodyBone.LeftUpperArm, MuscleAxis.Z), 0.5f);
        pose.SetMuscle(Muscle(HumanBodyBone.LeftUpperArm, MuscleAxis.X), 0.3f);
        pose.SetMuscle(Muscle(HumanBodyBone.Spine, MuscleAxis.X), 0.2f);
        pose.SetMuscle(Muscle(HumanBodyBone.Spine, MuscleAxis.Z), 0.4f);
        pose.BodyPosition = new Float3(0.3f, 0.1f, 0.2f);

        var mirrored = new HumanPose();
        PoseMirror.Mirror(pose, mirrored);

        Assert.Equal(0.5f, mirrored.GetMuscle(HumanBodyBone.RightUpperArm, MuscleAxis.Z));
        Assert.Equal(0.3f, mirrored.GetMuscle(HumanBodyBone.RightUpperArm, MuscleAxis.X));
        Assert.Equal(0f, mirrored.GetMuscle(HumanBodyBone.LeftUpperArm, MuscleAxis.Z));
        Assert.Equal(-0.2f, mirrored.GetMuscle(HumanBodyBone.Spine, MuscleAxis.X));
        Assert.Equal(0.4f, mirrored.GetMuscle(HumanBodyBone.Spine, MuscleAxis.Z));
        Assert.Equal(-0.3f, mirrored.BodyPosition.X);
    }

    [Fact]
    public void MirroredGoalRotation_MatchesTheMirroredFoot()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose pose = BindPose(avatar);
        RotateLocal(avatar, pose, HumanBodyBone.LeftFoot, Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.5f) * Quaternion.AxisAngle(Left, 0.3f));

        var mirrored = new HumanPose();
        PoseMirror.Mirror(Encode(avatar, pose), mirrored);

        var mirroredPose = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, pose, mirroredPose);
        HumanPose reencoded = Encode(avatar, mirroredPose);

        Quaternion a = mirrored.GetGoal(HumanGoal.RightFoot).Transform.rotation;
        Quaternion b = reencoded.GetGoal(HumanGoal.RightFoot).Transform.rotation;
        Assert.True(AngleDeg(a, b) < 1f, $"mirrored goal rotation is {AngleDeg(a, b):N1} degrees off");
    }
}
