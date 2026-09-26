using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The foot placement solver: feet onto the ground under them, the hips dropped so both legs reach.</summary>
public class FootPlacement_Tests
{
    private static (Avatar Avatar, Pose Pose) Standing()
    {
        Avatar avatar = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f));
        return (avatar, HumanoidTestRig.BindPose(avatar));
    }

    private static float Y(Avatar avatar, Pose pose, HumanBodyBone bone) => HumanoidTestRig.ModelPos(avatar, pose, bone).Y;

    [Fact]
    public void UnevenGround_PutsEachFootOnItsOwn_AndDropsTheHipsForTheLowerOne()
    {
        (Avatar avatar, Pose pose) = Standing();
        new FootPlacement().Solve(pose, avatar.Humanoid!, new FootGround(-0.2f, Float3.UnitY), new FootGround(0f, Float3.UnitY), 1f, 1f / 60f);

        Assert.Equal(-0.2f, Y(avatar, pose, HumanBodyBone.LeftFoot), 3);
        Assert.Equal(0f, Y(avatar, pose, HumanBodyBone.RightFoot), 3);
        Assert.Equal(0.8f, Y(avatar, pose, HumanBodyBone.Hips), 3);
    }

    // It settles on the ground at once, then eases half way to a change in each half life.
    [Fact]
    public void AChangeInTheGround_IsEasedByItsHalfLife()
    {
        (Avatar avatar, Pose standing) = Standing();
        var placement = new FootPlacement { FootSmoothing = 0.1f, HipsSmoothing = 0.1f };
        placement.Solve(HumanoidTestRig.BindPose(avatar), avatar.Humanoid!, new FootGround(0f, Float3.UnitY), new FootGround(0f, Float3.UnitY), 1f, 0.1f);

        placement.Solve(standing, avatar.Humanoid!, new FootGround(-0.2f, Float3.UnitY), new FootGround(0f, Float3.UnitY), 1f, 0.1f);

        Assert.Equal(-0.1f, Y(avatar, standing, HumanBodyBone.LeftFoot), 3);
        Assert.Equal(0.9f, Y(avatar, standing, HumanBodyBone.Hips), 3);
    }

    [Fact]
    public void AtZeroWeight_ThePoseIsLeftAlone()
    {
        (Avatar avatar, Pose pose) = Standing();
        new FootPlacement().Solve(pose, avatar.Humanoid!, new FootGround(-0.2f, Float3.UnitY), new FootGround(0.1f, Float3.UnitY), 0f, 1f / 60f);

        Assert.Equal(0f, Y(avatar, pose, HumanBodyBone.LeftFoot), 3);
        Assert.Equal(1f, Y(avatar, pose, HumanBodyBone.Hips), 3);
    }
}
