using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class FootGroundingNodeTests
{
    [Fact]
    public void FootGroundingNode_PullsLiftedFootToGround()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        HumanoidRig rig = avatar.Humanoid!;

        // Author a pose with the left knee bent so the foot lifts off the ground.
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        int upper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int lower = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);
        Transform3D ub = skeleton.GetBoneParentSpaceTransform(upper);
        pose.SetTransform(upper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.8f), ub.scale));
        Transform3D lb = skeleton.GetBoneParentSpaceTransform(lower);
        pose.SetTransform(lower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.4f), lb.scale));

        var clip = new AnimationClip(skeleton, new[] { pose, pose }, 1f);

        var graph = new AnimationGraph();
        int clipNode = graph.AddClip(clip);
        int grounded = graph.AddFootGrounding(clipNode); // ground heights default to 0, weight 1
        graph.SetRoot(grounded);

        AnimationGraphInstance instance = graph.CreateInstance(avatar);
        instance.Update(0.016f);

        instance.Pose.CalculateModelSpaceTransforms();
        int leftFoot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        Assert.Equal(0.0, (double)instance.Pose.GetModelSpaceTransform(leftFoot).position.Y, 2);
    }
}
