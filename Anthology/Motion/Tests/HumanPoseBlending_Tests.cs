using Prowl.Vector.Spatial;
using Prowl.Vector;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

/// <summary>Blending human poses in muscle space.</summary>
public class HumanPoseBlending_Tests
{
    private static readonly Float3 Up = new(0f, 1f, 0f);

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

    private static readonly int SpineFrontBack = HumanTrait.GetMuscleIndex(HumanBodyBone.Spine, MuscleAxis.Z);

    private static HumanPose PoseWithUniformMuscles(float value)
    {
        var p = new HumanPose();
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
            p.SetMuscle(i, value);
        return p;
    }

    private static readonly int ArmDownUp = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftUpperArm, MuscleAxis.Z);

    private static readonly int LegFrontBack = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftUpperLeg, MuscleAxis.Z);

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
    public void Blend_LerpsMuscles()
    {
        var result = new HumanPose();
        HumanPoseBlender.Blend(result, PoseWithUniformMuscles(0f), PoseWithUniformMuscles(0.8f), 0.5f);
        Assert.Equal(0.4, (double)result.GetMuscle(SpineFrontBack), 4);
    }

    [Fact]
    public void MaskedBlend_OnlyAffectsMaskedBones()
    {
        var result = new HumanPose();
        var mask = HumanPoseMask.ForBodyPart(HumanBodyPart.UpperBody);
        HumanPoseBlender.Blend(result, PoseWithUniformMuscles(0f), PoseWithUniformMuscles(0.8f), 1f, mask);

        Assert.Equal(0.8, (double)result.GetMuscle(ArmDownUp), 4);
        Assert.Equal(0.0, (double)result.GetMuscle(LegFrontBack), 4);
    }

    [Fact]
    public void Subtract_BuildsDelta()
    {
        var delta = new HumanPose();
        HumanPoseBlender.Subtract(delta, PoseWithUniformMuscles(0.7f), PoseWithUniformMuscles(0.2f));
        Assert.Equal(0.5, (double)delta.GetMuscle(SpineFrontBack), 4);
    }

    [Fact]
    public void AddLayer_AddsWeightedAdditive()
    {
        var basePose = PoseWithUniformMuscles(0.2f);
        HumanPoseBlender.AddLayer(basePose, PoseWithUniformMuscles(0.4f), 0.5f);
        Assert.Equal(0.4, (double)basePose.GetMuscle(SpineFrontBack), 4);
    }

    [Fact]
    public void AddLayer_LayersBodyRotationOnTheSameSideSubtractRemovesIt()
    {
        var a = new HumanPose { BodyRotation = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.9f) * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.5f) };
        var b = new HumanPose { BodyRotation = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.7f) };
        var delta = new HumanPose();
        HumanPoseBlender.Subtract(delta, a, b);
        HumanPoseBlender.AddLayer(b, delta, 1f);
        Assert.True(MathF.Abs(Quaternion.Dot(a.BodyRotation, b.BodyRotation)) > 0.9999f);
    }
}
