// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class OperatorTests
{
    private static CSGControlMesh Box() => CSGControlMesh.FromPlanes(GeometryGenerator.BoxPlanes(new Float3(2f), Float3.Zero))!;

    private static SurfaceData[] Marked(CSGControlMesh mesh)
        => Enumerable.Range(0, mesh.Polygons.Count).Select(p => new SurfaceData { Scale = Float2.One, Offset = new Float2(p, 0f) }).ToArray();

    /// <summary>An inset face shrinks inside its outline, ringed by new faces that take its surface, and is what is selected after.</summary>
    [Fact]
    public void InsetRingsAFaceWithFacesOfItsSurface()
    {
        CSGControlMesh box = Box();
        int top = Enumerable.Range(0, box.Polygons.Count).First(p => box.PolygonNormal(p).Y > 0.99f);
        var target = new CSGBrushTarget(box, Marked(box));
        var after = new ElementSelection();

        string? problem = MeshEdit.Run(target.Target, (mesh, selected) => Operators.Inset(mesh, target.FacesOf([top]), 0.25f, selected), after);

        Assert.Null(problem);
        GeometryData.Face inner = Assert.Single(after.Faces);
        Assert.All(inner.NeighborVertices(), v => Assert.True(MathF.Abs(v.Point.X) <= 0.75f + 1e-4f && MathF.Abs(v.Point.Z) <= 0.75f + 1e-4f));

        (CSGControlMesh inset, SurfaceData[] surfaces) = target.ToBrush();
        Assert.Equal(CSGMeshProblem.None, inset.Check());
        Assert.Equal(box.Polygons.Count + 4, inset.Polygons.Count);
        Assert.Equal(8f, inset.Volume(), 4);

        var upward = Enumerable.Range(0, inset.Polygons.Count).Where(p => inset.PolygonNormal(p).Y > 0.99f).ToList();
        Assert.Equal(5, upward.Count);
        Assert.All(upward, p => Assert.Equal(top, (int)surfaces[p].Offset.X));
    }

    /// <summary>An inset wider than its face cannot be made, and leaves the mesh as it was.</summary>
    [Fact]
    public void AnInsetTooWideIsRefused()
    {
        CSGControlMesh box = Box();
        int top = Enumerable.Range(0, box.Polygons.Count).First(p => box.PolygonNormal(p).Y > 0.99f);
        var target = new CSGBrushTarget(box, Marked(box));
        int faces = target.Target.Mesh.Faces.Count;

        string? problem = MeshEdit.Run(target.Target, (mesh, selected) => Operators.Inset(mesh, target.FacesOf([top]), 5f, selected), new ElementSelection());

        Assert.NotNull(problem);
        Assert.Equal(faces, target.Target.Mesh.Faces.Count);
    }

    private static int TopOf(CSGControlMesh mesh) => Enumerable.Range(0, mesh.Polygons.Count).First(p => mesh.PolygonNormal(p).Y > 0.99f);

    private static GeometryData.Vertex VertexAt(GeometryData mesh, Float3 at) => mesh.Vertices.First(v => Float3.Distance(v.Point, at) < 1e-4f);

    private static GeometryData.Edge EdgeBetween(GeometryData mesh, Float3 a, Float3 b) => mesh.FindEdge(VertexAt(mesh, a), VertexAt(mesh, b))!;

    private static CSGControlMesh Brush(CSGBrushTarget target)
    {
        (CSGControlMesh mesh, _) = target.ToBrush();
        Assert.Equal(CSGMeshProblem.None, mesh.Check());
        return mesh;
    }

    [Fact]
    public void ExtrudingARegionGrowsTheSolidAndSelectsTheMovedFace()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.ExtrudeRegion(mesh, target.FacesOf([TopOf(box)]), 1f, selected), after));

        GeometryData.Face moved = Assert.Single(after.Faces);
        Assert.All(moved.NeighborVertices(), v => Assert.Equal(2f, v.Point.Y, 4));
        Assert.Equal(12f, Brush(target).Volume(), 3);
    }

    /// <summary>A line splitting the top, dissolved, leaves the top whole again.</summary>
    [Fact]
    public void DissolvingAnEdgeJoinsTheFacesBesideIt()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData mesh0 = target.Target.Mesh;
        var corners = new[] { VertexAt(mesh0, new Float3(1, 1, 1)), VertexAt(mesh0, new Float3(-1, 1, -1)) };
        var split = new ElementSelection();
        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Connect(mesh, corners, -Float3.UnitY, selected), split));
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, _) => Operators.Dissolve(mesh, split, SelectMode.Edge, after), after));

        GeometryData.Face top = Assert.Single(after.Faces);
        Assert.True(top.Normal().Y > 0.99f);
        Assert.Equal(6, target.Target.Mesh.Faces.Count);
        Assert.Equal(8f, Brush(target).Volume(), 3);
    }

    [Fact]
    public void SubdividingThenDissolvingAPointGivesTheBoxBack()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        var after = new ElementSelection();
        GeometryData.Edge edge = EdgeBetween(target.Target.Mesh, new Float3(1, 1, 1), new Float3(-1, 1, 1));

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.SubdivideEdges(mesh, [edge], 1, selected), after));
        GeometryData.Vertex middle = Assert.Single(after.Vertices);
        Assert.Equal(new Float3(0, 1, 1), middle.Point);
        Assert.Equal(9, target.Target.Mesh.Vertices.Count);

        var dissolve = new ElementSelection();
        dissolve.Vertices.Add(middle);
        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Dissolve(mesh, dissolve, SelectMode.Vertex, selected), new ElementSelection()));
        Assert.Equal(8, Brush(target).Points.Count);
    }

    /// <summary>A top split in two by a connected line, then dissolved where flat, is one face again.</summary>
    [Fact]
    public void ConnectSplitsAFaceAndLimitedDissolveJoinsItBack()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData mesh0 = target.Target.Mesh;
        var corners = new[] { VertexAt(mesh0, new Float3(1, 1, 1)), VertexAt(mesh0, new Float3(-1, 1, -1)) };
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Connect(mesh, corners, -Float3.UnitY, selected), after));
        Assert.Single(after.Edges);
        Assert.Equal(7, target.Target.Mesh.Faces.Count);

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.LimitedDissolve(mesh, null, 5f, selected), new ElementSelection()));
        Assert.Equal(6, target.Target.Mesh.Faces.Count);
        Assert.Equal(8f, Brush(target).Volume(), 3);
    }

    [Fact]
    public void MergingTwoCornersAtTheirCentreLeavesOnePoint()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData mesh0 = target.Target.Mesh;
        var pair = new[] { VertexAt(mesh0, new Float3(1, 1, 1)), VertexAt(mesh0, new Float3(-1, 1, 1)) };
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Merge(mesh, pair, new Float3(0, 1, 1), selected), after));

        GeometryData.Vertex merged = Assert.Single(after.Vertices);
        Assert.Equal(new Float3(0, 1, 1), merged.Point);
        Assert.Equal(7, Brush(target).Points.Count);
    }

    [Fact]
    public void CollapsingAnEdgeLeavesOnePointAtItsMiddle()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData.Edge edge = EdgeBetween(target.Target.Mesh, new Float3(1, 1, 1), new Float3(-1, 1, 1));
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Collapse(mesh, [edge], selected), after));

        GeometryData.Vertex merged = Assert.Single(after.Vertices);
        Assert.Equal(new Float3(0, 1, 1), merged.Point);
        Assert.Equal(7, Brush(target).Points.Count);
    }

    [Fact]
    public void FlatteningPutsTheSelectedPointsOnOnePlane()
    {
        CSGControlMesh box = Box();
        int top = TopOf(box);
        var target = new CSGBrushTarget(box, Marked(box));
        VertexAt(target.Target.Mesh, new Float3(1, 1, 1)).Point = new Float3(1, 1.4f, 1);
        var selection = new ElementSelection();
        selection.Faces.UnionWith(target.FacesOf([top]));
        Selecting.Normalize(selection, SelectMode.Face);

        Assert.Null(MeshEdit.Run(target.Target, (mesh, _) => Operators.Flatten(mesh, selection), new ElementSelection()));

        Float3[] points = selection.Vertices.Select(v => v.Point).ToArray();
        Float3 normal = Float3.Normalize(Float3.Cross(points[1] - points[0], points[2] - points[0]));
        Assert.All(points, p => Assert.Equal(0f, Float3.Dot(p - points[0], normal), 3));
        Assert.Equal(1.1f, points.Average(p => p.Y), 3);
    }

    private static CSGControlMesh Prism()
    {
        Float2[] outline = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
        var points = new List<Float3>();
        foreach (Float2 p in outline) points.Add(new Float3(p.X, -1f, p.Y));
        foreach (Float2 p in outline) points.Add(new Float3(p.X, 1f, p.Y));
        var polygons = new List<int[]> { new[] { 0, 1, 2, 3 }, new[] { 7, 6, 5, 4 } };
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            polygons.Add([j, i, 4 + i, 4 + j]);
        }
        CSGControlMesh mesh = CSGControlMesh.FromPolygons(points, polygons);
        if (mesh.Volume() < 0) mesh.ReverseWinding();
        return mesh;
    }

    [Fact]
    public void BevellingAnEdgeCutsItOffAndSelectsTheStrip()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData.Edge edge = EdgeBetween(target.Target.Mesh, new Float3(1, 1, 1), new Float3(-1, 1, 1));
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Bevel(mesh, [edge], 0.25f, 1, selected), after));

        GeometryData.Face strip = Assert.Single(after.Faces);
        Assert.True(strip.Normal().Y > 0.5f && strip.Normal().Z > 0.5f);
        CSGControlMesh brush = Brush(target);
        Assert.Equal(7, brush.Polygons.Count);
        Assert.Equal(8f - 0.25f * 0.25f * 0.5f * 2f, brush.Volume(), 3);
    }

    [Fact]
    public void BevellingACornerCapsIt()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData.Vertex corner = VertexAt(target.Target.Mesh, new Float3(1, 1, 1));
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.BevelPoints(mesh, [corner], 0.5f, selected), after));

        Assert.Single(after.Faces);
        Assert.Equal(7, Brush(target).Polygons.Count);
    }

    [Fact]
    public void ALoopCutGoesRoundTheRingAndSelectsTheNewEdges()
    {
        CSGControlMesh prism = Prism();
        var target = new CSGBrushTarget(prism, Marked(prism));
        GeometryData.Edge upright = EdgeBetween(target.Target.Mesh, new Float3(1, -1, 1), new Float3(1, 1, 1));
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.LoopCut(mesh, upright, 1, 0f, selected), after));

        Assert.Equal(4, after.Edges.Count);
        Assert.All(after.Edges, e => Assert.Equal(0f, e.Vert1.Point.Y, 4));
        Assert.Equal(10, Brush(target).Polygons.Count);
    }

    [Fact]
    public void SlidingACutLoopMovesItAlongTheRing()
    {
        CSGControlMesh prism = Prism();
        var target = new CSGBrushTarget(prism, Marked(prism));
        GeometryData.Edge upright = EdgeBetween(target.Target.Mesh, new Float3(1, -1, 1), new Float3(1, 1, 1));
        var loop = new ElementSelection();
        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.LoopCut(mesh, upright, 1, 0f, selected), loop));

        Assert.Null(MeshEdit.Run(target.Target, (mesh, _) => Operators.SlideEdges(mesh, [.. loop.Edges], 0.5f), new ElementSelection()));

        float y = loop.Edges.First().Vert1.Point.Y;
        Assert.Equal(0.5f, MathF.Abs(y), 3);
        Assert.All(loop.Edges, e => Assert.Equal(y, e.Vert2.Point.Y, 3));
        Assert.Equal(8f, Brush(target).Volume(), 3);
    }

    /// <summary>A knife drawn straight across the top, from edge to edge, splits it in two.</summary>
    [Fact]
    public void AKnifeAcrossTheTopSplitsIt()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData mesh0 = target.Target.Mesh;
        GeometryData.Edge front = EdgeBetween(mesh0, new Float3(1, 1, 1), new Float3(-1, 1, 1));
        GeometryData.Edge back = EdgeBetween(mesh0, new Float3(1, 1, -1), new Float3(-1, 1, -1));
        var points = new List<KnifePoint>
        {
            new() { Point = new Float3(0, 1, 1), Edge = front },
            new() { Point = new Float3(0, 1, -1), Edge = back },
        };
        var planes = new List<(Float3, Float3)> { (new Float3(0, 1, 0), Float3.UnitX) };
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) =>
            Operators.Knife(mesh, points, planes, (_, p) => MathF.Abs(p.Y - 1f) < 1e-3f ? (1f - p.Z) / 2f : null, null, selected), after));

        Assert.NotEmpty(after.Edges);
        Assert.Equal(7, Brush(target).Polygons.Count);
    }

    [Fact]
    public void SubdividingAFaceSplitsItIntoFour()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.SubdivideFaces(mesh, target.FacesOf([TopOf(box)]), 1, selected), after));

        Assert.Equal(4, after.Faces.Count);
        Assert.Equal(9, Brush(target).Polygons.Count);
    }

    [Fact]
    public void TriangulatingThenJoiningTrianglesGivesTheFaceBack()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        var triangles = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.TriangulateFaces(mesh, target.FacesOf([TopOf(box)]), selected), triangles));
        Assert.Equal(2, triangles.Faces.Count);
        Assert.Equal(7, target.Target.Mesh.Faces.Count);

        var quads = new ElementSelection();
        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.TrisToQuads(mesh, [.. triangles.Faces], 5f, selected), quads));
        Assert.Single(quads.Faces);
        Assert.Equal(6, Brush(target).Polygons.Count);
    }

    /// <summary>Inset faces on top and bottom, bridged, make a square hole through the box.</summary>
    [Fact]
    public void BridgingTwoFacesMakesAHoleThrough()
    {
        CSGControlMesh box = Box();
        int top = TopOf(box), bottom = Enumerable.Range(0, box.Polygons.Count).First(p => box.PolygonNormal(p).Y < -0.99f);
        var target = new CSGBrushTarget(box, Marked(box));
        var inner = new ElementSelection();
        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Inset(mesh, target.FacesOf([top, bottom]), 0.5f, selected), inner));
        Assert.Equal(2, inner.Faces.Count);
        GeometryData.Face[] ends = [.. inner.Faces];
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Bridge(mesh, ends[0], ends[1], 1, selected), after));

        Assert.Equal(4, after.Faces.Count);
        Assert.Equal(6f, Brush(target).Volume(), 3);
    }

    [Fact]
    public void SymmetrizingCopiesOneSideOverTheOther()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        VertexAt(target.Target.Mesh, new Float3(1, 1, 1)).Point = new Float3(1.5f, 1, 1);

        Assert.Null(MeshEdit.Run(target.Target, (mesh, _) => Operators.Symmetrize(mesh, 0, keepPositive: true, 0f), new ElementSelection()));

        CSGControlMesh brush = Brush(target);
        Assert.Contains(brush.Points, p => Float3.Distance(p, new Float3(-1.5f, 1, 1)) < 1e-4f);
        Assert.Contains(brush.Points, p => Float3.Distance(p, new Float3(1.5f, 1, 1)) < 1e-4f);
        Assert.DoesNotContain(brush.Points, p => Float3.Distance(p, new Float3(-1, 1, 1)) < 1e-4f);
    }

    [Fact]
    public void MergingByDistanceJoinsNearPoints()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData.Edge edge = EdgeBetween(target.Target.Mesh, new Float3(1, 1, 1), new Float3(-1, 1, 1));
        var middle = new ElementSelection();
        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.SubdivideEdges(mesh, [edge], 1, selected), middle));
        var scope = new[] { middle.Vertices.First(), VertexAt(target.Target.Mesh, new Float3(1, 1, 1)) };
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.MergeByDistance(mesh, scope, 1.5f, selected), after));

        Assert.Equal(8, Brush(target).Points.Count);
        Assert.Single(after.Vertices);
    }

    [Fact]
    public void SmoothingPullsCornersIn()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        var corners = new[] { VertexAt(target.Target.Mesh, new Float3(1, 1, 1)) };
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Smooth(mesh, corners, 0.5f, selected), after));

        Assert.Single(after.Vertices);
        Assert.True(Brush(target).Volume() < 8f);
    }

    /// <summary>A box pulled up, its upper half separated, gives two closed boxes.</summary>
    [Fact]
    public void SeparatingTheUpperHalfMakesTwoBrushes()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.ExtrudeRegion(mesh, target.FacesOf([TopOf(box)]), 2f, selected), new ElementSelection()));
        (CSGControlMesh tall, SurfaceData[] surfaces) = target.ToBrush();
        var upper = Enumerable.Range(0, tall.Polygons.Count).Where(p => tall.PolygonPoints(p).All(i => tall.Points[i].Y >= 1f - 1e-4f)).ToList();

        Assert.True(CSGBrushTarget.Separate(tall, surfaces, upper, out var kept, out var separated));

        Assert.Equal(CSGMeshProblem.None, kept.Mesh.Check());
        Assert.Equal(CSGMeshProblem.None, separated.Mesh.Check());
        Assert.Equal(8f, kept.Mesh.Volume(), 3);
        Assert.Equal(8f, separated.Mesh.Volume(), 3);
        Assert.Equal(kept.Mesh.Polygons.Count, kept.Surfaces.Length);
    }

    [Fact]
    public void ABridgeOfTwoSegmentsHasTwoRingsOfFaces()
    {
        CSGControlMesh box = Box();
        int top = TopOf(box), bottom = Enumerable.Range(0, box.Polygons.Count).First(p => box.PolygonNormal(p).Y < -0.99f);
        var target = new CSGBrushTarget(box, Marked(box));
        var inner = new ElementSelection();
        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Inset(mesh, target.FacesOf([top, bottom]), 0.5f, selected), inner));
        GeometryData.Face[] ends = [.. inner.Faces];
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Bridge(mesh, ends[0], ends[1], 2, selected), after));

        Assert.Equal(8, after.Faces.Count);
        Assert.Equal(6f, Brush(target).Volume(), 3);
    }

    [Fact]
    public void SubdividingAFaceByThreeCutsMakesAGrid()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.SubdivideFaces(mesh, target.FacesOf([TopOf(box)]), 3, selected), after));

        Assert.Equal(16, after.Faces.Count);
        Assert.Equal(8f, Brush(target).Volume(), 3);
    }

    [Fact]
    public void RelaxingEvensOutPoints()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData.Edge edge = EdgeBetween(target.Target.Mesh, new Float3(1, 1, 1), new Float3(-1, 1, 1));
        var added = new ElementSelection();
        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.SubdivideEdges(mesh, [edge], 1, selected), added));
        GeometryData.Vertex middle = added.Vertices.First();
        middle.Point = new Float3(0.6f, 1, 1);
        var after = new ElementSelection();

        Assert.Null(MeshEdit.Run(target.Target, (mesh, selected) => Operators.Relax(mesh, [middle], 1f, selected), after));

        Assert.Single(after.Vertices);
        Assert.True(MathF.Abs(middle.Point.X) < 0.6f);
    }
}
