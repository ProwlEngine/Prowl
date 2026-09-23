namespace Prowl.Motion.Tests;

/// <summary>Human descriptions: which skeleton bone each humanoid bone maps to.</summary>
public class HumanDescription_Tests
{
    [Fact]
    public void UnmappedBone_ReturnsInvalidIndex()
        => Assert.Equal(Skeleton.InvalidIndex, new HumanDescription().GetSkeletonBoneIndex(HumanBodyBone.Head));

    [Fact]
    public void Empty_DoesNotHaveAllRequiredBones()
        => Assert.False(new HumanDescription().HasAllRequiredBones);
}
