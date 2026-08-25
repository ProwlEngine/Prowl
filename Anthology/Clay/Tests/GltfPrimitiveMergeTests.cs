// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// A glTF primitive is a material batch, not an object. One authored model with several material
/// slots exports as one mesh of several primitives, so the primitives have to merge back into one
/// mesh with one sub-mesh per material rather than becoming sibling objects in the scene.
/// </summary>
public sealed class GltfPrimitiveMergeTests
{
    /// <summary>
    /// One mesh, two primitives, two materials, the way an exporter writes a model with two
    /// material slots: each primitive owns its own slice of the position buffer and indexes from
    /// zero within it, so the merge has to shift the second one's indices by the first one's
    /// vertex count.
    /// </summary>
    private static Model LoadTwoPrimitiveMesh(ModelImporterSettings? settings = null)
    {
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0, 2, 0, 0, 3, 0, 0, 2, 1, 0];
        ushort[] indices = [0, 1, 2];

        var bytes = new byte[positions.Length * 4 + 6];
        Buffer.BlockCopy(positions, 0, bytes, 0, positions.Length * 4);
        Buffer.BlockCopy(indices, 0, bytes, positions.Length * 4, 6);

        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          "nodes": [ { "name": "Object", "mesh": 0 } ],
          "meshes": [ { "name": "Object", "primitives": [
            { "attributes": { "POSITION": 0 }, "indices": 2, "material": 0 },
            { "attributes": { "POSITION": 1 }, "indices": 2, "material": 1 }
          ] } ],
          "materials": [ { "name": "Red" }, { "name": "Blue" } ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 2, "componentType": 5123, "count": 3, "type": "SCALAR" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0,  "byteLength": 36 },
            { "buffer": 0, "byteOffset": 36, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 72, "byteLength": 6 }
          ],
          "buffers": [ { "byteLength": 78, "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(bytes)}}" } ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ModelImporter.Load(stream, "gltf", settings ?? ModelImporterSettings.Raw);
    }

    [Fact]
    public void PrimitivesBecomeOneMesh()
    {
        var model = LoadTwoPrimitiveMesh();

        var mesh = Assert.Single(model.Meshes);
        Assert.Equal(6, mesh.VertexCount);
        Assert.Equal("Object", mesh.Name);
    }

    [Fact]
    public void EachPrimitiveBecomesItsOwnSubMesh()
    {
        var mesh = Assert.Single(LoadTwoPrimitiveMesh().Meshes);

        Assert.Equal(2, mesh.SubMeshes.Length);
        Assert.Equal(0, mesh.SubMeshes[0].MaterialIndex);
        Assert.Equal(1, mesh.SubMeshes[1].MaterialIndex);

        foreach (var sub in mesh.SubMeshes)
        {
            Assert.Equal(PrimitiveTopology.Triangles, sub.Topology);
            Assert.Equal(3, sub.IndexCount);
        }
    }

    // Each sub-mesh has to address the vertices of the primitive it came from, which is what the
    // per-primitive vertex offset in the merge is for.
    [Fact]
    public void SubMeshIndicesAddressTheRightVertices()
    {
        var mesh = Assert.Single(LoadTwoPrimitiveMesh().Meshes);

        var firstIndices = mesh.GetIndices32(0);
        var secondIndices = mesh.GetIndices32(1);

        Assert.All(firstIndices, i => Assert.True(i < 3, $"index {i} should belong to the first primitive"));
        Assert.All(secondIndices, i => Assert.True(i >= 3, $"index {i} should belong to the second primitive"));
    }

    // The sibling nodes that used to be synthesised per extra primitive are gone.
    [Fact]
    public void NoExtraNodesAreSynthesised()
    {
        var model = LoadTwoPrimitiveMesh();

        Assert.DoesNotContain(model.Nodes, n => n.Name.Contains("_prim"));

        var meshNodes = model.Nodes.Where(n => n.MeshIndex >= 0).ToList();
        var node = Assert.Single(meshNodes);
        Assert.Equal("Object", node.Name);
    }

    [Fact]
    public void SinglePrimitiveMeshKeepsItsOwnName()
    {
        // The old mapper named every mesh "<parent>/prim0" whether or not it had siblings.
        var model = ModelImporter.Load(TestModels.Gltf("2.0/Box/glTF-Binary/Box.glb"));

        foreach (var mesh in model.Meshes)
            Assert.DoesNotContain("/prim", mesh.Name);
    }

    [Fact]
    public void MergeSurvivesTheFullGameQualityPipeline()
    {
        var model = LoadTwoPrimitiveMesh(ModelImporterSettings.GameQuality);
        var mesh = Assert.Single(model.Meshes);

        // Welding, degenerate removal and the rest must not lose the per-face material split.
        Assert.Equal(2, mesh.SubMeshes.Length);
        Assert.Equal(0, mesh.SubMeshes[0].MaterialIndex);
        Assert.Equal(1, mesh.SubMeshes[1].MaterialIndex);
    }

    [Fact]
    public void EditorMaxQuality_KeepsTheMaterialSplitThroughCacheReordering()
    {
        // ImproveCacheLocality shuffles triangles, so it has to carry each one's material with it.
        var model = LoadTwoPrimitiveMesh(ModelImporterSettings.EditorMaxQuality);
        var mesh = Assert.Single(model.Meshes);

        Assert.Equal(2, mesh.SubMeshes.Length);
        var byMaterial = mesh.SubMeshes.ToDictionary(s => s.MaterialIndex);
        Assert.True(byMaterial.ContainsKey(0));
        Assert.True(byMaterial.ContainsKey(1));

        foreach (var sub in mesh.SubMeshes)
            foreach (uint i in mesh.GetIndices32(Array.IndexOf(mesh.SubMeshes, sub)))
                Assert.True(i < mesh.VertexCount);
    }

    /// <summary>
    /// A real multi-primitive model, to check the merge against something not hand-written.
    /// </summary>
    [Fact]
    public void RealModelsMergeWithoutLosingGeometry()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/DamagedHelmet/glTF-Binary/DamagedHelmet.glb"));

        foreach (var mesh in model.Meshes)
        {
            Assert.NotEmpty(mesh.SubMeshes);
            foreach (var sub in mesh.SubMeshes)
            {
                Assert.True(sub.IndexCount > 0);
                Assert.True(sub.IndexStart + sub.IndexCount <= mesh.Indices.Length);
                for (int i = 0; i < sub.IndexCount; i++)
                    Assert.True(mesh.Indices[sub.IndexStart + i] < mesh.VertexCount);
            }
        }
    }
}
