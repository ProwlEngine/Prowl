using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The IK Rig node: solving several effectors at once.</summary>
public class N_IKRig_Tests
{
    private sealed record Leg(Avatar Avatar, AnimationClip Clip, int Upper, int Lower, int Foot, Float3 Lifted);

    private static Leg MakeLiftedLeg()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        HumanoidRig rig = avatar.Humanoid!;
        int upper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int lower = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);
        int foot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);

        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        Transform3D ub = skeleton.GetBoneParentSpaceTransform(upper);
        pose.SetTransform(upper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.8f), ub.scale));
        Transform3D lb = skeleton.GetBoneParentSpaceTransform(lower);
        pose.SetTransform(lower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.4f), lb.scale));
        Float3 lifted = pose.GetModelSpaceTransform(foot).position;

        return new Leg(avatar, new AnimationClip(skeleton, new[] { pose, pose }, 1f), upper, lower, foot, lifted);
    }

    private static Float3 FootAfterUpdate(AnimationGraph graph, Leg leg, Action<AnimationGraphInstance>? setup = null)
    {
        AnimationGraphInstance instance = graph.CreateInstance(leg.Avatar);
        setup?.Invoke(instance);
        instance.Update(0.016f);
        return instance.Pose.GetModelSpaceTransform(leg.Foot).position;
    }

    [Fact]
    public void IKRigNode_UnsetTargetLeavesTheLegAlone()
    {
        Leg leg = MakeLiftedLeg();
        var g = new AnimationGraph();
        int clip = g.AddClip(leg.Clip);
        int target = g.AddTargetParameter("T");
        g.SetRoot(g.AddIKRig(clip, new[] { new IKEffectorInfo("Leg", new[] { leg.Upper, leg.Lower, leg.Foot }, target) }));

        Assert.True(Float3.Distance(FootAfterUpdate(g, leg), leg.Lifted) < 1e-4f);
    }

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
