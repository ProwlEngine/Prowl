using Prowl.Clay;
using Prowl.Clay.Importer;
using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// Tests for the FBX skinning + animation path: skin clusters resolve to valid joint indices,
/// inverse-bind matrices match per-bone counts, BoneWeights are normalized, and a static FBX
/// produces zero skins / clips.
/// </summary>
public sealed class FbxAnimationTests
{
    [Fact]
    public void AnimationWithSkeleton_PopulatesValidSkinAndNormalizedWeights()
    {
        var model = ModelImporter.Load(TestModels.Fbx("animation_with_skeleton.fbx"));

        Assert.NotEmpty(model.Skins);
        Assert.NotEmpty(model.AnimationClips);

        var skin = model.Skins[0];
        Assert.NotEmpty(skin.BoneNodeIndices);
        Assert.Equal(skin.BoneNodeIndices.Length, skin.InverseBindPoses.Length);
        foreach (var idx in skin.BoneNodeIndices)
            Assert.InRange(idx, 0, model.Nodes.Count - 1);

        var skinnedMesh = model.Meshes.First(m => m.BoneWeights is not null);
        Assert.NotNull(skinnedMesh.BindPoses);
        Assert.Equal(skin.InverseBindPoses.Length, skinnedMesh.BindPoses!.Length);

        // Every vertex weight tuple must sum to ~1 post LimitBoneWeights.
        foreach (var bw in skinnedMesh.BoneWeights!)
        {
            float sum = bw.Weight0 + bw.Weight1 + bw.Weight2 + bw.Weight3;
            Assert.InRange(sum, 0.99f, 1.01f);
        }
    }

    [Fact]
    public void AnimationWithSkeleton_AnimationBindings_TargetValidNodes()
    {
        var model = ModelImporter.Load(TestModels.Fbx("animation_with_skeleton.fbx"));
        var clip = model.AnimationClips[0];

        Assert.True(clip.Duration > 0f);
        Assert.NotEmpty(clip.Bindings);
        foreach (var b in clip.Bindings)
            Assert.InRange(b.NodeIndex, 0, model.Nodes.Count - 1);
    }

    [Fact]
    public void StaticFbx_ProducesNoSkinsAndNoAnimations()
    {
        // spider.fbx is rigid geometry only - no clusters, no AnimationStack.
        var model = ModelImporter.Load(TestModels.Fbx("spider.fbx"));

        Assert.Empty(model.Skins);
        Assert.Empty(model.AnimationClips);
    }
}
