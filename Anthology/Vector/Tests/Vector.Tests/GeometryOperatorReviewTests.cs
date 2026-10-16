// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

using Edge = Prowl.Vector.Geometry.GeometryData.Edge;
using Face = Prowl.Vector.Geometry.GeometryData.Face;
using Vertex = Prowl.Vector.Geometry.GeometryData.Vertex;

namespace Prowl.Vector.Tests;

/// <summary>
/// Checks the editing operators keep a mesh watertight and keep every attribute meaningful. Each loop carries a
/// uv that is a linear function of its vertex position, so any correct interpolation reproduces it exactly.
/// </summary>
public class GeometryOperatorReviewTests
{
    private static Float2 UvOf(Float3 p) => new(p.X * 2f + p.Z, p.Y * 3f - p.Z);

    private static GeometryData Cube()
    {
        GeometryData mesh = GeometryGenerator.Box(1f, 1f, 1f);
        GeometryOperators.Translate(mesh, new Float3(0.5f, 0.5f, 0.5f));
        mesh.AddFaceAttribute("tag", GeometryData.AttributeBaseType.Int, 1);
        mesh.AddLoopAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
        mesh.AddEdgeAttribute("crease", GeometryData.AttributeBaseType.Int, 1);
        int tag = 1;
        foreach (Face f in mesh.Faces)
        {
            f.Attributes["tag"] = new GeometryData.IntAttributeValue(tag++);
            FixUv(f);
        }
        return mesh;
    }

    private static void FixUv(Face f)
    {
        var loop = f.Loop!;
        do
        {
            Float2 uv = UvOf(loop.Vert.Point);
            loop.Attributes["uv"] = new GeometryData.FloatAttributeValue(uv.X, uv.Y);
            loop = loop.Next!;
        } while (loop != f.Loop);
    }

    private static void AssertUvLinear(GeometryData mesh)
    {
        foreach (Face f in mesh.Faces)
        {
            var loop = f.Loop!;
            do
            {
                var uv = (GeometryData.FloatAttributeValue)loop.Attributes["uv"];
                Float2 want = UvOf(loop.Vert.Point);
                Assert.True(MathF.Abs(uv.Data[0] - want.X) < 1e-3f && MathF.Abs(uv.Data[1] - want.Y) < 1e-3f,
                    $"uv {uv.Data[0]},{uv.Data[1]} should be {want.X},{want.Y} at {loop.Vert.Point}");
                loop = loop.Next!;
            } while (loop != f.Loop);
        }
    }

    private static void AssertTagsPresent(GeometryData mesh)
    {
        foreach (Face f in mesh.Faces)
            Assert.True(f.Attributes.TryGetValue("tag", out var v) && ((GeometryData.IntAttributeValue)v).Data[0] > 0, "a face lost its tag");
    }

    private static void AssertWatertight(GeometryData mesh)
    {
        foreach (Edge e in mesh.Edges)
        {
            var loops = new List<GeometryData.Loop>();
            var l = e.Loop;
            if (l != null)
            {
                var start = l;
                do { loops.Add(l!); l = l!.RadialNext; } while (l != start && l != null);
            }
            Assert.True(loops.Count == 2, $"edge {e.Vert1.Point} to {e.Vert2.Point} has {loops.Count} faces");
            bool forward0 = loops[0].Vert == e.Vert1, forward1 = loops[1].Vert == e.Vert1;
            Assert.True(forward0 != forward1, $"edge {e.Vert1.Point} to {e.Vert2.Point} is wound the same way by both faces");
        }
        foreach (Vertex v in mesh.Vertices) Assert.NotNull(v.Edge);
    }

    private static float Volume(GeometryData mesh)
    {
        float volume = 0f;
        foreach (Face f in mesh.Faces)
        {
            List<Vertex> v = f.NeighborVertices();
            for (int i = 1; i + 1 < v.Count; i++)
                volume += Float3.Dot(v[0].Point, Float3.Cross(v[i].Point, v[i + 1].Point)) / 6f;
        }
        return volume;
    }

