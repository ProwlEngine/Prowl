using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The look at solver: body, head and eyes turning toward a target.</summary>
public class LookAt_Tests
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

    private static (Avatar Avatar, Pose Pose) MakeStandingAvatar()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.CalculateModelSpaceTransforms();
        return (avatar, pose);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(-135f)]
    public void HeadFaces_ATargetInFront(float facingDegrees)
    {
        Quaternion facing = Quaternion.AxisAngle(Up, facingDegrees * MathF.PI / 180f);
        Avatar avatar = new HumanoidTestRig
        {
            Chest = false, UpperChest = false, Neck = false, Shoulders = false,
            ArmatureRotation = facing,
        }.BuildAvatar();
        HumanoidRig rig = avatar.Humanoid!;

        var pose = new Pose(avatar.Skeleton);
        pose.SetToReferencePose();
        int head = rig.GetSkeletonBoneIndex(HumanBodyBone.Head);
        Float3 headPos = pose.GetModelSpaceTransform(head).position;
        Float3 look = facing * Float3.Normalize(new Float3(0.2f, 0f, 1f));

        LookAtSolver.Solve(pose, rig, headPos + look * 5f, clampWeight: 0f, bodyWeight: 0f, headWeight: 1f, eyesWeight: 0f);

        Quaternion delta = pose.GetModelSpaceTransform(head).rotation * Quaternion.Inverse(avatar.Skeleton.GetBoneModelSpaceTransform(head).rotation);
        Float3 headForward = delta * (facing * Forward);
        Assert.True(Float3.Dot(headForward, look) > 0.999f, $"head forward {headForward}, expected {look}");
    }

    [Fact]
    public void PartialLook_TurnsTowardATargetInFront()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        HumanoidRig rig = avatar.Humanoid!;
        var pose = new Pose(avatar.Skeleton);
        pose.SetToReferencePose();
        int head = rig.GetSkeletonBoneIndex(HumanBodyBone.Head);
        Float3 headPos = pose.GetModelSpaceTransform(head).position;
        Float3 look = Float3.Normalize(new Float3(-0.3f, 0.1f, 1f));

        LookAtSolver.Solve(pose, rig, headPos + look * 5f, clampWeight: 0f, bodyWeight: 0.2f, headWeight: 0.6f, eyesWeight: 0f);

        Float3 headForward = pose.GetModelSpaceTransform(head).rotation * Forward;
        Assert.True(Float3.Dot(headForward, Forward) > 0.9f, $"head forward {headForward} turned away from the front");
        Assert.True(Float3.Dot(headForward, look) > Float3.Dot(Forward, look), $"head forward {headForward} did not turn toward {look}");
    }

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
    public void ZeroWeights_LeaveTheHeadUnchanged()
    {
        var (avatar, pose) = MakeStandingAvatar();
        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);
        Quaternion before = pose.GetTransform(head).rotation;

        LookAtSolver.Solve(pose, avatar.Humanoid!, new Float3(5f, 1f, 0f), 0f, 0f, 0f, 0f);

        Quaternion after = pose.GetTransform(head).rotation;
        Assert.True(MathF.Abs(Quaternion.Dot(before, after)) > 0.9999f);
    }

    [Fact]
    public void OffAxisTarget_RotatesTheHead()
    {
        var (avatar, pose) = MakeStandingAvatar();
        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);
        Quaternion before = pose.GetTransform(head).rotation;

        // Target far to the side: the head should rotate to look toward it.
        LookAtSolver.Solve(pose, avatar.Humanoid!, new Float3(10f, 1.6f, 0f), clampWeight: 0f, bodyWeight: 0.3f, headWeight: 1f, eyesWeight: 0f);

        Quaternion after = pose.GetTransform(head).rotation;
        Assert.True(MathF.Abs(Quaternion.Dot(before, after)) < 0.999f);
    }
}
