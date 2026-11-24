// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

public class GeometryDissolveTests
{
    // A flat sheet of quads, columns by rows, in the XY plane
    private static GeometryData Grid(int columns, int rows)
    {
        GeometryData mesh = new();
        for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
                mesh.AddVertex(x, y, 0);
        int stride = columns + 1;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
                mesh.AddFace(y * stride + x, y * stride + x + 1, (y + 1) * stride + x + 1, (y + 1) * stride + x);
        return mesh;
    }

    [Fact]
    public void DissolveEdge_JoinsItsTwoFaces()
    {
        GeometryData mesh = Grid(2, 1);
        GeometryData.Edge middle = mesh.FindEdge(mesh.Vertices[1], mesh.Vertices[4])!;

        Assert.Equal(1, GeometryOperators.DissolveEdges(mesh, [middle]));

        // The ends of the edge only joined two edges afterwards, so they went too
        Assert.Single(mesh.Faces);
        Assert.Equal(4, mesh.Faces[0].VertCount);
        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal(4, mesh.Edges.Count);
        Assert.True(mesh.Faces[0].Normal().Z > 0.99f);
    }

    [Fact]
    public void DissolveEdge_OnABorder_IsLeftAlone()
    {
        GeometryData mesh = Grid(1, 1);

        Assert.Equal(0, GeometryOperators.DissolveEdges(mesh, mesh.Edges.ToList()));

        Assert.Single(mesh.Faces);
        Assert.Equal(4, mesh.Edges.Count);
    }

    [Fact]
    public void DissolveVertex_Inside_LeavesOneFace()
    {
        GeometryData mesh = Grid(2, 2);

        Assert.Equal(1, GeometryOperators.DissolveVertices(mesh, [mesh.Vertices[4]]));

        Assert.Single(mesh.Faces);
        Assert.Equal(8, mesh.Faces[0].VertCount);
        Assert.Equal(8, mesh.Vertices.Count);
        Assert.Equal(8, mesh.Edges.Count);
    }

    [Fact]
    public void DissolveVertex_BetweenTwoEdges_JoinsThem()
    {
        GeometryData mesh = new();
        Float3[] points = [new(0, 0, 0), new(2, 0, 0), new(2, 2, 0), new(1, 3, 0), new(0, 2, 0)];
        mesh.AddFace(points.Select(p => mesh.AddVertex(p)).ToArray());

        Assert.Equal(1, GeometryOperators.DissolveVertices(mesh, [mesh.Vertices[3]]));

        Assert.Single(mesh.Faces);
        Assert.Equal(4, mesh.Faces[0].VertCount);
        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal(4, mesh.Edges.Count);
        Assert.True(mesh.Faces[0].Normal().Z > 0.99f);
    }

    [Fact]
    public void DissolveVertex_OfATriangle_IsLeftAlone()
    {
        GeometryData mesh = TestUtil.MakeTriangle();

        Assert.Equal(0, GeometryOperators.DissolveVertices(mesh, [mesh.Vertices[0]]));

        Assert.Single(mesh.Faces);
        Assert.Equal(3, mesh.Vertices.Count);
    }

    [Fact]
    public void DissolveVertex_OnABorderWithThreeEdges_IsLeftAlone()
    {
        GeometryData mesh = Grid(2, 1);

        Assert.Equal(0, GeometryOperators.DissolveVertices(mesh, [mesh.Vertices[1]]));

        Assert.Equal(2, mesh.Faces.Count);
    }

    [Fact]
    public void DissolveFaces_MergesAPatch_AndKeepsCornerAttributes()
    {
        GeometryData mesh = Grid(2, 2);
        mesh.AddLoopAttribute(GeometryData.UvAttribute, GeometryData.AttributeBaseType.Float, 2);
        foreach (GeometryData.Loop loop in mesh.Loops)
            loop.Attributes[GeometryData.UvAttribute] = new GeometryData.FloatAttributeValue(loop.Vert.Point.X, loop.Vert.Point.Y);

        var merged = GeometryOperators.DissolveFaces(mesh, mesh.Faces.ToList());

        GeometryData.Face face = Assert.Single(merged);
        Assert.Single(mesh.Faces);
        Assert.Equal(8, face.VertCount);
        Assert.True(face.Normal().Z > 0.99f);
        GeometryData.Loop corner = face.Loop!;
        for (int i = 0; i < 8; i++)
        {
            float[] uv = corner.Attributes[GeometryData.UvAttribute].AsFloat()!.Data;
            Assert.Equal(corner.Vert.Point.X, uv[0]);
            Assert.Equal(corner.Vert.Point.Y, uv[1]);
            corner = corner.Next!;
        }
    }

    [Fact]
    public void DissolveFaces_LeavesARing_Alone()
    {
        GeometryData mesh = Grid(3, 3);
        var ring = mesh.Faces.Where((_, i) => i != 4).ToList();

        Assert.Empty(GeometryOperators.DissolveFaces(mesh, ring));

        Assert.Equal(9, mesh.Faces.Count);
    }

    [Fact]
    public void DissolveFaces_TreatsSeparatePatches_Apart()
    {
        GeometryData mesh = Grid(5, 1);
        var faces = new[] { mesh.Faces[0], mesh.Faces[1], mesh.Faces[3], mesh.Faces[4] };

        Assert.Equal(2, GeometryOperators.DissolveFaces(mesh, faces).Count);

        Assert.Equal(3, mesh.Faces.Count);
    }

    // Every face fanned into triangles, the way a mesh asset arrives
    private static GeometryData Triangulated(GeometryData source)
    {
        Float3[] points = source.Vertices.Select(v => v.Point).ToArray();
        var index = source.Vertices.Select((v, i) => (v, i)).ToDictionary(p => p.v, p => p.i);
        var triangles = new System.Collections.Generic.List<int>();
        foreach (GeometryData.Face face in source.Faces)
        {
            var ring = face.NeighborVertices();
            for (int i = 1; i + 1 < ring.Count; i++) triangles.AddRange([index[ring[0]], index[ring[i]], index[ring[i + 1]]]);
        }
        return GeometryGenerator.TriangleMesh(points, [.. triangles]);
    }

    [Fact]
    public void DissolveFlat_JoinsATriangulatedBoxIntoSixFaces()
    {
        GeometryData mesh = Triangulated(GeometryGenerator.Box(new Float3(2f, 1f, 3f), segments: new Int3(3, 2, 4)));
        float volume = mesh.Volume();

        GeometryOperators.DissolveFlat(mesh, 0.5f, 1e-5f);

        Assert.Equal(6, mesh.Faces.Count);
        Assert.Equal(8, mesh.Vertices.Count);
        Assert.True(mesh.IsClosed());
        Assert.Equal(volume, mesh.Volume(), 4);
    }

    [Theory]
    [InlineData(800)]
    [InlineData(1000)]
    public void DissolveFlat_KeepsAGentleCurve(int segments)
    {
        GeometryData mesh = Triangulated(GeometryGenerator.Cylinder(1f, 2f, segments: segments));
        float volume = mesh.Volume();

        GeometryOperators.DissolveFlat(mesh, 0.5f, 2e-5f);

        // The caps join, the sides stay round
        Assert.True(mesh.Faces.Count >= segments / 2 + 2, $"{mesh.Faces.Count} faces");
        Assert.True(mesh.IsClosed());
        Assert.True(MathF.Abs(mesh.Volume() - volume) < volume * 1e-3f, $"{mesh.Volume()} against {volume}");
    }
}
