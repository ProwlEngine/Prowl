// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Runtime.Test;

public class LODTests
{
    #region Mesh and geometry conversion

    /// <summary>Every triangle as its three corners, each corner as position, normal and UV, rotation independent.</summary>
    private static List<string> Corners(Mesh mesh)
    {
        var positions = mesh.Vertices;
        var normals = mesh.Normals;
        var uvs = mesh.UV;
        var indices = mesh.Indices;

        string Corner(uint i) => $"{positions[i]}|{normals[i]}|{uvs[i]}";

        var triangles = new List<string>();
        for (int i = 0; i < indices.Length; i += 3)
        {
            var corners = new[] { Corner(indices[i]), Corner(indices[i + 1]), Corner(indices[i + 2]) };
            int first = Array.IndexOf(corners, corners.Min(StringComparer.Ordinal));
            triangles.Add(string.Join(";", corners[first], corners[(first + 1) % 3], corners[(first + 2) % 3]));
        }
        triangles.Sort(StringComparer.Ordinal);
        return triangles;
    }

    [Fact]
    public void CubeWeldsToItsEightCorners()
    {
        using var cube = Mesh.CreateCube(Float3.One);

        GeometryData geometry = MeshGeometry.ToGeometryData(cube);

        Assert.Equal(8, geometry.Vertices.Count);
        Assert.Equal(12, geometry.Faces.Count);
    }

    [Fact]
    public void CubeRoundTripsThroughGeometry()
    {
        using var cube = Mesh.CreateCube(Float3.One);

        using Mesh back = MeshGeometry.ToMesh(MeshGeometry.ToGeometryData(cube), cube);

        Assert.Equal(cube.VertexCount, back.VertexCount);
        Assert.Equal(cube.IndexCount, back.IndexCount);
        Assert.Equal(Corners(cube), Corners(back));
    }

    [Fact]
    public void ByteColorsRoundTripExactly()
    {
        using var triangle = Mesh.CreateTriangle(Float3.Zero, Float3.UnitX, Float3.UnitY);
        triangle.Colors32 = [new Color32(200, 13, 255, 1), new Color32(0, 128, 77, 254), new Color32(99, 100, 101, 102)];

        using Mesh back = MeshGeometry.ToMesh(MeshGeometry.ToGeometryData(triangle), triangle);

        Assert.False(back.HasColors);
        Assert.Equal(triangle.Colors32.OrderBy(c => c.R), back.Colors32.OrderBy(c => c.R));
    }

    [Fact]
    public void NonTriangleMeshesAreRejected()
    {
        using var lines = new Mesh();
        lines.Vertices = [Float3.Zero, Float3.UnitX];
        lines.Topology = PrimitiveTopology.LineList;
        lines.Indices = [0, 1];

        Assert.Throws<InvalidOperationException>(() => MeshGeometry.ToGeometryData(lines));
    }

    #endregion

    #region Mesh LOD generation

    private const int GridCells = 16;

