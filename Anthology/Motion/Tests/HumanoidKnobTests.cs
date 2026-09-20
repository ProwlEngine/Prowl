using Prowl.Vector;
using Prowl.Vector.Spatial;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

/// <summary>The HumanDescription tuning knobs: twist distribution, feet spacing and limb stretch.</summary>
public class HumanoidKnobTests
{
    private static readonly Float3 Left = new(1f, 0f, 0f);
    private static readonly Float3 Outward = new(-1f, 0f, 0f);
    private static readonly Float3 Up = new(0f, 1f, 0f);
    private const float Deg = MathF.PI / 180f;

    private static Avatar Target(Action<HumanDescription> tune)
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig().Build();
        tune(description);
        return AvatarBuilder.BuildHumanoid(skeleton, description);
    }

    // A source whose upper bones take none of the lower twist, so an untwisted upper arm or leg is exactly what it encodes.
    private static Avatar Source() => Target(d => { d.UpperArmTwist = 0f; d.UpperLegTwist = 0f; });

    private static float TwistFromBind(Avatar avatar, Pose pose, HumanBodyBone bone)
        => AngleDeg(avatar.Skeleton.GetBoneModelSpaceTransform(Index(avatar, bone)).rotation, ModelRot(avatar, pose, bone));

    [Fact]
    public void LowerArmTwist_IsTheShareOfHandTwistTakenByTheForearm()
    {
        Avatar source = Source();
        Avatar target = Target(d => { d.LowerArmTwist = 0.3f; d.UpperArmTwist = 0f; });

        Pose pose = BindPose(source);
        RotateLocal(source, pose, HumanBodyBone.LeftHand, Quaternion.AxisAngle(Left, 60f * Deg));
        Pose result = Retarget(source, pose, target);

        Assert.InRange(TwistFromBind(target, result, HumanBodyBone.LeftLowerArm), 17f, 19f);
        Assert.InRange(TwistFromBind(target, result, HumanBodyBone.LeftHand), 59f, 61f);
        Assert.True(TwistFromBind(target, result, HumanBodyBone.LeftUpperArm) < 1f);
    }

    // How far each bone turns when the upper bone's twist muscle goes from zero to the given value.
    private static float TwistTurn(Avatar avatar, HumanBodyBone muscleBone, float value, HumanBodyBone bone)
    {
        Pose Decode(float v)
        {
            var human = new HumanPose();
            human.SetMuscle(HumanTrait.GetMuscleIndex(muscleBone, MuscleAxis.X), v);
            var pose = new Pose(avatar.Skeleton);
            Retargeter.RetargetTo(avatar, human, pose);
            pose.CalculateModelSpaceTransforms();
            return pose;
        }
        return AngleDeg(ModelRot(avatar, Decode(0f), bone), ModelRot(avatar, Decode(value), bone));
    }

    [Fact]
    public void UpperArmTwist_IsTheShareOfUpperArmTwistShownOnTheUpperArm()
    {
        Avatar target = Target(d => d.UpperArmTwist = 0.4f);
        int twist = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftUpperArm, MuscleAxis.X);
        float full = 0.5f * HumanTrait.GetMuscleDefaultMax(twist);

        Assert.InRange(TwistTurn(target, HumanBodyBone.LeftUpperArm, 0.5f, HumanBodyBone.LeftUpperArm), 0.4f * full - 0.5f, 0.4f * full + 0.5f);
        Assert.InRange(TwistTurn(target, HumanBodyBone.LeftUpperArm, 0.5f, HumanBodyBone.LeftLowerArm), full - 0.5f, full + 0.5f);
        Assert.InRange(TwistTurn(target, HumanBodyBone.LeftUpperArm, 0.5f, HumanBodyBone.LeftHand), full - 0.5f, full + 0.5f);
    }

    [Fact]
    public void LowerLegTwist_IsTheShareOfFootTwistTakenByTheShin()
    {
        Avatar source = Source();
        Avatar target = Target(d => d.LowerLegTwist = 0.25f);

        Pose pose = BindPose(source);
        RotateLocal(source, pose, HumanBodyBone.LeftFoot, Quaternion.AxisAngle(Up, 40f * Deg));
        Pose result = Retarget(source, pose, target);

        Assert.InRange(TwistFromBind(target, result, HumanBodyBone.LeftLowerLeg), 9f, 11f);
        Assert.InRange(TwistFromBind(target, result, HumanBodyBone.LeftFoot), 39f, 41f);
    }

    [Fact]
    public void UpperLegTwist_IsTheShareOfUpperLegTwistShownOnTheThigh()
    {
        Avatar target = Target(d => d.UpperLegTwist = 0.5f);
        int twist = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftUpperLeg, MuscleAxis.X);
        float full = 0.5f * HumanTrait.GetMuscleDefaultMax(twist);

        Assert.InRange(TwistTurn(target, HumanBodyBone.LeftUpperLeg, 0.5f, HumanBodyBone.LeftUpperLeg), 0.5f * full - 0.5f, 0.5f * full + 0.5f);
        Assert.InRange(TwistTurn(target, HumanBodyBone.LeftUpperLeg, 0.5f, HumanBodyBone.LeftLowerLeg), full - 0.5f, full + 0.5f);
        Assert.InRange(TwistTurn(target, HumanBodyBone.LeftUpperLeg, 0.5f, HumanBodyBone.LeftFoot), full - 0.5f, full + 0.5f);
    }

    private static float LegLength(Avatar avatar)
    {
        Float3 Bind(HumanBodyBone bone) => avatar.Skeleton.GetBoneModelSpaceTransform(Index(avatar, bone)).position;
        return Float3.Distance(Bind(HumanBodyBone.LeftUpperLeg), Bind(HumanBodyBone.LeftLowerLeg)) + Float3.Distance(Bind(HumanBodyBone.LeftLowerLeg), Bind(HumanBodyBone.LeftFoot));
    }

    [Fact]
    public void FeetSpacing_WidensTheStance()
    {
        Avatar source = new HumanoidTestRig().BuildAvatar();
        Avatar narrow = Target(_ => { });
        Avatar wide = Target(d => d.FeetSpacing = 0.2f);

        float Stance(Avatar avatar)
        {
            Pose result = Retarget(source, BindPose(source), avatar);
            return ModelPos(avatar, result, HumanBodyBone.RightFoot).X - ModelPos(avatar, result, HumanBodyBone.LeftFoot).X;
        }

        float widened = Stance(wide) - Stance(narrow);
        Assert.InRange(widened, 0.2f * LegLength(wide) - 0.02f, 0.2f * LegLength(wide) + 0.02f);
    }

    private static float GoalMiss(Avatar avatar, HumanGoal goal, Float3 push)
    {
        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, BindPose(avatar), human);
        HumanGoalState state = human.GetGoal(goal);
        state.Transform = new Transform3D(state.Transform.position + push, state.Transform.rotation, Float3.One);
        state.PositionWeight = 1f;
        human.SetGoal(goal, state);

        var result = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, result);
        result.CalculateModelSpaceTransforms();

        Float3 hipCenter = (ModelPos(avatar, result, HumanBodyBone.LeftUpperLeg) + ModelPos(avatar, result, HumanBodyBone.RightUpperLeg)) * 0.5f;
        Float3 desired = hipCenter + state.Transform.position * LegLength(avatar);
        return Float3.Distance(desired, ModelPos(avatar, result, HumanTrait.GetGoalEndBone(goal)));
    }

    [Fact]
    public void ArmStretch_LetsTheHandReachAFarGoal()
    {
        Float3 push = Outward * 0.08f;
        Assert.True(GoalMiss(Target(d => d.ArmStretch = 0f), HumanGoal.LeftHand, push) > 0.05f);
        Assert.True(GoalMiss(Target(d => d.ArmStretch = 0.3f), HumanGoal.LeftHand, push) < 0.01f);
    }

    [Fact]
    public void LegStretch_LetsTheFootReachAFarGoal()
    {
        var push = new Float3(0f, -0.08f, 0f);
        Assert.True(GoalMiss(Target(d => d.LegStretch = 0f), HumanGoal.LeftFoot, push) > 0.05f);
        Assert.True(GoalMiss(Target(d => d.LegStretch = 0.3f), HumanGoal.LeftFoot, push) < 0.01f);
    }
}
