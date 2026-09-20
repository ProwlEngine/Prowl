namespace Prowl.Motion.Tests;

public class HumanDescriptionTests
{
    [Fact]
    public void New_HasNoBonesMapped()
        => Assert.False(new HumanDescription().HasBone(HumanBodyBone.Hips));

    [Fact]
    public void SetThenGet_RoundTrips()
    {
        var d = new HumanDescription();
        d.SetSkeletonBoneIndex(HumanBodyBone.Hips, 5);
        Assert.Equal(5, d.GetSkeletonBoneIndex(HumanBodyBone.Hips));
    }

    [Fact]
    public void UnmappedBone_ReturnsInvalidIndex()
        => Assert.Equal(Skeleton.InvalidIndex, new HumanDescription().GetSkeletonBoneIndex(HumanBodyBone.Head));

    [Fact]
    public void Empty_DoesNotHaveAllRequiredBones()
        => Assert.False(new HumanDescription().HasAllRequiredBones);
}
