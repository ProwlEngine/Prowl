using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class IKNodeTargetTests
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
    public void TwoBoneIKNode_UnsetTargetLeavesTheLegAlone()
    {
        Leg leg = MakeLiftedLeg();
        var g = new AnimationGraph();
        int clip = g.AddClip(leg.Clip);
        int target = g.AddTargetParameter("T");
        g.SetRoot(g.AddTwoBoneIK(clip, target, leg.Upper, leg.Lower, leg.Foot));

        Assert.True(Float3.Distance(FootAfterUpdate(g, leg), leg.Lifted) < 1e-4f);
    }

    [Fact]
    public void TwoBoneIKNode_ResolvesATargetTypedInput()
    {
        Leg leg = MakeLiftedLeg();
        var g = new AnimationGraph();
        int clip = g.AddClip(leg.Clip);
        int target = g.AddTargetParameter("T");
        g.SetRoot(g.AddTwoBoneIK(clip, target, leg.Upper, leg.Lower, leg.Foot));

        var goal = new Float3(leg.Lifted.X, 0.1f, leg.Lifted.Z + 0.1f);
        Float3 foot = FootAfterUpdate(g, leg, i => i.SetTarget("T", Target.FromWorld(new Transform3D(goal, Quaternion.Identity, Float3.One))));

        Assert.True(Float3.Distance(foot, goal) < 1e-3f, $"foot {foot} missed {goal}");
    }

    [Fact]
    public void TwoBoneIKNode_ConvertsWorldTargetsIntoModelSpace()
    {
        Leg leg = MakeLiftedLeg();
        var g = new AnimationGraph();
        int clip = g.AddClip(leg.Clip);
        int target = g.AddTargetParameter("T");
        int root = g.AddTwoBoneIK(clip, target, leg.Upper, leg.Lower, leg.Foot);
        g.SetRoot(root);
        AnimationGraphInstance instance = g.CreateInstance(leg.Avatar);

        var goal = new Float3(leg.Lifted.X, 0.1f, leg.Lifted.Z + 0.1f);
        var world = new Transform3D(new Float3(5f, 1f, 2f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.7f), Float3.One);
        Float3 goalWorld = world.TransformPoint(goal);
        var context = new GraphContext
        {
            DeltaTime = 0.016f,
            Skeleton = leg.Avatar.Skeleton,
            Avatar = leg.Avatar,
            UpdateId = 1,
            WorldTransform = world,
            WorldTransformInverse = new Transform3D(Quaternion.Inverse(world.rotation) * -world.position, Quaternion.Inverse(world.rotation), Float3.One),
            Parameters = new[] { ParameterValue.FromTarget(Target.FromWorld(new Transform3D(goalWorld, Quaternion.Identity, Float3.One))) },
        };
        var node = (PoseNodeInstance)instance.GetNodeInstance(root);
        node.Update(context);

        Float3 foot = node.Pose.GetModelSpaceTransform(leg.Foot).position;
        Assert.True(Float3.Distance(foot, goal) < 1e-3f, $"foot {foot} missed {goal}");
    }

    [Fact]
    public void LookAtNode_ResolvesATargetTypedInput()
    {
        Avatar avatar = LookAtRegressionTests.MakeAvatar();
        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);

        var g = new AnimationGraph();
        int target = g.AddTargetParameter("T");
        g.SetRoot(g.AddLookAt(g.AddReferencePose(), target, clamp: 0f, body: 0f, head: 1f, eyes: 0f));

        AnimationGraphInstance instance = g.CreateInstance(avatar);
        instance.SetTarget("T", Target.FromWorld(new Transform3D(new Float3(10f, 1.7f, 0f), Quaternion.Identity, Float3.One)));
        instance.Update(0.016f);

        Float3 forward = instance.Pose.GetModelSpaceTransform(head).rotation * new Float3(0f, 0f, 1f);
        Assert.True(forward.X > 0.99f, $"head forward {forward} does not face the target");
    }

    [Fact]
    public void LookAtNode_UnsetTargetLeavesTheHeadAlone()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);

        var g = new AnimationGraph();
        int target = g.AddTargetParameter("T");
        g.SetRoot(g.AddLookAt(g.AddReferencePose(), target, clamp: 0f, body: 0f, head: 1f, eyes: 0f));

        AnimationGraphInstance instance = g.CreateInstance(avatar);
        instance.Update(0.016f);

        Assert.Equal(skeleton.GetBoneParentSpaceTransform(head).rotation, instance.Pose.GetTransform(head).rotation);
    }
}
