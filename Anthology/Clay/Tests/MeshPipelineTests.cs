// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;
using Prowl.Vector;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// Triangulation and welding both rewrite topology, and both had a way to hand the GPU something
/// it cannot draw: an n-gon whose triangles come back wound the wrong way, and a face naming one
/// vertex twice.
/// </summary>
public sealed class MeshPipelineTests
{
    private static Model LoadObj(string objText, PostProcessFlags flags)
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(objText));
        return ModelImporter.Load(ms, "obj", ModelImporterSettings.Raw with { PostProcess = flags });
    }

    /// <summary>Geometric normal of the triangle starting at <paramref name="tri"/>.</summary>
    private static Float3 FaceNormal(Mesh mesh, int tri)
    {
        Float3 a = mesh.Vertices[(int)mesh.Indices[tri * 3 + 0]];
        Float3 b = mesh.Vertices[(int)mesh.Indices[tri * 3 + 1]];
        Float3 c = mesh.Vertices[(int)mesh.Indices[tri * 3 + 2]];
        return Float3.Cross(b - a, c - a);
    }


    // A pentagon, so it takes the ear-clipping path rather than the convex-quad shortcut.
    private const string PentagonCcw = """
    v 0 0 0
    v 2 0 0
    v 3 1.5 0
    v 1 2.5 0
    v -1 1.5 0
    f 1 2 3 4 5
    """;

    // The same pentagon listed the other way round, so its normal points down the negative Z axis.
    private const string PentagonCw = """
    v 0 0 0
    v -1 1.5 0
    v 1 2.5 0
    v 3 1.5 0
    v 2 0 0
    f 1 2 3 4 5
    """;

    [Fact]
    public void CounterClockwisePolygon_TriangulatesFacingTheSameWay()
    {
        var mesh = LoadObj(PentagonCcw, PostProcessFlags.Triangulate).Meshes[0];

        Assert.Equal(3, mesh.Indices.Length / 3);
        for (int t = 0; t < 3; t++)
            Assert.True(FaceNormal(mesh, t).Z > 0f, $"triangle {t} faces the wrong way");
    }

    /// <summary>
    /// The projection drops the dominant axis by magnitude and ignores its sign, so a polygon facing
    /// the negative side of that axis projects clockwise. Ear clipping walks it backwards to get a
    /// counter-clockwise ring, and used to emit the triangles in that walking order, which is the
    /// reverse of the source polygon. Half of all n-gons land in this case, and they imported with
    /// flipped normals.
    /// </summary>
    [Fact]
    public void ClockwisePolygon_TriangulatesFacingTheSameWay()
    {
        var mesh = LoadObj(PentagonCw, PostProcessFlags.Triangulate).Meshes[0];

        Assert.Equal(3, mesh.Indices.Length / 3);
        for (int t = 0; t < 3; t++)
            Assert.True(FaceNormal(mesh, t).Z < 0f, $"triangle {t} faces the wrong way");
    }

    // A concave polygon exercises the skip path in the ear-clipping loop rather than clipping each
    // vertex in turn, and it must keep its facing too.
    [Fact]
    public void ConcavePolygon_TriangulatesFacingTheSameWay()
    {
        const string arrowhead = """
        v 0 0 0
        v 4 0 0
        v 4 4 0
        v 2 1 0
        v 0 4 0
        f 1 2 3 4 5
        """;

        var mesh = LoadObj(arrowhead, PostProcessFlags.Triangulate).Meshes[0];

        Assert.Equal(3, mesh.Indices.Length / 3);
        for (int t = 0; t < 3; t++)
            Assert.True(FaceNormal(mesh, t).Z > 0f, $"triangle {t} faces the wrong way");
    }

    [Fact]
    public void ConvexQuad_KeepsItsFacing()
    {
        const string quad = """
        v 0 0 0
        v 1 0 0
        v 1 1 0
        v 0 1 0
        f 1 2 3 4
        """;

        var mesh = LoadObj(quad, PostProcessFlags.Triangulate).Meshes[0];

        Assert.Equal(2, mesh.Indices.Length / 3);
        Assert.True(FaceNormal(mesh, 0).Z > 0f);
        Assert.True(FaceNormal(mesh, 1).Z > 0f);
    }


    /// <summary>
    /// Welding collapses two coincident vertices onto one index, which turns any face spanning both
    /// into a face naming that vertex twice. The degenerate pass runs before welding, not after, and
    /// GameFast does not run it at all, so those reached the GPU.
    /// </summary>
    [Fact]
    public void FaceCollapsedByWelding_IsDropped()
    {
        const string obj = """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        v 1 0 0
        f 1 2 3
        f 1 2 4
        """;

        var mesh = LoadObj(obj, PostProcessFlags.JoinIdenticalVertices).Meshes[0];

        Assert.Equal(1, mesh.Indices.Length / 3);
    }

    [Fact]
    public void FacesUnaffectedByWelding_Survive()
    {
        const string obj = """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        v 1 1 0
        f 1 2 3
        f 2 4 3
        """;

        var mesh = LoadObj(obj, PostProcessFlags.JoinIdenticalVertices).Meshes[0];

        Assert.Equal(2, mesh.Indices.Length / 3);
        Assert.Equal(4, mesh.Vertices.Length);
    }

    // Welding still does its job: the shared edge's two pairs of coincident vertices become one each.
    [Fact]
    public void CoincidentVerticesAcrossFaces_AreWelded()
    {
        const string obj = """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        v 1 0 0
        v 0 1 0
        v 1 1 0
        f 1 2 3
        f 4 6 5
        """;

        var mesh = LoadObj(obj, PostProcessFlags.JoinIdenticalVertices).Meshes[0];

        Assert.Equal(4, mesh.Vertices.Length);
        Assert.Equal(2, mesh.Indices.Length / 3);
    }


    /// <summary>
    /// Six vertices making two triangles, the second three sitting exactly on the first three. One
    /// morph target moves them by <paramref name="secondTriangleDelta"/> along X, so the two halves
    /// are identical in every attribute except their deltas.
    /// </summary>
    private static Model LoadMorphPair(float secondTriangleDelta)
    {
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0];
        float d = secondTriangleDelta;
        float[] deltas = [0, 0, 0, 0, 0, 0, 0, 0, 0, d, 0, 0, d, 0, 0, d, 0, 0];

        var bytes = new List<byte>();
        void AddFloats(float[] v)
        {
            var b = new byte[v.Length * 4];
            Buffer.BlockCopy(v, 0, b, 0, b.Length);
            bytes.AddRange(b);
        }
        AddFloats(positions);
        AddFloats(deltas);
        int indicesOffset = bytes.Count;
        for (int i = 0; i < 6; i++) bytes.AddRange(BitConverter.GetBytes((ushort)i));
        bytes.AddRange(new byte[4]);

        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          "nodes": [ { "name": "N", "mesh": 0 } ],
          "meshes": [ { "name": "Morphed", "primitives": [ {
            "attributes": { "POSITION": 0 },
            "targets": [ { "POSITION": 1 } ],
            "indices": 2
          } ] } ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 6, "type": "VEC3" },
            { "bufferView": 1, "componentType": 5126, "count": 6, "type": "VEC3" },
            { "bufferView": 2, "componentType": 5123, "count": 6, "type": "SCALAR" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 72 },
            { "buffer": 0, "byteOffset": 72, "byteLength": 72 },
            { "buffer": 0, "byteOffset": {{indicesOffset}}, "byteLength": 12 }
          ],
          "buffers": [ { "byteLength": {{bytes.Count}}, "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(bytes.ToArray())}}" } ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ModelImporter.Load(ms, "gltf", ModelImporterSettings.Raw with { PostProcess = PostProcessFlags.JoinIdenticalVertices });
    }

    /// <summary>
    /// Morph deltas left the weld hash so a face mesh does not pay to read every shape and frame for
    /// every vertex. They are still part of the merge predicate, so two vertices that agree on
    /// everything else and differ only in their deltas have to survive as two.
    /// </summary>
    [Fact]
    public void VerticesDifferingOnlyInMorphDeltas_StayDistinct()
    {
        var mesh = LoadMorphPair(secondTriangleDelta: 5f).Meshes[0];

        Assert.Equal(6, mesh.Vertices.Length);
    }

    [Fact]
    public void VerticesAgreeingOnTheirMorphDeltas_StillWeld()
    {
        var mesh = LoadMorphPair(secondTriangleDelta: 0f).Meshes[0];

        Assert.Equal(3, mesh.Vertices.Length);
    }
}
