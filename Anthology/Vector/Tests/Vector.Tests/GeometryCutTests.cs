// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

public class GeometryCutTests
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

    private static GeometryData.Edge EdgeAt(GeometryData mesh, Float3 a, Float3 b)
        => mesh.Edges.First(e => (e.Vert1.Point == a && e.Vert2.Point == b) || (e.Vert1.Point == b && e.Vert2.Point == a));

    [Fact]
    public void KnifeCut_SplitsEveryFaceThePlaneCrosses()
    {
        GeometryData mesh = Grid(3, 1);

        var cuts = GeometryOperators.KnifeCut(mesh, mesh.Faces.ToList(), new Float3(0, 0.25f, 0), new Float3(0, 1, 0));

        Assert.Equal(3, cuts.Count);
        Assert.Equal(6, mesh.Faces.Count);
        Assert.Equal(12, mesh.Vertices.Count);
        Assert.All(mesh.Faces, f => Assert.Equal(4, f.VertCount));
        Assert.All(cuts, e => Assert.True(e.Vert1.Point.Y == 0.25f && e.Vert2.Point.Y == 0.25f));
        Assert.Equal(3f, mesh.SurfaceArea(), 4);
    }

    [Fact]
    public void KnifeCut_KeptToAPart_LeavesTheRestWhole()
    {
        GeometryData mesh = Grid(3, 1);

        var cuts = GeometryOperators.KnifeCut(mesh, mesh.Faces.ToList(), new Float3(0, 0.5f, 0), new Float3(0, 1, 0), p => p.X < 1.5f);

        // Only the first face has both its crossings inside the part. The second has one, so it is not cut.
        Assert.Single(cuts);
        Assert.Equal(4, mesh.Faces.Count);
    }

    [Fact]
    public void KnifeCut_ASlantedPlane_MakesTrianglesAndFiveSidedFaces()
    {
        GeometryData mesh = Grid(1, 1);

        var cuts = GeometryOperators.KnifeCut(mesh, mesh.Faces.ToList(), new Float3(0.5f, 0, 0), new Float3(1, 1, 0));

        Assert.Single(cuts);
        Assert.Equal(new[] { 3, 5 }, mesh.Faces.Select(f => f.VertCount).OrderBy(c => c).ToArray());
        Assert.Equal(1f, mesh.SurfaceArea(), 4);
    }

    [Fact]
    public void SplitEdges_AcrossASheet_MakesTwoPieces()
    {
        GeometryData mesh = Grid(2, 2);
        var row = GeometryOperators.EdgeLoop(EdgeAt(mesh, new Float3(0, 1, 0), new Float3(1, 1, 0)));

        var copies = GeometryOperators.SplitEdges(mesh, row);

        Assert.Equal(3, copies.Count);
        Assert.Equal(12, mesh.Vertices.Count);
        Assert.Equal(4, mesh.Faces.Count);
        // No edge joins the lower faces to the upper ones any more
        Assert.All(mesh.Edges.Where(e => e.Vert1.Point.Y == 1 && e.Vert2.Point.Y == 1), e => Assert.Single(e.NeighborFaces()));
    }

    [Fact]
    public void SplitEdges_EndingInsideTheSheet_OpensASlit()
    {
        GeometryData mesh = Grid(4, 2);
        var slit = new[] { EdgeAt(mesh, new Float3(1, 1, 0), new Float3(2, 1, 0)), EdgeAt(mesh, new Float3(2, 1, 0), new Float3(3, 1, 0)) };

        var copies = GeometryOperators.SplitEdges(mesh, slit);

        // Only the vertex in the middle of the slit is doubled, its two ends stay joined
        Assert.Single(copies);
        Assert.Equal(new Float3(2, 1, 0), copies[0].Point);
        Assert.Equal(16, mesh.Vertices.Count);
        Assert.Equal(8, mesh.Faces.Count);
    }

    [Fact]
    public void GridFill_FillsARing_WithQuads()
    {
        // A ring of twelve edges around a three by three square
        GeometryData mesh = new();
        Float3[] points =
        [
            new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(3, 0, 0), new(3, 1, 0), new(3, 2, 0),
            new(3, 3, 0), new(2, 3, 0), new(1, 3, 0), new(0, 3, 0), new(0, 2, 0), new(0, 1, 0),
        ];
        var ring = points.Select(p => mesh.AddVertex(p)).ToArray();
        for (int i = 0; i < ring.Length; i++) mesh.AddEdge(ring[i], ring[(i + 1) % ring.Length]);

        var faces = GeometryOperators.GridFill(mesh, mesh.Edges.ToList(), 3);

        Assert.Equal(9, faces.Count);
        Assert.Equal(16, mesh.Vertices.Count);
        Assert.All(faces, f => Assert.Equal(4, f.VertCount));
        Assert.All(faces, f => Assert.Equal(1f, f.Area(), 4));
        Assert.All(faces, f => Assert.True(Math.Abs(f.Normal().Z) > 0.99f));
        Assert.Single(faces.Select(f => Math.Sign(f.Normal().Z)).Distinct());
        // The four new vertices landed on the lattice
        Assert.Contains(mesh.Vertices, v => Float3.Length(v.Point - new Float3(1, 1, 0)) < 1e-4f);
        Assert.Contains(mesh.Vertices, v => Float3.Length(v.Point - new Float3(2, 2, 0)) < 1e-4f);
    }

    [Fact]
    public void GridFill_AnOddRing_Throws()
    {
        GeometryData mesh = new();
        var ring = new[] { mesh.AddVertex(0, 0, 0), mesh.AddVertex(1, 0, 0), mesh.AddVertex(1, 1, 0), mesh.AddVertex(0.5f, 1.5f, 0), mesh.AddVertex(0, 1, 0) };
        for (int i = 0; i < ring.Length; i++) mesh.AddEdge(ring[i], ring[(i + 1) % ring.Length]);

        Assert.Throws<InvalidOperationException>(() => GeometryOperators.GridFill(mesh, mesh.Edges.ToList()));
        Assert.Empty(mesh.Faces);
    }

    [Fact]
    public void RelaxVertices_EvensSpacing_ButStaysOnTheSurface()
    {
        GeometryData mesh = Grid(2, 2);
        mesh.Vertices[4].Point = new Float3(1.4f, 1.2f, 0);

        GeometryOperators.RelaxVertices(mesh, [mesh.Vertices[4]], 1f);

        Assert.True(Float3.Length(mesh.Vertices[4].Point - new Float3(1, 1, 0)) < 1e-4f);

        // A bump stays a bump: moving in or out of the surface is not what relax does
        GeometryData sphere = GeometryGenerator.Sphere(1f, segments: 16, rings: 8);
        float before = sphere.Volume();
        GeometryOperators.RelaxVertices(sphere, sphere.Vertices.ToList(), 0.5f, 5);
        GeometryData smoothed = GeometryGenerator.Sphere(1f, segments: 16, rings: 8);
        GeometryOperators.SmoothVertices(smoothed, smoothed.Vertices.ToList(), 0.5f, 5);
        Assert.True(Math.Abs(sphere.Volume() - before) < Math.Abs(smoothed.Volume() - before));
    }

    [Fact]
    public void Crease_HoldsAnEdge_UnderSubdivision()
    {
        GeometryData Subdivided(float crease)
        {
            GeometryData cube = GeometryGenerator.Box(new Float3(2, 2, 2));
            cube.AddEdgeAttribute(GeometryData.CreaseAttribute, GeometryData.AttributeBaseType.Float, 1);
            foreach (GeometryData.Edge edge in cube.Edges) edge.Crease = crease;
            return GeometryOperators.SubdivideSmooth(cube, 2);
        }

        float none = Subdivided(0).Volume(), half = Subdivided(0.5f).Volume(), full = Subdivided(1).Volume();

        Assert.Equal(8f, full, 3);
        Assert.True(none < half && half < full);
    }
}
