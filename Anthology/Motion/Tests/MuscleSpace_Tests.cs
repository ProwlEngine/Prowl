using Prowl.Clay.Importer;
using Prowl.Clay;
using Prowl.Vector.Spatial;
using Prowl.Vector;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

/// <summary>Muscle space: encoding a humanoid pose as muscles and decoding it back, with the twist and stretch knobs.</summary>
public class MuscleSpace_Tests
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

    private static readonly int SpineTwist = HumanTrait.GetMuscleIndex(HumanBodyBone.Spine, MuscleAxis.X);

    private static readonly Float3 Left = new(1f, 0f, 0f);

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

    private static readonly Float3 Up = new(0f, 1f, 0f);

    private static float LegLength(Avatar avatar)
    {
        Float3 Bind(HumanBodyBone bone) => avatar.Skeleton.GetBoneModelSpaceTransform(Index(avatar, bone)).position;
        return Float3.Distance(Bind(HumanBodyBone.LeftUpperLeg), Bind(HumanBodyBone.LeftLowerLeg)) + Float3.Distance(Bind(HumanBodyBone.LeftLowerLeg), Bind(HumanBodyBone.LeftFoot));
    }

    private static readonly Float3 Outward = new(-1f, 0f, 0f);

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

    private static int Muscle(HumanBodyBone bone, MuscleAxis axis) => HumanTrait.GetMuscleIndex(bone, axis);

    private static Avatar Mixamo()
        => AvatarBuilder.BuildAutomatic(ClayModelLoader.BuildSkeleton(ModelImporter.Load(TestAssets.RumbaDancing)));

    private static readonly Float3 X = new(1f, 0f, 0f);

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

    private static (float Rotation, float Position) WorstDifference(Pose a, Pose b)
    {
        a.CalculateModelSpaceTransforms();
        b.CalculateModelSpaceTransforms();
        float rotation = 0f, position = 0f;
        for (int i = 0; i < a.BoneCount; i++)
        {
            rotation = MathF.Max(rotation, AngleDeg(a.GetModelSpaceTransform(i).rotation, b.GetModelSpaceTransform(i).rotation));
            position = MathF.Max(position, Float3.Distance(a.GetModelSpaceTransform(i).position, b.GetModelSpaceTransform(i).position));
        }
        return (rotation, position);
    }

    private static HumanPose RandomHuman(Random rng, float amplitude, bool body)
    {
        var human = new HumanPose();
        for (int m = 0; m < HumanTrait.MuscleCount; m++)
            human.SetMuscle(m, (float)(rng.NextDouble() * 2.0 - 1.0) * amplitude);
        if (body)
        {
            human.BodyRotation = Quaternion.Normalize(new Quaternion((float)rng.NextDouble() * 0.3f, (float)rng.NextDouble() * 0.3f, (float)rng.NextDouble() * 0.3f, 1f));
            human.BodyPosition = new Float3((float)rng.NextDouble() * 0.2f, (float)rng.NextDouble() * 0.2f, (float)rng.NextDouble() * 0.2f);
        }
        return human;
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
    public void NewPose_IsTheNeutralPose()
    {
        var pose = new HumanPose();
        Assert.All(pose.Muscles.ToArray(), m => Assert.Equal(0f, m));
        Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Identity, pose.BodyRotation)) > 0.999f);
    }

    [Fact]
    public void Reset_ClearsMusclesAndStandsTheBodyUp()
    {
        var pose = new HumanPose();
        pose.SetMuscle(SpineTwist, 0.9f);
        pose.RootTransform = new Transform3D(new Float3(1f, 2f, 3f), Quaternion.Identity, Float3.One);
        pose.Reset();
        Assert.Equal(0f, pose.GetMuscle(SpineTwist));
        Assert.Equal(1.0, (double)pose.RootTransform.position.Y, 5);
        Assert.Equal(0.0, (double)pose.RootTransform.position.Z, 5);
    }

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

    [ModelFact]
    public void RandomMuscles_DecodeAndEncodeBackExactly_OnARealRig()
    {
        Avatar avatar = Mixamo();
        var rng = new Random(3);
        var decoded = new Pose(avatar.Skeleton);
        var encoded = new HumanPose();

        for (int trial = 0; trial < 40; trial++)
        {
            var human = new HumanPose();
            for (int m = 0; m < HumanTrait.MuscleCount; m++)
                if (avatar.Humanoid!.HasBone(HumanTrait.GetMuscleBone(m)))
                    human.SetMuscle(m, (float)(rng.NextDouble() * 1.6 - 0.8));
            human.BodyRotation = Quaternion.Normalize(Quaternion.AxisAngle(Float3.Normalize(new Float3(0.3f, 1f, -0.2f)), (float)rng.NextDouble()));
            human.BodyPosition = new Float3(0.1f, 0.9f, -0.2f);

            Retargeter.RetargetTo(avatar, human, decoded);
            Retargeter.RetargetFrom(avatar, decoded, encoded);

            for (int m = 0; m < HumanTrait.MuscleCount; m++)
                Assert.True(MathF.Abs(human.GetMuscle(m) - encoded.GetMuscle(m)) < 2e-3f, $"trial {trial}: {HumanTrait.GetMuscleName(m)} went {human.GetMuscle(m):N4} to {encoded.GetMuscle(m):N4}");
            Assert.True(Float3.Distance(human.BodyPosition, encoded.BodyPosition) < 1e-3f);
            Assert.True(AngleDeg(human.BodyRotation, encoded.BodyRotation) < 0.1f);
        }
    }

    [Fact]
    public void CurledFingerRest_IsStraightenedForTheTPose()
    {
        Avatar straight = new HumanoidTestRig { Fingers = true }.BuildAvatar();
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig { Fingers = true }.Build();
        int intermediate = description.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexIntermediate);
        Transform3D bind = skeleton.GetBoneParentSpaceTransform(intermediate);
        var local = new Transform3D[skeleton.BoneCount];
        for (int i = 0; i < local.Length; i++)
            local[i] = skeleton.GetBoneParentSpaceTransform(i);
        local[intermediate] = new Transform3D(bind.position, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.6f), bind.scale);
        var ids = Enumerable.Range(0, skeleton.BoneCount).Select(skeleton.GetBoneID).ToArray();
        var parents = Enumerable.Range(0, skeleton.BoneCount).Select(skeleton.GetParentBoneIndex).ToArray();
        Avatar curled = AvatarBuilder.BuildHumanoid(new Skeleton(ids, parents, local), description);

        Pose result = Retarget(straight, BindPose(straight), curled);

        Float3 Segment(HumanBodyBone from, HumanBodyBone to) => ModelPos(curled, result, to) - ModelPos(curled, result, from);
        float bend = AngleDeg(Segment(HumanBodyBone.LeftIndexProximal, HumanBodyBone.LeftIndexIntermediate), Segment(HumanBodyBone.LeftIndexIntermediate, HumanBodyBone.LeftIndexDistal));
        Assert.True(bend < 0.5f, $"finger still bent {bend:N1} degrees");
    }

    [Fact]
    public void NoStretch_ClampsToNaturalReach()
    {
        // Chain reach is 2; target at distance 4 with no stretch stays at reach 2.
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();

        TwoBoneIK.Solve(pose, 0, 1, 2, new Float3(4f, 0f, 0f), stretch: 0f);

        pose.CalculateModelSpaceTransforms();
        float reached = Float3.Distance(pose.GetModelSpaceTransform(0).position, pose.GetModelSpaceTransform(2).position);
        Assert.True(reached < 2.01f, $"reached {reached} should not exceed natural reach 2");
    }

    [Fact]
    public void WithStretch_ExtendsBeyondNaturalReach()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();

        // 50% stretch allows reach up to 3.
        TwoBoneIK.Solve(pose, 0, 1, 2, new Float3(2.8f, 0f, 0f), stretch: 0.5f);

        pose.CalculateModelSpaceTransforms();
        Float3 end = pose.GetModelSpaceTransform(2).position;
        Assert.True(Float3.Distance(new Float3(2.8f, 0f, 0f), end) < 1e-2f, $"end {end} should reach the stretched target");
    }

    [Fact]
    public void RoundTrip_ForearmTwist_DoesNotDrift()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        int twist = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftLowerArm, MuscleAxis.X);
        int upperTwist = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftUpperArm, MuscleAxis.X);
        var human = new HumanPose();
        human.SetMuscle(twist, 0.5f);
        Pose first = Decoded(avatar, human);

        for (int cycle = 0; cycle < 4; cycle++)
        {
            human = Encoded(avatar, Decoded(avatar, human));
            Assert.Equal(0.5, human.GetMuscle(twist), 3);
            Assert.Equal(0.0, human.GetMuscle(upperTwist), 3);
        }
        Assert.True(WorstDifference(first, Decoded(avatar, human)).Rotation < 0.1f);
    }

    [Theory]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f)]
    [InlineData(0.2f, 0.9f, 0.7f, 0.1f)]
    [InlineData(0f, 1f, 1f, 0f)]
    [InlineData(0.9f, 0.9f, 0.3f, 0.6f)]
    public void EncodeOfDecode_ReturnsTheMuscles(float upperArm, float lowerArm, float upperLeg, float lowerLeg)
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig { Fingers = true }.Build();
        description.UpperArmTwist = upperArm;
        description.LowerArmTwist = lowerArm;
        description.UpperLegTwist = upperLeg;
        description.LowerLegTwist = lowerLeg;
        Avatar avatar = AvatarBuilder.BuildHumanoid(skeleton, description);
        HumanoidRig rig = avatar.Humanoid!;
        var rng = new Random(5);

        for (int trial = 0; trial < 100; trial++)
        {
            HumanPose human = RandomHuman(rng, 0.7f, body: false);
            HumanPose back = Encoded(avatar, Decoded(avatar, human));
            for (int m = 0; m < HumanTrait.MuscleCount; m++)
            {
                if (!rig.HasBone(HumanTrait.GetMuscleBone(m)))
                    continue;
                float error = MathF.Abs(human.GetMuscle(m) - back.GetMuscle(m));
                Assert.True(error < 5e-3f, $"trial {trial}: {HumanTrait.GetMuscleName(m)} went from {human.GetMuscle(m):N4} to {back.GetMuscle(m):N4}");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedRoundTrips_KeepThePose(bool minimal)
    {
        HumanoidTestRig rigDef = minimal
            ? new HumanoidTestRig { Chest = false, UpperChest = false, Neck = false, Shoulders = false, Toes = false }
            : new HumanoidTestRig { ArmDownDegrees = 30f };
        Avatar avatar = rigDef.BuildAvatar();
        var rng = new Random(9);
        float amplitude = minimal ? 0.4f : 0.7f;

        for (int trial = 0; trial < 60; trial++)
        {
            Pose first = Decoded(avatar, Encoded(avatar, Decoded(avatar, RandomHuman(rng, amplitude, body: true))));
            Pose pose = first;
            for (int cycle = 0; cycle < 3; cycle++)
                pose = Decoded(avatar, Encoded(avatar, pose));
            float rotation = WorstDifference(first, pose).Rotation;
            Assert.True(rotation < 0.5f, $"trial {trial}: a bone drifted {rotation:N2} degrees");
        }
    }
}
