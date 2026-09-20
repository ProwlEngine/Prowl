namespace Prowl.Motion.Tests;

public class HumanTraitTests
{
    [Fact]
    public void Hips_IsRequired()
        => Assert.True(HumanTrait.IsRequired(HumanBodyBone.Hips));

    [Fact]
    public void Jaw_IsOptional()
        => Assert.False(HumanTrait.IsRequired(HumanBodyBone.Jaw));

    [Fact]
    public void UpperChest_IsOptional()
        => Assert.True(HumanTrait.IsOptional(HumanBodyBone.UpperChest));

    [Fact]
    public void Head_ParentIsNeck()
        => Assert.Equal(HumanBodyBone.Neck, HumanTrait.GetParentBone(HumanBodyBone.Head));

    [Fact]
    public void Hips_HasNoParent()
        => Assert.Null(HumanTrait.GetParentBone(HumanBodyBone.Hips));

    [Fact]
    public void Hips_AliasesIncludeCommonNames()
    {
        var aliases = HumanTrait.GetNameAliases(HumanBodyBone.Hips);
        Assert.Contains("hips", aliases);
        Assert.Contains("pelvis", aliases);
    }

    [Fact]
    public void BoneName_IsCanonical()
        => Assert.Equal("Hips", HumanTrait.GetBoneName(HumanBodyBone.Hips));
}
