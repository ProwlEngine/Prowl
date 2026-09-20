namespace Prowl.Motion.Tests;

public class AvatarTests
{
    [Fact]
    public void BuildAutomatic_OnSimpleChain_IsGeneric()
    {
        var avatar = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeChain());
        Assert.Equal(AvatarType.Generic, avatar.Type);
        Assert.False(avatar.IsHuman);
        Assert.Null(avatar.Humanoid);
    }

    [ModelFact]
    public void BuildAutomatic_OnMixamoModel_IsHumanoid()
    {
        var avatar = AvatarBuilder.BuildAutomatic(ClayModelLoader.LoadHumanoidSkeleton());
        Assert.Equal(AvatarType.Humanoid, avatar.Type);
        Assert.True(avatar.IsHuman);
        Assert.NotNull(avatar.Humanoid);
    }

    [ModelFact]
    public void Humanoid_MapsHipsToAValidSkeletonBone()
    {
        var avatar = AvatarBuilder.BuildAutomatic(ClayModelLoader.LoadHumanoidSkeleton());
        int hips = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        Assert.NotEqual(Skeleton.InvalidIndex, hips);
    }

    [Fact]
    public void BuildGeneric_ProducesGenericAvatar()
    {
        var avatar = AvatarBuilder.BuildGeneric(TestSkeletons.MakeChain());
        Assert.Equal(AvatarType.Generic, avatar.Type);
        Assert.True(avatar.IsValid);
    }

    [Fact]
    public void TryBuildHumanoid_OnSimpleChain_ReturnsFalse()
        => Assert.False(AvatarBuilder.TryBuildHumanoid(TestSkeletons.MakeChain(), out _));

    [ModelFact]
    public void TryBuildHumanoid_OnMixamoModel_ReturnsTrue()
        => Assert.True(AvatarBuilder.TryBuildHumanoid(ClayModelLoader.LoadHumanoidSkeleton(), out _));
}
