// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;
using Prowl.Vector;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// GenerateNormals and GenerateSmoothNormals. Both are the same algorithm at different smoothing
/// angles, so what is worth pinning is the split behaviour: flat shading only works if the vertices
/// along a hard edge are duplicated, since one vertex can only carry one normal.
/// </summary>
public sealed class NormalGenerationTests
{
    /// <summary>
    /// Two triangles meeting at a right angle along a shared edge, with no NORMAL attribute.
    /// Positions: an L-shaped fold, one quad in the XZ plane and one rising in Y off its far edge.
    /// </summary>
    private static Model LoadFoldedPair(PostProcessFlags extraFlags, float smoothAngle = 80f)
    {
        // 4 positions, 2 triangles. Vertices 1 and 2 are shared across the fold.
        //   v0 (0,0,0)  v1 (1,0,0)  v2 (0,0,1)   -> floor triangle, normal +Y
        //   v3 (0,1,1)                            -> wall triangle (v1, v2, v3), steeply turned
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 1];
        ushort[] indices = [0, 1, 2, 1, 2, 3];

        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          "nodes": [ { "mesh": 0 } ],
          "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1 } ] } ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 4, "type": "VEC3" },
            { "bufferView": 1, "componentType": 5123, "count": 6, "type": "SCALAR" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 48 },
            { "buffer": 0, "byteOffset": 48, "byteLength": 12 }
          ],
          "buffers": [ { "byteLength": 60, "uri": "data:application/octet-stream;base64,{{Base64(positions, indices)}}" } ]
        }
        """;

        var settings = new ModelImporterSettings
        {
            // Deliberately no JoinIdenticalVertices: the split has to be observable on its own.
            PostProcess = PostProcessFlags.Triangulate | extraFlags,
            SmoothNormalsAngleDeg = smoothAngle,
        };

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ModelImporter.Load(stream, "gltf", settings);
    }

    private static string Base64(float[] positions, ushort[] indices)
    {
        var bytes = new byte[positions.Length * 4 + indices.Length * 2];
        Buffer.BlockCopy(positions, 0, bytes, 0, positions.Length * 4);
        Buffer.BlockCopy(indices, 0, bytes, positions.Length * 4, indices.Length * 2);
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public void WithoutTheFlag_NormalsStayAbsent()
    {
        var mesh = Assert.Single(LoadFoldedPair(PostProcessFlags.None).Meshes);
        Assert.Null(mesh.Normals);
    }

    [Fact]
    public void GenerateNormals_ProducesUnitNormalsForEveryVertex()
    {
        var mesh = Assert.Single(LoadFoldedPair(PostProcessFlags.GenerateNormals).Meshes);

        Assert.NotNull(mesh.Normals);
        Assert.Equal(mesh.VertexCount, mesh.Normals!.Length);
        foreach (var n in mesh.Normals)
            Assert.Equal(1f, Float3.Length(n), 3);
    }

    // The whole point of flat shading: a vertex on the fold cannot carry both faces' normals, so it
    // has to be duplicated. This is what the old Prowl-side fallback could not do.
    [Fact]
    public void GenerateNormals_SplitsTheSharedEdge()
    {
        var mesh = Assert.Single(LoadFoldedPair(PostProcessFlags.GenerateNormals).Meshes);

        Assert.True(mesh.VertexCount > 4,
            $"expected the fold to split shared vertices, still {mesh.VertexCount}");

        // Every corner's normal must match its own face normal, within floating point.
        AssertEveryCornerMatchesItsFaceNormal(mesh, tolerance: 1e-3f);
    }

    [Fact]
    public void GenerateSmoothNormals_BelowTheAngle_SharesOneNormal()
    {
        // 180 degrees smooths across anything, so the fold merges and no vertex is duplicated.
        var mesh = Assert.Single(LoadFoldedPair(PostProcessFlags.GenerateSmoothNormals, smoothAngle: 180f).Meshes);

        Assert.Equal(4, mesh.VertexCount);
        foreach (var n in mesh.Normals!)
            Assert.Equal(1f, Float3.Length(n), 3);
    }

    [Fact]
    public void GenerateSmoothNormals_AboveTheAngle_SplitsLikeAHardEdge()
    {
        // The fold is 90 degrees, so a 30 degree threshold has to break it.
        var mesh = Assert.Single(LoadFoldedPair(PostProcessFlags.GenerateSmoothNormals, smoothAngle: 30f).Meshes);

        Assert.True(mesh.VertexCount > 4, "a 90 degree fold must not smooth under a 30 degree threshold");
        AssertEveryCornerMatchesItsFaceNormal(mesh, tolerance: 1e-3f);
    }

    // Both flags set is not an error: smooth runs first and wins, and the flat step finds the
    // normals already present.
    [Fact]
    public void BothFlags_SmoothWins()
    {
        var flags = PostProcessFlags.GenerateNormals | PostProcessFlags.GenerateSmoothNormals;
        var mesh = Assert.Single(LoadFoldedPair(flags, smoothAngle: 180f).Meshes);

        Assert.Equal(4, mesh.VertexCount);
    }

    [Fact]
    public void AuthoredNormalsAreLeftAlone()
    {
        // CesiumMan ships normals; generation must not touch them.
        var withFlag = ModelImporter.Load(TestModels.Gltf("2.0/CesiumMan/glTF-Binary/CesiumMan.glb"),
            new ModelImporterSettings { PostProcess = PostProcessFlags.Triangulate | PostProcessFlags.GenerateSmoothNormals });
        var without = ModelImporter.Load(TestModels.Gltf("2.0/CesiumMan/glTF-Binary/CesiumMan.glb"),
            new ModelImporterSettings { PostProcess = PostProcessFlags.Triangulate });

        Assert.Equal(without.Meshes.Count, withFlag.Meshes.Count);
        for (int i = 0; i < withFlag.Meshes.Count; i++)
        {
            Assert.Equal(without.Meshes[i].VertexCount, withFlag.Meshes[i].VertexCount);
            Assert.Equal(without.Meshes[i].Normals, withFlag.Meshes[i].Normals);
        }
    }

    [Fact]
    public void RecalculateNormals_ReplacesAuthoredOnes()
    {
        var settings = new ModelImporterSettings
        {
            PostProcess = PostProcessFlags.Triangulate | PostProcessFlags.GenerateSmoothNormals,
            RecalculateNormals = true,
            SmoothNormalsAngleDeg = 180f,
        };
        var model = ModelImporter.Load(TestModels.Gltf("2.0/CesiumMan/glTF-Binary/CesiumMan.glb"), settings);

        foreach (var mesh in model.Meshes)
        {
            Assert.NotNull(mesh.Normals);
            foreach (var n in mesh.Normals!)
                Assert.Equal(1f, Float3.Length(n), 2);
        }
    }

    private static void AssertEveryCornerMatchesItsFaceNormal(Mesh mesh, float tolerance)
    {
        foreach (var sub in mesh.SubMeshes)
        {
            if (sub.Topology != PrimitiveTopology.Triangles) continue;

            for (int i = 0; i < sub.IndexCount; i += 3)
            {
                int a = (int)mesh.Indices[sub.IndexStart + i];
                int b = (int)mesh.Indices[sub.IndexStart + i + 1];
                int c = (int)mesh.Indices[sub.IndexStart + i + 2];

                Float3 faceNormal = Float3.Normalize(Float3.Cross(
                    mesh.Vertices[b] - mesh.Vertices[a],
                    mesh.Vertices[c] - mesh.Vertices[a]));

                foreach (int v in new[] { a, b, c })
                    Assert.True(Float3.Dot(mesh.Normals![v], faceNormal) > 1f - tolerance,
                        $"vertex {v} normal {mesh.Normals[v]} does not match its face normal {faceNormal}");
            }
        }
    }
}
