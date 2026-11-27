using Prowl.Clay.Importer;
using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The humanoid bone table: required bones, parents and names.</summary>
public class HumanTrait_Tests
{
    [Fact]
    public void Fingers_AreInTheHumanoidSet()
    {
        Assert.True(HumanTrait.IsOptional(HumanBodyBone.LeftThumbProximal));
        Assert.Equal(HumanBodyBone.LeftHand, HumanTrait.GetParentBone(HumanBodyBone.LeftThumbProximal));
        Assert.Equal(HumanBodyBone.LeftThumbProximal, HumanTrait.GetParentBone(HumanBodyBone.LeftThumbIntermediate));
        Assert.Equal(HumanBodyBone.LeftThumbIntermediate, HumanTrait.GetParentBone(HumanBodyBone.LeftThumbDistal));
    }

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
