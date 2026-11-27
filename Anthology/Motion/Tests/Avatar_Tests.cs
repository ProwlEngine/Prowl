using Prowl.Vector.Spatial;
using Prowl.Vector;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

/// <summary>Avatars: building generic and humanoid avatars from a skeleton and a description.</summary>
public class Avatar_Tests
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

    [Fact]
    public void BuildHumanoid_WithoutRequiredBones_Throws()
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig().Build();
        description.SetSkeletonBoneIndex(HumanBodyBone.Hips, Skeleton.InvalidIndex);

        Assert.Throws<ArgumentException>(() => AvatarBuilder.BuildHumanoid(skeleton, description));
    }

    [Fact]
    public void BuildHumanoid_WithOutOfRangeBoneIndex_Throws()
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig().Build();
        description.SetSkeletonBoneIndex(HumanBodyBone.Jaw, skeleton.BoneCount + 5);

        Assert.Throws<ArgumentException>(() => AvatarBuilder.BuildHumanoid(skeleton, description));
    }

    [Fact]
    public void HumanoidRig_IsNotAffectedByLaterDescriptionEdits()
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig().Build();
        Avatar avatar = AvatarBuilder.BuildHumanoid(skeleton, description);
        int hips = description.GetSkeletonBoneIndex(HumanBodyBone.Hips);

        description.SetSkeletonBoneIndex(HumanBodyBone.Hips, Skeleton.InvalidIndex);
        description.LegStretch = 0.9f;

        Assert.Equal(hips, avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips));
        Assert.True(avatar.IsValid);
        Assert.NotEqual(0.9f, avatar.Humanoid!.Description.LegStretch);
    }
}
