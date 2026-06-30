using Prowl.Clay;
using Prowl.Clay.Importer;
using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// Tests for the KHR_materials_* PBR extensions and the EmbedTextures post-process step.
/// Each extension test asserts both directions: the typed surface gets populated AND the raw
/// extension key is consumed (i.e. no longer appears in <see cref="Material.RawExtensions"/>),
/// so we'd catch a regression that mapped to the surface but forgot to drop the raw entry.
/// </summary>
public sealed class GltfMaterialExtensionsTests
{
    [Fact]
    public void Clearcoat_PopulatesTypedSurface_AndConsumesRawKey()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/ClearCoatTest/glTF-Binary/ClearCoatTest.glb"));

        Assert.Contains(model.Materials, m => m.Clearcoat is not null && m.Clearcoat.Factor > 0f);
        foreach (var m in model.Materials)
            Assert.DoesNotContain("KHR_materials_clearcoat", m.RawExtensions.Keys);
    }

    [Fact]
    public void Sheen_PopulatesTypedSurface_AndConsumesRawKey()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/SheenChair/glTF-Binary/SheenChair.glb"));

        Assert.Contains(model.Materials, m => m.Sheen is not null);
        foreach (var m in model.Materials)
            Assert.DoesNotContain("KHR_materials_sheen", m.RawExtensions.Keys);
    }

    [Fact]
    public void Transmission_PopulatesTypedSurface_AndConsumesRawKey()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/TransmissionTest/glTF-Binary/TransmissionTest.glb"));

        Assert.Contains(model.Materials, m => m.Transmission is not null && m.Transmission.Factor > 0f);
        foreach (var m in model.Materials)
            Assert.DoesNotContain("KHR_materials_transmission", m.RawExtensions.Keys);
    }

    [Fact]
    public void UnknownExtension_PassesThroughToRawExtensions()
    {
        // KHR_materials_iridescence is not mapped to a typed surface; consumers depending on
        // raw-extension passthrough must still see it.
        var model = ModelImporter.Load(TestModels.Gltf("2.0/IridescentDishWithOlives/glTF-Binary/IridescentDishWithOlives.glb"));

        Assert.Contains(model.Materials, m => m.RawExtensions.ContainsKey("KHR_materials_iridescence"));
    }

    [Fact]
    public void EmbedTextures_Off_KeepsExternalTexturesAsSourcePathOnly()
    {
        // GameQuality has EmbedTextures OFF by default - external textures stay external.
        var model = ModelImporter.Load(
            TestModels.Gltf("2.0/BoxTextured/glTF/BoxTextured.gltf"),
            ModelImporterSettings.GameQuality);

        var tex = model.Textures[0];
        Assert.NotNull(tex.SourcePath);
        Assert.Null(tex.EncodedBytes);
    }

    [Fact]
    public void EmbedTextures_On_InlinesExternalBytes_AndPreservesSourcePath()
    {
        var settings = ModelImporterSettings.GameQuality with
        {
            PostProcess = ModelImporterSettings.GameQuality.PostProcess | PostProcessFlags.EmbedTextures,
        };
        var model = ModelImporter.Load(TestModels.Gltf("2.0/BoxTextured/glTF/BoxTextured.gltf"), settings);

        var tex = model.Textures[0];
        // SourcePath stays populated so streaming callers can still see provenance.
        Assert.NotNull(tex.SourcePath);
        Assert.NotNull(tex.EncodedBytes);
        Assert.True(tex.EncodedBytes!.Length > 0);
    }

    [Fact]
    public void EmbedTextures_OnAlreadyEmbeddedGlb_IsNoOp()
    {
        var settings = ModelImporterSettings.GameQuality with
        {
            PostProcess = ModelImporterSettings.GameQuality.PostProcess | PostProcessFlags.EmbedTextures,
        };
        var model = ModelImporter.Load(TestModels.Gltf("2.0/BoxTextured/glTF-Binary/BoxTextured.glb"), settings);

        var tex = model.Textures[0];
        Assert.Null(tex.SourcePath);
        Assert.NotNull(tex.EncodedBytes);
    }
}
