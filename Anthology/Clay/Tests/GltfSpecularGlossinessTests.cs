// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// KHR_materials_pbrSpecularGlossiness is converted into the metallic-roughness core so consumers
/// that only understand metal/rough get a usable surface. Without the conversion a spec/gloss
/// material lands on the all-defaults white fully-rough fully-metallic surface, which is a total
/// loss rather than a degradation.
/// </summary>
public sealed class GltfSpecularGlossinessTests
{
    /// <summary>
    /// Minimal glTF carrying one material. No mesh is needed: materials map independently of
    /// geometry, and the node mapper always produces a root.
    /// </summary>
    private static Model LoadWithMaterial(string materialJson)
    {
        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "extensionsUsed": [ "KHR_materials_pbrSpecularGlossiness" ],
          "scene": 0,
          "scenes": [ { "nodes": [] } ],
          "materials": [ {{materialJson}} ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ModelImporter.Load(stream, "gltf", ModelImporterSettings.Raw);
    }

    [Fact]
    public void PureDielectric_ConvertsToNonMetalWithTheDiffuseAsBaseColour()
    {
        // Low specular, coloured diffuse: the classic painted-surface spec/gloss material.
        var model = LoadWithMaterial("""
        {
          "name": "Painted",
          "extensions": { "KHR_materials_pbrSpecularGlossiness": {
            "diffuseFactor": [ 0.8, 0.2, 0.2, 1.0 ],
            "specularFactor": [ 0.04, 0.04, 0.04 ],
            "glossinessFactor": 0.25
          } }
        }
        """);

        var m = Assert.Single(model.Materials);

        Assert.True(m.Metallic < 0.05f, $"expected a dielectric, got metallic {m.Metallic}");
        Assert.Equal(0.75f, m.Roughness, 3);

        // Base colour should come back close to the diffuse it was derived from.
        Assert.Equal(0.8f, m.BaseColor.R, 1);
        Assert.True(m.BaseColor.R > m.BaseColor.G && m.BaseColor.R > m.BaseColor.B);
    }

    [Fact]
    public void PureMetal_ConvertsToMetallicWithTheSpecularAsBaseColour()
    {
        // Black diffuse with a bright coloured specular is how spec/gloss expresses a metal.
        var model = LoadWithMaterial("""
        {
          "name": "Gold",
          "extensions": { "KHR_materials_pbrSpecularGlossiness": {
            "diffuseFactor": [ 0.0, 0.0, 0.0, 1.0 ],
            "specularFactor": [ 1.0, 0.77, 0.34 ],
            "glossinessFactor": 0.9
          } }
        }
        """);

        var m = Assert.Single(model.Materials);

        Assert.True(m.Metallic > 0.9f, $"expected a metal, got metallic {m.Metallic}");
        Assert.Equal(0.1f, m.Roughness, 3);

        // The gold tint has to survive into base colour, or the metal renders grey.
        Assert.True(m.BaseColor.R > m.BaseColor.G && m.BaseColor.G > m.BaseColor.B);
        Assert.Equal(1.0f, m.BaseColor.R, 1);
    }

    [Fact]
    public void DiffuseTexture_BecomesTheBaseColourTexture()
    {
        var model = LoadWithMaterial("""
        {
          "name": "Textured",
          "extensions": { "KHR_materials_pbrSpecularGlossiness": {
            "diffuseTexture": { "index": 0, "texCoord": 1 },
            "glossinessFactor": 0.5
          } }
        }
        """);

        var m = Assert.Single(model.Materials);

        Assert.NotNull(m.BaseColorTexture);
        Assert.Equal(0, m.BaseColorTexture!.TextureIndex);
        Assert.Equal(1, m.BaseColorTexture.UVChannel);
    }

    // The extension is kept so a consumer that would rather shade the original still can.
    [Fact]
    public void TypedExtensionSurvivesTheConversion()
    {
        var model = LoadWithMaterial("""
        {
          "extensions": { "KHR_materials_pbrSpecularGlossiness": {
            "diffuseFactor": [ 0.5, 0.5, 0.5, 1.0 ],
            "glossinessFactor": 0.6
          } }
        }
        """);

        var m = Assert.Single(model.Materials);

        Assert.NotNull(m.SpecularGlossiness);
        Assert.Equal(0.6f, m.SpecularGlossiness!.GlossinessFactor, 3);
        Assert.DoesNotContain("KHR_materials_pbrSpecularGlossiness", m.RawExtensions.Keys);
    }

    /// <summary>
    /// An authored pbrMetallicRoughness block is the exporter's own fallback, produced with access
    /// to the source images, so it beats a factor-only reconstruction.
    /// </summary>
    [Fact]
    public void AuthoredMetallicRoughnessBlockWins()
    {
        var model = LoadWithMaterial("""
        {
          "pbrMetallicRoughness": {
            "baseColorFactor": [ 0.1, 0.2, 0.3, 1.0 ],
            "metallicFactor": 0.25,
            "roughnessFactor": 0.75
          },
          "extensions": { "KHR_materials_pbrSpecularGlossiness": {
            "diffuseFactor": [ 0.9, 0.9, 0.9, 1.0 ],
            "specularFactor": [ 1.0, 1.0, 1.0 ],
            "glossinessFactor": 1.0
          } }
        }
        """);

        var m = Assert.Single(model.Materials);

        Assert.Equal(0.25f, m.Metallic, 3);
        Assert.Equal(0.75f, m.Roughness, 3);
        Assert.Equal(0.1f, m.BaseColor.R, 3);
    }

    // Nothing to convert means nothing is touched: a plain metal/rough material keeps its values.
    [Fact]
    public void MaterialWithoutTheExtensionIsUnchanged()
    {
        var model = LoadWithMaterial("""
        {
          "pbrMetallicRoughness": { "metallicFactor": 1.0, "roughnessFactor": 0.3 }
        }
        """);

        var m = Assert.Single(model.Materials);

        Assert.Null(m.SpecularGlossiness);
        Assert.Equal(1.0f, m.Metallic, 3);
        Assert.Equal(0.3f, m.Roughness, 3);
    }
}
