using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class LookAtTests
{
    private static (Avatar Avatar, Pose Pose) MakeStanding()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.CalculateModelSpaceTransforms();
        return (avatar, pose);
    }

    [Fact]
    public void ZeroWeights_LeaveTheHeadUnchanged()
    {
        var (avatar, pose) = MakeStanding();
        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);
        Quaternion before = pose.GetTransform(head).rotation;

        LookAtSolver.Solve(pose, avatar.Humanoid!, new Float3(5f, 1f, 0f), 0f, 0f, 0f, 0f);

        Quaternion after = pose.GetTransform(head).rotation;
        Assert.True(MathF.Abs(Quaternion.Dot(before, after)) > 0.9999f);
    }

    [Fact]
    public void OffAxisTarget_RotatesTheHead()
    {
        var (avatar, pose) = MakeStanding();
        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);
        Quaternion before = pose.GetTransform(head).rotation;

        // Target far to the side: the head should rotate to look toward it.
        LookAtSolver.Solve(pose, avatar.Humanoid!, new Float3(10f, 1.6f, 0f), clampWeight: 0f, bodyWeight: 0.3f, headWeight: 1f, eyesWeight: 0f);

        Quaternion after = pose.GetTransform(head).rotation;
        Assert.True(MathF.Abs(Quaternion.Dot(before, after)) < 0.999f);
    }

    [Fact]
    public void RetargetTo_AppliesLookAtFromHumanPose()
    {
        var (avatar, _) = MakeStanding();
        var human = new HumanPose();
        // No source rotation (rest), but request a look-at.
        human.LookAtPosition = new Float3(10f, 1.6f, 0f);
        human.LookAtHeadWeight = 1f;
        human.LookAtBodyWeight = 0.3f;

        var result = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, result);

        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);
        Quaternion bind = avatar.Skeleton.GetBoneParentSpaceTransform(head).rotation;
        Assert.True(MathF.Abs(Quaternion.Dot(bind, result.GetTransform(head).rotation)) < 0.999f);
    }
}
