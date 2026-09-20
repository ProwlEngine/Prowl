using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class LookAtFacingTests
{
    private static readonly Float3 Up = new(0f, 1f, 0f);
    private static readonly Float3 Forward = new(0f, 0f, 1f);

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
}
