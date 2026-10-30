// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// KHR_materials_iridescence, anisotropy, dispersion and diffuse_transmission map to typed surfaces with the spec defaults
/// and consume their raw keys.
/// </summary>
public sealed class GltfLayeredMaterialTests
{
    private static Material LoadMaterial(string extension, string body)
    {
        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "extensionsUsed": [ "{{extension}}" ],
          "scene": 0,
          "scenes": [ { "nodes": [] } ],
          "textures": [ { "source": 0 }, { "source": 0 } ],
          "images": [ { "uri": "missing.png" } ],
          "materials": [ { "name": "M", "extensions": { "{{extension}}": {{body}} } } ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Material m = Assert.Single(ModelImporter.Load(stream, "gltf", ModelImporterSettings.Raw).Materials);
        Assert.DoesNotContain(extension, m.RawExtensions.Keys);
        return m;
    }

    [Fact]
    public void Iridescence_ReadsEveryField()
    {
        Material m = LoadMaterial("KHR_materials_iridescence", """
        {
          "iridescenceFactor": 0.8, "iridescenceTexture": { "index": 0 }, "iridescenceIor": 1.8,
          "iridescenceThicknessMinimum": 250, "iridescenceThicknessMaximum": 500, "iridescenceThicknessTexture": { "index": 1, "texCoord": 1 }
        }
        """);

        IridescenceExtension i = Assert.IsType<IridescenceExtension>(m.Iridescence);
        Assert.Equal(0.8f, i.Factor);
        Assert.Equal(0, i.FactorTexture!.TextureIndex);
        Assert.Equal(1.8f, i.Ior);
        Assert.Equal(250f, i.ThicknessMinimum);
        Assert.Equal(500f, i.ThicknessMaximum);
        Assert.Equal(1, i.ThicknessTexture!.TextureIndex);
        Assert.Equal(1, i.ThicknessTexture.UVChannel);
    }

    [Fact]
    public void Iridescence_DefaultsFollowTheSpec()
    {
        IridescenceExtension i = Assert.IsType<IridescenceExtension>(LoadMaterial("KHR_materials_iridescence", "{ }").Iridescence);
        Assert.Equal(0f, i.Factor);
        Assert.Equal(1.3f, i.Ior);
        Assert.Equal(100f, i.ThicknessMinimum);
        Assert.Equal(400f, i.ThicknessMaximum);
        Assert.Null(i.FactorTexture);
        Assert.Null(i.ThicknessTexture);
    }

    [Fact]
    public void Anisotropy_ReadsEveryField()
    {
        Material m = LoadMaterial("KHR_materials_anisotropy", """
        { "anisotropyStrength": 0.6, "anisotropyRotation": 1.25, "anisotropyTexture": { "index": 1 } }
        """);

        AnisotropyExtension a = Assert.IsType<AnisotropyExtension>(m.Anisotropy);
        Assert.Equal(0.6f, a.Strength);
        Assert.Equal(1.25f, a.Rotation);
        Assert.Equal(1, a.Texture!.TextureIndex);
    }

    [Fact]
    public void Anisotropy_DefaultsFollowTheSpec()
    {
        AnisotropyExtension a = Assert.IsType<AnisotropyExtension>(LoadMaterial("KHR_materials_anisotropy", "{ }").Anisotropy);
        Assert.Equal(0f, a.Strength);
        Assert.Equal(0f, a.Rotation);
        Assert.Null(a.Texture);
    }

    [Fact]
    public void Dispersion_ReadsTheValueAndItsAbbeNumber()
    {
        DispersionExtension d = Assert.IsType<DispersionExtension>(LoadMaterial("KHR_materials_dispersion", "{ \"dispersion\": 0.5 }").Dispersion);
        Assert.Equal(0.5f, d.Dispersion);
        Assert.Equal(40f, d.AbbeNumber);
    }

    [Fact]
    public void Dispersion_DefaultsToNone()
    {
        DispersionExtension d = Assert.IsType<DispersionExtension>(LoadMaterial("KHR_materials_dispersion", "{ }").Dispersion);
        Assert.Equal(0f, d.Dispersion);
        Assert.Equal(float.PositiveInfinity, d.AbbeNumber);
    }

    [Fact]
    public void DiffuseTransmission_ReadsEveryField()
    {
        Material m = LoadMaterial("KHR_materials_diffuse_transmission", """
        {
          "diffuseTransmissionFactor": 0.4, "diffuseTransmissionTexture": { "index": 0 },
          "diffuseTransmissionColorFactor": [ 0.2, 0.9, 0.1 ], "diffuseTransmissionColorTexture": { "index": 1 }
        }
        """);

        DiffuseTransmissionExtension t = Assert.IsType<DiffuseTransmissionExtension>(m.DiffuseTransmission);
        Assert.Equal(0.4f, t.Factor);
        Assert.Equal(0, t.FactorTexture!.TextureIndex);
        Assert.Equal(0.2f, t.ColorFactor.R);
        Assert.Equal(0.9f, t.ColorFactor.G);
        Assert.Equal(0.1f, t.ColorFactor.B);
        Assert.Equal(1, t.ColorTexture!.TextureIndex);
    }

    [Fact]
    public void DiffuseTransmission_DefaultsFollowTheSpec()
    {
        DiffuseTransmissionExtension t = Assert.IsType<DiffuseTransmissionExtension>(LoadMaterial("KHR_materials_diffuse_transmission", "{ }").DiffuseTransmission);
        Assert.Equal(0f, t.Factor);
        Assert.Equal(1f, t.ColorFactor.R);
        Assert.Equal(1f, t.ColorFactor.G);
        Assert.Equal(1f, t.ColorFactor.B);
    }

    [Fact]
    public void Clearcoat_ReadsTheNormalTextureScale()
    {
        Material m = LoadMaterial("KHR_materials_clearcoat", """
        { "clearcoatFactor": 1, "clearcoatNormalTexture": { "index": 1, "scale": 0.4 } }
        """);

        ClearcoatExtension c = Assert.IsType<ClearcoatExtension>(m.Clearcoat);
        Assert.Equal(1, c.NormalTexture!.TextureIndex);
        Assert.Equal(0.4f, c.NormalScale);
    }

    [Fact]
    public void Clearcoat_NormalScaleDefaultsToOne()
    {
        ClearcoatExtension withMap = Assert.IsType<ClearcoatExtension>(LoadMaterial("KHR_materials_clearcoat", """
        { "clearcoatNormalTexture": { "index": 0 } }
        """).Clearcoat);
        Assert.Equal(1f, withMap.NormalScale);

        ClearcoatExtension without = Assert.IsType<ClearcoatExtension>(LoadMaterial("KHR_materials_clearcoat", "{ }").Clearcoat);
        Assert.Equal(1f, without.NormalScale);
        Assert.Null(without.NormalTexture);
    }

    [Theory]
    [InlineData("KHR_materials_iridescence")]
    [InlineData("KHR_materials_anisotropy")]
    [InlineData("KHR_materials_dispersion")]
    [InlineData("KHR_materials_diffuse_transmission")]
    public void LayeredExtension_LoadsWhenRequired(string extension)
    {
        string json = $$"""
        { "asset": { "version": "2.0" }, "extensionsRequired": [ "{{extension}}" ], "scene": 0, "scenes": [ { "nodes": [] } ] }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.NotNull(ModelImporter.Load(stream, "gltf", ModelImporterSettings.Raw).Root);
    }
}
