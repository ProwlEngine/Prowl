using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class IKRigNodeTests
{
    [Fact]
    public void IKRigNode_DrivesLegChainToTarget()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        HumanoidRig rig = avatar.Humanoid!;

        int upper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int lower = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);
        int foot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);

        // Bent-knee pose with the foot lifted off the ground.
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        Transform3D ub = skeleton.GetBoneParentSpaceTransform(upper);
        pose.SetTransform(upper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.8f), ub.scale));
        Transform3D lb = skeleton.GetBoneParentSpaceTransform(lower);
        pose.SetTransform(lower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.4f), lb.scale));
        pose.CalculateModelSpaceTransforms();
        Float3 lifted = pose.GetModelSpaceTransform(foot).position;
        Assert.True(lifted.Y > 0.1f);

        var clip = new AnimationClip(skeleton, new[] { pose, pose }, 1f);

        var g = new AnimationGraph();
        int clipNode = g.AddClip(clip);
        int target = g.AddVectorParameter("FootTarget", new Float3(lifted.X, 0f, lifted.Z)); // ground it
        int effectors = g.AddIKRig(clipNode, new[]
        {
            new IKEffectorInfo("LeftLeg", new[] { upper, lower, foot }, target),
        });
        g.SetRoot(effectors);

        AnimationGraphInstance i = g.CreateInstance(avatar);
        i.Update(0.016f);

        i.Pose.CalculateModelSpaceTransforms();
        Assert.Equal(0.0, (double)i.Pose.GetModelSpaceTransform(foot).position.Y, 2);
    }
}
