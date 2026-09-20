using Prowl.Vector;

namespace Prowl.Motion.Tests;

public class MuscleSpaceOpsTests
{
    private static readonly int SpineFrontBack = HumanTrait.GetMuscleIndex(HumanBodyBone.Spine, MuscleAxis.Z);
    private static readonly int ArmDownUp = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftUpperArm, MuscleAxis.Z);
    private static readonly int LegFrontBack = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftUpperLeg, MuscleAxis.Z);

    private static HumanPose PoseWithUniformMuscles(float value)
    {
        var p = new HumanPose();
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
            p.SetMuscle(i, value);
        return p;
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
