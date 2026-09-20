using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class MatchTargetTests
{
    [Fact]
    public void RampWeight_RisesFromStartToTarget()
    {
        Assert.Equal(0.0, (double)MatchTarget.ComputeRampWeight(0.2f, 0.2f, 0.6f), 4);
        Assert.Equal(0.5, (double)MatchTarget.ComputeRampWeight(0.4f, 0.2f, 0.6f), 4);
        Assert.Equal(1.0, (double)MatchTarget.ComputeRampWeight(0.6f, 0.2f, 0.6f), 4);
    }

    [Fact]
    public void Correction_FullRamp_ClosesTheGap()
    {
        var part = new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One);
        var target = new Transform3D(new Float3(2f, 0f, 0f), Quaternion.Identity, Float3.One);

        Transform3D full = MatchTarget.ComputeCorrection(part, target, new Float3(1f, 1f, 1f), 0f, 1f);
        Assert.Equal(2.0, (double)full.position.X, 4);

        Transform3D half = MatchTarget.ComputeCorrection(part, target, new Float3(1f, 1f, 1f), 0f, 0.5f);
        Assert.Equal(1.0, (double)half.position.X, 4);
    }

    [Fact]
    public void Correction_PositionMask_DisablesAxis()
    {
        var part = new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One);
        var target = new Transform3D(new Float3(2f, 3f, 0f), Quaternion.Identity, Float3.One);
        Transform3D c = MatchTarget.ComputeCorrection(part, target, new Float3(1f, 0f, 1f), 0f, 1f); // Y disabled
        Assert.Equal(2.0, (double)c.position.X, 4);
        Assert.Equal(0.0, (double)c.position.Y, 4);
    }
}

public class FootGroundingTests
{
    [Fact]
    public void Ground_PullsLiftedFootToTheGround()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var rig = avatar.Humanoid!;

        // Bend the left knee so the foot lifts off the ground (still reachable back down).
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        int upper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int lower = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);
        Transform3D ub = skeleton.GetBoneParentSpaceTransform(upper);
        pose.SetTransform(upper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.8f), ub.scale));
        Transform3D lb = skeleton.GetBoneParentSpaceTransform(lower);
        pose.SetTransform(lower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.4f), lb.scale));
        pose.CalculateModelSpaceTransforms();

        int leftFoot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        float liftedY = pose.GetModelSpaceTransform(leftFoot).position.Y;
        Assert.True(liftedY > 0.1f); // it actually lifted

        FootGrounding.Ground(pose, rig, HumanGoal.LeftFoot, groundY: 0f, weight: 1f);
        pose.CalculateModelSpaceTransforms();

        Assert.Equal(0.0, (double)pose.GetModelSpaceTransform(leftFoot).position.Y, 2);
    }
}
