namespace Prowl.Motion.Tests;

/// <summary>Exercises the auto-mapper's side-naming support and topology validation.</summary>
public class AutoMapperRobustnessTests
{
    [Fact]
    public void PrefixNaming_MapsHumanoid()
        => Assert.True(HumanoidAutoMapper.IsLikelyHumanoid(TestSkeletons.MakeMinimalHumanoid(TestSkeletons.SideNaming.Prefix)));

    [Fact]
    public void UnderscoreSuffixNaming_MapsHumanoid()
        => Assert.True(HumanoidAutoMapper.IsLikelyHumanoid(TestSkeletons.MakeMinimalHumanoid(TestSkeletons.SideNaming.SuffixUnderscore)));

    [Fact]
    public void BrokenArmHierarchy_IsRejectedDespiteCompleteNames()
    {
        // All required bone names are present, but the left hand is parented under the hips, so the
        // arm chain is not a real hierarchy. Topology validation must reject it.
        var skeleton = TestSkeletons.MakeMinimalHumanoid(TestSkeletons.SideNaming.Prefix, breakArmHierarchy: true);
        Assert.False(HumanoidAutoMapper.IsLikelyHumanoid(skeleton));
    }

    [Fact]
    public void UnderscoreSuffix_AssignsHandsCorrectly()
    {
        Assert.True(HumanoidAutoMapper.TryMap(TestSkeletons.MakeMinimalHumanoid(TestSkeletons.SideNaming.SuffixUnderscore), out var description));
        Assert.True(description.HasBone(HumanBodyBone.LeftHand));
        Assert.True(description.HasBone(HumanBodyBone.RightHand));
    }

    [ModelFact]
    public void MixamoModel_ScaleIsPositive()
    {
        var avatar = AvatarBuilder.BuildAutomatic(ClayModelLoader.LoadHumanoidSkeleton());
        Assert.True(avatar.Humanoid!.Scale > 0f);
    }
}
