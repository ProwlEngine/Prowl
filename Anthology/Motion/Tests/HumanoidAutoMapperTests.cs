namespace Prowl.Motion.Tests;

public class HumanoidAutoMapperTests
{
    [Fact]
    public void SimpleChain_IsNotHumanoid()
        => Assert.False(HumanoidAutoMapper.IsLikelyHumanoid(TestSkeletons.MakeChain()));

    [Fact]
    public void SimpleChain_TryMap_Fails()
        => Assert.False(HumanoidAutoMapper.TryMap(TestSkeletons.MakeChain(), out _));

    [ModelFact]
    public void MixamoModel_IsHumanoid()
        => Assert.True(HumanoidAutoMapper.IsLikelyHumanoid(ClayModelLoader.LoadHumanoidSkeleton()));

    [ModelFact]
    public void MixamoModel_TryMap_Succeeds()
        => Assert.True(HumanoidAutoMapper.TryMap(ClayModelLoader.LoadHumanoidSkeleton(), out _));

    [ModelFact]
    public void MixamoModel_MapsHipsAndHead()
    {
        Assert.True(HumanoidAutoMapper.TryMap(ClayModelLoader.LoadHumanoidSkeleton(), out var description));
        Assert.True(description.HasBone(HumanBodyBone.Hips));
        Assert.True(description.HasBone(HumanBodyBone.Head));
    }

    [ModelFact]
    public void MixamoModel_MapsAllRequiredBones()
    {
        Assert.True(HumanoidAutoMapper.TryMap(ClayModelLoader.LoadHumanoidSkeleton(), out var description));
        Assert.True(description.HasAllRequiredBones);
    }
}