    private static Face FaceFacing(GeometryData mesh, Float3 direction) => mesh.Faces.First(f => Float3.Dot(Float3.Normalize(f.Normal()), direction) > 0.99f);

    [Fact]
    public void CubeFixtureIsWatertightAndOutwardFacing()
    {
        GeometryData cube = Cube();
        AssertWatertight(cube);
        Assert.Equal(1f, Volume(cube), 3);
        AssertUvLinear(cube);
    }

    [Fact]
    public void Subdivide_KeepsFaceAndLoopAttributes()
    {
        GeometryData cube = Cube();
        GeometryOperators.Subdivide(cube);
        Assert.Equal(24, cube.Faces.Count);
        AssertWatertight(cube);
        AssertTagsPresent(cube);
        AssertUvLinear(cube);
    }

    [Theory]
    [InlineData(0.25f, true)]
    [InlineData(0.25f, false)]
    public void SplitEdge_InterpolatesLoopAttributesInEitherDirection(float factor, bool fromFirst)
    {
        GeometryData cube = Cube();
        Edge edge = cube.Edges[0];
        edge.Attributes["crease"] = new GeometryData.IntAttributeValue(7);
        Vertex from = fromFirst ? edge.Vert1 : edge.Vert2;
        Vertex made = GeometryOperators.SplitEdge(cube, edge, from, factor, out _);
        Assert.Equal(6, cube.Faces.Count);
        AssertUvLinear(cube);
        AssertTagsPresent(cube);
        foreach (Edge half in made.NeighborEdges())
            Assert.Equal(7, ((GeometryData.IntAttributeValue)half.Attributes["crease"]).Data[0]);
    }

    [Fact]
    public void SplitFace_KeepsAttributesOnBothHalves()
    {
        GeometryData cube = Cube();
        Face top = FaceFacing(cube, Float3.UnitY);
        List<Vertex> verts = top.NeighborVertices();
        GeometryOperators.SplitFace(cube, top, verts[0], verts[2], out Edge? diagonal);
        Assert.NotNull(diagonal);
        Assert.Equal(7, cube.Faces.Count);
        AssertWatertight(cube);
        AssertTagsPresent(cube);
        AssertUvLinear(cube);
    }

    [Fact]
    public void Extrude_OneFace_LeavesNoLooseGeometry()
    {
        GeometryData cube = Cube();
        int before = cube.Vertices.Count;
        GeometryOperators.ExtrudeFaces(cube, [FaceFacing(cube, Float3.UnitY)], 1f, ExtrudeMode.AverageNormal);
        Assert.Equal(before + 4, cube.Vertices.Count);
        Assert.Equal(10, cube.Faces.Count);
        Assert.Equal(2f, Volume(cube), 3);
        Assert.All(cube.Edges, e => Assert.NotNull(e.Loop));
        AssertWatertight(cube);
    }

    [Fact]
    public void Extrude_Inward_WindsWallsTheOtherWay()
    {
        GeometryData cube = Cube();
        GeometryOperators.ExtrudeFaces(cube, [FaceFacing(cube, Float3.UnitY)], -0.5f, ExtrudeMode.AverageNormal);
        Assert.Equal(0.5f, Volume(cube), 3);
        AssertWatertight(cube);
    }

    [Fact]
    public void Extrude_Region_RemovesInteriorLeftovers()
    {
        GeometryData grid = GeometryGenerator.Plane(2f, 2);
        int faces = grid.Faces.Count;
        Assert.Equal(4, faces);
        GeometryOperators.ExtrudeFaces(grid, grid.Faces.ToList(), 1f, ExtrudeMode.AverageNormal);
        Assert.All(grid.Edges, e => Assert.NotNull(e.Loop));
        Assert.All(grid.Vertices, v => Assert.NotNull(v.Edge));
        Assert.Equal(4 + 8, grid.Faces.Count);
    }

