using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class LookAtRegressionTests
{
    private static readonly Float3 Up = new(0f, 1f, 0f);
    private static readonly Float3 Forward = new(0f, 0f, 1f);

    internal static Avatar MakeAvatar()
        => new HumanoidTestRig { Chest = false, UpperChest = false, Neck = false, Shoulders = false }.BuildAvatar();

    private static (HumanoidRig Rig, Pose Pose) MakeStanding()
    {
        Avatar avatar = MakeAvatar();
        var pose = new Pose(avatar.Skeleton);
        pose.SetToReferencePose();
        return (avatar.Humanoid!, pose);
    }

    private static Quaternion HeadRotation(HumanoidRig rig, Pose pose)
        => pose.GetModelSpaceTransform(rig.GetSkeletonBoneIndex(HumanBodyBone.Head)).rotation;

    private static Float3 HeadPosition(HumanoidRig rig, Pose pose)
        => pose.GetModelSpaceTransform(rig.GetSkeletonBoneIndex(HumanBodyBone.Head)).position;

    private static float YawDegrees(Float3 v) => MathF.Atan2(v.X, v.Z) * 180f / MathF.PI;

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    public void TargetBehind_DoesNotRollTheHeadOver(float clamp)
    {
        var (rig, pose) = MakeStanding();
        LookAtSolver.Solve(pose, rig, new Float3(0f, 2.7f, -10f), clamp, 0f, 1f, 0f);

        Float3 headUp = HeadRotation(rig, pose) * Up;
        Assert.True(headUp.Y > 0.9f, $"head up {headUp} rolled over");
    }

    [Fact]
    public void AnimatedHeadYaw_IsNotDoubleCounted()
    {
        var (rig, pose) = MakeStanding();
        int head = rig.GetSkeletonBoneIndex(HumanBodyBone.Head);
        Transform3D local = pose.GetTransform(head);
        pose.SetTransform(head, new Transform3D(local.position, Quaternion.AxisAngle(Up, MathF.PI / 4f), local.scale));

        Float3 headPos = HeadPosition(rig, pose);
        var target = new Float3(headPos.X + 5f, headPos.Y, headPos.Z + 5f);
        LookAtSolver.Solve(pose, rig, target, 0f, 0f, 1f, 0f);

        Assert.Equal(45.0, (double)YawDegrees(HeadRotation(rig, pose) * Forward), 1);
    }

    [Fact]
    public void BodyWeightAlone_TurnsTheSpineAndCarriesTheHead()
    {
        var (rig, pose) = MakeStanding();
        int spine = rig.GetSkeletonBoneIndex(HumanBodyBone.Spine);
        Quaternion spineBefore = pose.GetTransform(spine).rotation;

        Float3 headPos = HeadPosition(rig, pose);
        LookAtSolver.Solve(pose, rig, new Float3(headPos.X + 10f, headPos.Y, headPos.Z), 0f, 1f, 0f, 0f);

        Assert.True(Quaternion.Angle(spineBefore, pose.GetTransform(spine).rotation) > 0.5f);
        Assert.Equal(90.0, (double)YawDegrees(HeadRotation(rig, pose) * Forward), 1);
    }

    [Fact]
    public void HeadWeight_TurnsTheHeadPartWay()
    {
        var (rig, pose) = MakeStanding();
        Float3 headPos = HeadPosition(rig, pose);
        LookAtSolver.Solve(pose, rig, new Float3(headPos.X + 10f, headPos.Y, headPos.Z), 0f, 0f, 0.5f, 0f);

        Assert.Equal(45.0, (double)YawDegrees(HeadRotation(rig, pose) * Forward), 1);
    }

    [Fact]
    public void OverallWeight_ScalesTheWholeLook()
    {
        var (rig, pose) = MakeStanding();
        Float3 headPos = HeadPosition(rig, pose);
        var target = new Float3(headPos.X + 10f, headPos.Y, headPos.Z);

        LookAtSolver.Solve(pose, rig, target, 0f, 0f, 0.4f, 1f, 0f);
        Assert.Equal(0.0, (double)YawDegrees(HeadRotation(rig, pose) * Forward), 3);

        LookAtSolver.Solve(pose, rig, target, 0.5f, 0f, 0.4f, 1f, 0f);
        Assert.Equal(45.0, (double)YawDegrees(HeadRotation(rig, pose) * Forward), 1);
    }

    [Fact]
    public void LookAtNode_UsesTheOverallWeight()
    {
        Avatar avatar = MakeAvatar();
        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);

        var g = new AnimationGraph();
        int target = g.AddVectorParameter("T", new Float3(10f, 1.7f, 0f));
        g.SetRoot(g.AddNode(new LookAtDefinition(g.AddReferencePose(), target, weight: 0.5f, clamp: 0f, body: 0f, head: 1f, eyes: 0f)));

        AnimationGraphInstance instance = g.CreateInstance(avatar);
        instance.Update(0.016f);

        Assert.Equal(45.0, (double)YawDegrees(instance.Pose.GetModelSpaceTransform(head).rotation * Forward), 1);
    }
}
