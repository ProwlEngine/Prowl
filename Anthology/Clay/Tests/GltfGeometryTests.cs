using Prowl.Clay;
using Prowl.Clay.Importer;
using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// End-to-end tests against the Khronos glTF sample models for core geometry, coordinate
/// conversion, and textured-material round trips. Animation and PBR-extension coverage live in
/// <see cref="GltfAnimationTests"/> and <see cref="GltfMaterialExtensionsTests"/>.
/// </summary>
public sealed class GltfGeometryTests
{
    [Fact]
    public void Box_Glb_LoadsAsUnitCube_WithExpectedTopology()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/Box/glTF-Binary/Box.glb"));

        Assert.Equal("glb", model.Metadata.Format);
        Assert.Equal("2.0", model.Metadata.FormatVersion);
        Assert.Single(model.Meshes);
        Assert.Single(model.Materials);

        var mesh = model.Meshes[0];
        // A cube authored with per-face normals expands to 4 unique attribute tuples per face
        // times 6 faces = 24 vertices, two triangles per face = 36 indices.
        Assert.Equal(24, mesh.VertexCount);
        Assert.Single(mesh.SubMeshes);
        Assert.Equal(36, mesh.SubMeshes[0].IndexCount);
        Assert.Equal(PrimitiveTopology.Triangles, mesh.SubMeshes[0].Topology);

        // Box.glb is centered at origin with half-extent 0.5.
        Assert.InRange(mesh.Bounds.Min.X, -0.6f, -0.4f);
        Assert.InRange(mesh.Bounds.Max.X,  0.4f,  0.6f);
        Assert.InRange(mesh.Bounds.Min.Y, -0.6f, -0.4f);
        Assert.InRange(mesh.Bounds.Max.Y,  0.4f,  0.6f);
    }

    [Fact]
    public void ConvertCoordinateSystem_FlipsZ_LeavesXAndYAlone()
    {
        string path = TestModels.Gltf("2.0/Box/glTF-Binary/Box.glb");

        var raw = ModelImporter.Load(path, ModelImporterSettings.Raw);
        var converted = ModelImporter.Load(path, ModelImporterSettings.Raw with
        {
            PostProcess = PostProcessFlags.ConvertCoordinateSystem,
        });

        var rawMesh = raw.Meshes[0];
        var convMesh = converted.Meshes[0];
        Assert.Equal(rawMesh.VertexCount, convMesh.VertexCount);

        for (int i = 0; i < rawMesh.VertexCount; i++)
        {
            Assert.Equal( rawMesh.Vertices[i].X, convMesh.Vertices[i].X, precision: 5);
            Assert.Equal( rawMesh.Vertices[i].Y, convMesh.Vertices[i].Y, precision: 5);
            Assert.Equal(-rawMesh.Vertices[i].Z, convMesh.Vertices[i].Z, precision: 5);
        }
    }

    [Fact]
    public void Glb_EmbeddedTexture_LandsInEncodedBytes_NotSourcePath()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/BoxTextured/glTF-Binary/BoxTextured.glb"));

        Assert.Single(model.Textures);
        var tex = model.Textures[0];
        Assert.Null(tex.SourcePath);
        Assert.NotNull(tex.EncodedBytes);
        // PNG magic bytes 0x89 0x50 0x4E 0x47.
        Assert.Equal(0x89, tex.EncodedBytes![0]);
        Assert.Equal(0x50, tex.EncodedBytes[1]);
        Assert.Equal(0x4E, tex.EncodedBytes[2]);
        Assert.Equal(0x47, tex.EncodedBytes[3]);
    }

    [Fact]
    public void Gltf_ExternalTexture_LandsInSourcePath_NotEncodedBytes()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/BoxTextured/glTF/BoxTextured.gltf"));

        Assert.Single(model.Textures);
        var tex = model.Textures[0];
        Assert.Null(tex.EncodedBytes);
        Assert.NotNull(tex.SourcePath);
        Assert.True(File.Exists(tex.SourcePath));
    }

    [Fact]
    public void DamagedHelmet_PopulatesAllFivePbrTextureSlots()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/DamagedHelmet/glTF-Binary/DamagedHelmet.glb"));

        Assert.Single(model.Materials);
        Assert.Equal(5, model.Textures.Count);

        var mat = model.Materials[0];
        Assert.NotNull(mat.BaseColorTexture);
        Assert.NotNull(mat.NormalTexture);
        Assert.NotNull(mat.OcclusionTexture);
        Assert.NotNull(mat.MetallicRoughnessTexture);
        Assert.NotNull(mat.EmissiveTexture);
        Assert.False(mat.Unlit);
    }
}