    [Fact]
    public void Extrude_WallsAndTopKeepFaceAttributes()
    {
        GeometryData cube = Cube();
        Face top = FaceFacing(cube, Float3.UnitY);
        GeometryOperators.ExtrudeFaces(cube, [top], 1f, ExtrudeMode.AverageNormal);
        AssertTagsPresent(cube);
    }

    [Fact]
    public void Extrude_UsesTheRealFaceNormalEvenWhenTheFirstCornersAreCollinear()
    {
        var mesh = new GeometryData();
        Vertex a = mesh.AddVertex(0, 0, 0), b = mesh.AddVertex(1, 0, 0), c = mesh.AddVertex(2, 0, 0), d = mesh.AddVertex(2, 0, 2), e = mesh.AddVertex(0, 0, 2);
        Face f = mesh.AddFace(a, b, c, d, e)!;
        Float3 normal = Float3.Normalize(f.Normal());
        GeometryOperators.ExtrudeFaces(mesh, [f], 1f, ExtrudeMode.AverageNormal);
        Assert.All(mesh.Vertices, v => Assert.False(float.IsNaN(v.Point.X) || float.IsNaN(v.Point.Y) || float.IsNaN(v.Point.Z)));
        Assert.Contains(mesh.Vertices, v => Float3.Distance(v.Point, new Float3(0, 0, 0) + normal) < 1e-4f);
    }

    [Fact]
    public void Inset_PerFace_LeavesNoLooseGeometryAndKeepsAttributes()
    {
        GeometryData cube = Cube();
        GeometryOperators.InsetFaces(cube, [FaceFacing(cube, Float3.UnitY)], 0.25f, InsetMode.PerFace);
        Assert.Equal(10, cube.Faces.Count);
        AssertUvLinear(cube);
        Assert.All(cube.Edges, e => Assert.NotNull(e.Loop));
        AssertWatertight(cube);
        AssertTagsPresent(cube);
    }

    [Fact]
    public void Inset_Shared_Region_LeavesNoLooseGeometry()
    {
        GeometryData grid = GeometryGenerator.Plane(2f, 2);
        GeometryOperators.InsetFaces(grid, grid.Faces.ToList(), 0.3f, InsetMode.Shared);
        Assert.All(grid.Edges, e => Assert.NotNull(e.Loop));
        Assert.All(grid.Vertices, v => Assert.NotNull(v.Edge));
    }

    [Fact]
    public void Inset_BordersOfARectangleAreEquallyThick()
    {
        var mesh = new GeometryData();
        Face f = mesh.AddFace(mesh.AddVertex(0, 0, 0), mesh.AddVertex(4, 0, 0), mesh.AddVertex(4, 0, 1), mesh.AddVertex(0, 0, 1))!;
        GeometryOperators.InsetFaces(mesh, [f], 0.1f, InsetMode.PerFace, InsetMeasure.Distance);
        Face inner = mesh.Faces.OrderBy(x => Float3.Length(x.Normal())).First();
        List<Float3> p = inner.NeighborVertices().Select(v => v.Point).ToList();
        Assert.Equal(0.1f, p.Min(q => q.X), 3);
        Assert.Equal(3.9f, p.Max(q => q.X), 3);
        Assert.Equal(0.1f, p.Min(q => q.Z), 3);
        Assert.Equal(0.9f, p.Max(q => q.Z), 3);
    }

    [Fact]
    public void Weld_RemapsLoopAttributesWhenACornerCollapses()
    {
        GeometryData cube = Cube();
        Face top = FaceFacing(cube, Float3.UnitY);
        List<Vertex> verts = top.NeighborVertices();
        Vertex extra = cube.AddVertex(verts[1].Point + new Float3(0, 0, 1e-6f));
        var corner = verts[1];
        cube.RemoveFace(top);
        cube.AddFace(verts[0], corner, extra, verts[2], verts[3]);
        foreach (Face f in cube.Faces) FixUv(f);
        int welded = GeometryOperators.WeldVertices(cube, 1e-4f);
        Assert.Equal(1, welded);
        AssertUvLinear(cube);
    }