    /// <summary>
    /// Flat skinned grid with a blend shape and two submeshes, split down the middle. Skin weights and the
    /// blend shape offset are functions of X, so any vertex can be checked against where it sits.
    /// </summary>
    private static Mesh SkinnedGrid()
    {
        var positions = new List<Float3>();
        var normals = new List<Float3>();
        var uvs = new List<Float2>();
        var boneIndices = new List<Float4>();
        var boneWeights = new List<Float4>();
        var offsets = new List<Float3>();

        for (int z = 0; z <= GridCells; z++)
        {
            for (int x = 0; x <= GridCells; x++)
            {
                positions.Add(new Float3(x, 0, z));
                normals.Add(Float3.UnitY);
                uvs.Add(new Float2(x, z) / GridCells);
                boneIndices.Add(new Float4(0, 1, 0, 0));
                boneWeights.Add(ExpectedWeights(x));
                offsets.Add(ExpectedOffset(x));
            }
        }

        var left = new List<uint>();
        var right = new List<uint>();
        for (int z = 0; z < GridCells; z++)
        {
            for (int x = 0; x < GridCells; x++)
            {
                uint a = (uint)(z * (GridCells + 1) + x), b = a + 1, d = a + GridCells + 1, c = d + 1;
                var target = x < GridCells / 2 ? left : right;
                target.AddRange([a, d, c, a, c, b]);
            }
        }

        var mesh = new Mesh { Name = "SkinnedGrid" };
        mesh.Vertices = positions.ToArray();
        mesh.Normals = normals.ToArray();
        mesh.UV = uvs.ToArray();
        mesh.BoneIndices = boneIndices.ToArray();
        mesh.BoneWeights = boneWeights.ToArray();
        mesh.BindPoses = [Float4x4.Identity, Float4x4.Identity];
        mesh.BoneNames = ["Root", "Tip"];
        mesh.BlendShapes = [new BlendShape { Name = "Bend", Frames = [new BlendShapeFrame { Weight = 100f, DeltaVertices = offsets.ToArray() }] }];
        mesh.Indices = [.. left, .. right];
        mesh.SetSubMeshCount(2);
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, left.Count));
        mesh.SetSubMesh(1, new SubMeshDescriptor(left.Count, right.Count));
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Float4 ExpectedWeights(float x) => new(x / GridCells, 1f - x / GridCells, 0, 0);
    private static Float3 ExpectedOffset(float x) => new(0, x * 0.1f, 0);

    [Fact]
    public void SkinningAndBlendShapesStayOnTheirVertices()
    {
        using var source = SkinnedGrid();

        MeshLOD lod = MeshLODGenerator.Generate(source, 0.25f);
        using Mesh mesh = lod.Mesh;

        Assert.True(lod.Triangles < source.IndexCount / 3);
        Assert.Equal(source.BoneNames, mesh.BoneNames);
        Assert.Equal(2, mesh.BindPoses!.Length);
        Assert.Equal("Bend", mesh.GetBlendShapeName(0));

        var positions = mesh.Vertices;
        var weights = mesh.BoneWeights;
        var indices = mesh.BoneIndices;
        var offsets = mesh.BlendShapes[0].Frames[0].DeltaVertices;
        for (int i = 0; i < positions.Length; i++)
        {
            Assert.Equal(ExpectedWeights(positions[i].X), weights[i]);
            Assert.Equal(new Float4(0, 1, 0, 0), indices[i]);
            Assert.Equal(ExpectedOffset(positions[i].X), offsets[i]);
        }
    }

    [Fact]
    public void SubmeshesKeepTheirTrianglesAndOrder()
    {
        using var source = SkinnedGrid();

        using Mesh mesh = MeshLODGenerator.Generate(source, 0.1f).Mesh;

        Assert.Equal(2, mesh.SubMeshCount);
        var positions = mesh.Vertices;
        var indices = mesh.Indices;
        for (int s = 0; s < 2; s++)
        {
            var sub = mesh.GetSubMesh(s);
            Assert.True(sub.IndexCount > 0);
            for (int i = sub.IndexStart; i < sub.IndexStart + sub.IndexCount; i++)
            {
                float x = positions[indices[i]].X;
                Assert.True(s == 0 ? x <= GridCells / 2 : x >= GridCells / 2);
            }
        }
    }

    [Fact]
    public void ChainShrinksLevelByLevel()
    {
        using var sphere = Mesh.CreateSphere(1f, 32, 32);
        // Counted from the geometry, which drops the sphere's degenerate pole triangles
        int sourceTriangles = MeshGeometry.ToGeometryData(sphere).Faces.Count;

        var levels = MeshLODGenerator.GenerateChain(sphere, [0.5f, 0.25f, 0.1f]);

        Assert.Equal(3, levels.Count);
        Assert.InRange(levels[0].Triangles, sourceTriangles / 2 - 1, sourceTriangles / 2);
        Assert.True(levels[1].Triangles < levels[0].Triangles);
        Assert.True(levels[2].Triangles < levels[1].Triangles);
        Assert.True(levels[1].Error >= levels[0].Error);
        Assert.True(levels[2].Error >= levels[1].Error);
        Assert.All(levels, l => Assert.Equal(l.Triangles * 3, l.Mesh.IndexCount));

        foreach (var level in levels) level.Mesh.Dispose();
    }

    /// <summary>
    /// Flat n by n grid of quads. With a back, every triangle is repeated facing the other way on its own
    /// vertices with flipped normals, the way double sided cards are authored. With a skin split, the
    /// middle column is duplicated so triangles left of it are bound to bone 0 and right of it to bone 1.
    /// </summary>
    private static Mesh Card(int n, bool back = false, bool skinSplit = false)
    {
        var positions = new List<Float3>();
        var normals = new List<Float3>();
        var weights = new List<Float4>();
        var indices = new List<uint>();

        int AddVertex(int x, int z, Float3 normal, Float4 weight)
        {
            positions.Add(new Float3(x, 0, z));
            normals.Add(normal);
            weights.Add(weight);
            return positions.Count - 1;
        }

        int side = (n + 1) * (n + 1);
        var left = new Float4(1, 0, 0, 0);
        var right = new Float4(0, 1, 0, 0);
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
                AddVertex(x, z, Float3.UnitY, x <= n / 2 ? left : right);

        // The middle column's copies for the right hand triangles
        var split = new Dictionary<int, int>();
        if (skinSplit)
            for (int z = 0; z <= n; z++)
                split[z * (n + 1) + n / 2] = AddVertex(n / 2, z, Float3.UnitY, right);

        int backStart = positions.Count;
        if (back)
            for (int i = 0; i < side; i++)
                AddVertex((int)positions[i].X, (int)positions[i].Z, -Float3.UnitY, weights[i]);

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                int a = z * (n + 1) + x, b = a + 1, d = a + n + 1, c = d + 1;
                if (skinSplit && x >= n / 2)
                {
                    if (split.TryGetValue(a, out int sa)) a = sa;
                    if (split.TryGetValue(d, out int sd)) d = sd;
                }
                indices.AddRange([(uint)a, (uint)d, (uint)c, (uint)a, (uint)c, (uint)b]);
                if (back)
                    indices.AddRange([(uint)(backStart + a), (uint)(backStart + c), (uint)(backStart + d),
                                      (uint)(backStart + a), (uint)(backStart + b), (uint)(backStart + c)]);
            }
        }

        var mesh = new Mesh { Name = "Card" };
        mesh.Vertices = positions.ToArray();
        mesh.Normals = normals.ToArray();
        mesh.BoneIndices = Enumerable.Repeat(new Float4(0, 1, 0, 0), positions.Count).ToArray();
        mesh.BoneWeights = weights.ToArray();
        mesh.Indices = indices.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    [Fact]
    public void DoubleSidedCardsSimplifyBothSides()
    {
        using var card = Card(8, back: true);

        GeometryData geometry = MeshGeometry.ToGeometryData(card);
        Assert.Equal(2 * 81, geometry.Vertices.Count);

        MeshLOD lod = MeshLODGenerator.Generate(card, 0.25f);
        using Mesh mesh = lod.Mesh;
        Assert.InRange(lod.Triangles, geometry.Faces.Count / 4 - 1, geometry.Faces.Count / 4);
    }

    [Fact]
    public void SkinSplitsStayJoined()
    {
        const int n = 8;
        using var card = Card(n, skinSplit: true);

        // Both copies of the middle column weld into one vertex each, the split lives on the corners
        Assert.Equal((n + 1) * (n + 1), MeshGeometry.ToGeometryData(card).Vertices.Count);

        using Mesh mesh = MeshLODGenerator.Generate(card, 0.25f).Mesh;

        var positions = mesh.Vertices;
        var weights = mesh.BoneWeights;
        var leftSeam = new HashSet<Float3>();
        var rightSeam = new HashSet<Float3>();
        for (int i = 0; i < positions.Length; i++)
        {
            bool isLeft = weights[i].X == 1;
            Assert.True(isLeft ? positions[i].X <= n / 2 : positions[i].X >= n / 2);
            if (positions[i].X == n / 2) (isLeft ? leftSeam : rightSeam).Add(positions[i]);
        }

        // Whatever survives of the seam survives on both sides, so no crack opens between them
        Assert.NotEmpty(leftSeam);
        Assert.True(leftSeam.SetEquals(rightSeam));
    }

    [Fact]
    public void UnsetSubmeshSlotsAreSkipped()
    {
        using var triangle = Mesh.CreateTriangle(Float3.Zero, Float3.UnitX, Float3.UnitY);
        triangle.SetSubMeshCount(2);
        triangle.SetSubMesh(0, new SubMeshDescriptor(0, 3));

        using Mesh back = MeshGeometry.ToMesh(MeshGeometry.ToGeometryData(triangle), triangle);

        Assert.Equal(2, back.SubMeshCount);
        Assert.Equal(3, back.GetSubMesh(0).IndexCount);
        Assert.Equal(0, back.GetSubMesh(1).IndexCount);
    }

    [Fact]
    public void BoneIndicesAreRounded()
    {
        using var triangle = Mesh.CreateTriangle(Float3.Zero, Float3.UnitX, Float3.UnitY);
        var index = new Float4(2.9999998f, 1.0000001f, 0, 0);
        triangle.BoneIndices = [index, index, index];
        triangle.BoneWeights = [new Float4(0.5f, 0.5f, 0, 0), new Float4(0.5f, 0.5f, 0, 0), new Float4(0.5f, 0.5f, 0, 0)];

        using Mesh back = MeshGeometry.ToMesh(MeshGeometry.ToGeometryData(triangle), triangle);

        Assert.All(back.BoneIndices, i => Assert.Equal(new Float4(3, 1, 0, 0), i));
    }

    #endregion
}