    [Fact]
    public void Weld_KeepsLooseEdgesAndEdgeAttributes()
    {
        GeometryData cube = Cube();
        Edge edge = cube.Edges[3];
        edge.Attributes["crease"] = new GeometryData.IntAttributeValue(9);
        Vertex a = cube.AddVertex(5, 5, 5), b = cube.AddVertex(6, 5, 5), b2 = cube.AddVertex(6, 5, 5 + 1e-6f);
        cube.AddEdge(a, b);
        cube.AddEdge(a, b2);
        GeometryOperators.WeldVertices(cube, 1e-4f);
        Assert.Contains(cube.Edges, e => e.Loop == null);
        Edge same = cube.Edges.First(e => e.Loop != null && Float3.Distance(e.Center(), edge.Center()) < 1e-4f);
        Assert.Equal(9, ((GeometryData.IntAttributeValue)same.Attributes["crease"]).Data[0]);
    }

    [Fact]
    public void Weld_LargeMeshFinishesQuickly()
    {
        GeometryData sphere = GeometryGenerator.Sphere(1f, Float3.Zero, 128, 64);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        GeometryOperators.WeldVertices(sphere, 1e-4f);
        Assert.True(watch.ElapsedMilliseconds < 5000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Bisect_CutsCubeInHalfWatertightWithAttributes()
    {
        GeometryData cube = Cube();
        GeometryOperators.BisectPlane(cube, Plane.FromNormalAndPoint(Float3.UnitX, new Float3(0.5f, 0, 0)));
        AssertWatertight(cube);
        Assert.Equal(1f, Volume(cube), 3);
        AssertTagsPresent(cube);
        AssertUvLinear(cube);
        Assert.Equal(10, cube.Faces.Count);
    }

    [Fact]
    public void Bisect_ConcaveFaceCrossingThePlaneTwiceIsCutTwice()
    {
        var mesh = new GeometryData();
        Float3[] u = [new(0, 0, 0), new(5, 0, 0), new(5, 0, 1), new(1, 0, 1), new(1, 0, 4), new(0, 0, 4)];
        mesh.AddFace(u.Select(p => mesh.AddVertex(p)).ToArray());
        GeometryOperators.BisectPlane(mesh, Plane.FromNormalAndPoint(Float3.UnitX, new Float3(0.5f, 0, 0)));
        Assert.True(mesh.Faces.All(f => f.NeighborVertices().All(v => v.Point.X >= 0.5f - 1e-4f) || f.NeighborVertices().All(v => v.Point.X <= 0.5f + 1e-4f)));
    }

    [Fact]
    public void BevelVertices_OneCornerIsWatertightAndKeepsAttributes()
    {
        GeometryData cube = Cube();
        Vertex corner = cube.Vertices.First(v => v.Point.X > 0.9f && v.Point.Y > 0.9f && v.Point.Z > 0.9f);
        GeometryOperators.BevelVertices(cube, [corner], 0.3f);
        AssertWatertight(cube);
        AssertUvLinear(cube);
        Assert.Equal(7, cube.Faces.Count);
        AssertTagsPresent(cube);
        float volume = Volume(cube);
        Assert.InRange(volume, 0.9f, 0.999f);
    }

    [Fact]
    public void BevelVertices_BothEndsOfAnEdgeStayWatertight()
    {
        GeometryData cube = Cube();
        Vertex a = cube.Vertices.First(v => v.Point.X > 0.9f && v.Point.Y > 0.9f && v.Point.Z > 0.9f);
        Vertex b = a.NeighborEdges().Select(e => e.OtherVertex(a)).First();
        GeometryOperators.BevelVertices(cube, [a, b], 0.2f);
        AssertWatertight(cube);
        AssertUvLinear(cube);
        AssertTagsPresent(cube);
    }

    [Fact]
    public void BevelVertices_AllCornersOfTheCubeMakesATruncatedCube()
    {
        GeometryData cube = Cube();
        GeometryOperators.BevelVertices(cube, cube.Vertices.ToList(), 0.25f);
        AssertWatertight(cube);
        Assert.Equal(14, cube.Faces.Count);
        Assert.Equal(24, cube.Vertices.Count);
    }

    [Fact]
    public void BevelVertices_OnAnOpenBorderStaysValid()
    {
        GeometryData grid = GeometryGenerator.Plane(2f, 2);
        Vertex corner = grid.Vertices.First(v => MathF.Abs(v.Point.X) > 0.9f && MathF.Abs(v.Point.Z) > 0.9f);
        GeometryOperators.BevelVertices(grid, [corner], 0.3f);
        Assert.All(grid.Faces, f => Assert.True(f.VertCount >= 3));
        Assert.All(grid.Vertices, v => Assert.NotNull(v.Edge));
    }

    [Fact]
    public void BevelEdges_CubeEdgeIsWatertightAndKeepsAttributes()
    {
        GeometryData cube = Cube();
        Edge edge = cube.Edges.First(e => MathF.Abs(e.Vert1.Point.X - 1f) < 1e-4f && MathF.Abs(e.Vert2.Point.X - 1f) < 1e-4f && MathF.Abs(e.Vert1.Point.Z) < 1e-4f && MathF.Abs(e.Vert2.Point.Z) < 1e-4f);
        GeometryOperators.BevelEdges(cube, [edge], 0.2f);
        AssertWatertight(cube);
        AssertUvLinear(cube);
        AssertTagsPresent(cube);
        Assert.Equal(7, cube.Faces.Count);
    }

    [Fact]
    public void Squarify_MakesASkewedQuadRectangular()
    {
        var mesh = new GeometryData();
        mesh.AddFace(mesh.AddVertex(0, 0, 0), mesh.AddVertex(2, 0, 0), mesh.AddVertex(3, 0, 1), mesh.AddVertex(1, 0, 1));
        for (int i = 0; i < 20; i++) GeometryOperators.SquarifyQuads(mesh, 0.5f);
        List<Vertex> v = mesh.Faces[0].NeighborVertices();
        for (int i = 0; i < 4; i++)
        {
            Float3 a = v[(i + 1) % 4].Point - v[i].Point, b = v[(i + 2) % 4].Point - v[(i + 1) % 4].Point;
            Assert.True(MathF.Abs(Float3.Dot(Float3.Normalize(a), Float3.Normalize(b))) < 1e-2f);
        }
    }

    [Fact]
    public void Squarify_DegenerateQuadDoesNotProduceNaN()
    {
        var mesh = new GeometryData();
        mesh.AddFace(mesh.AddVertex(0, 0, 0), mesh.AddVertex(1, 0, 0), mesh.AddVertex(2, 0, 0), mesh.AddVertex(3, 0, 0));
        GeometryOperators.SquarifyQuads(mesh);
        Assert.All(mesh.Vertices, p => Assert.False(float.IsNaN(p.Point.X) || float.IsNaN(p.Point.Y) || float.IsNaN(p.Point.Z)));
    }

    [Fact]
    public void Merge_CopiesFaceAndLoopAttributes()
    {
        GeometryData a = Cube();
        GeometryData b = Cube();
        GeometryOperators.Translate(b, new Float3(5, 0, 0));
        foreach (Face f in b.Faces) FixUv(f);
        GeometryOperators.Merge(a, b);
        Assert.Equal(12, a.Faces.Count);
        AssertTagsPresent(a);
        AssertUvLinear(a);
    }
}
