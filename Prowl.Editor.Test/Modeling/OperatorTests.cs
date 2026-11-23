// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Runtime.CSG;
using Prowl.Runtime.Resources;
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

    [Fact]
    public void ExtrudingAnEdgeMakesAFaceAndSelectsTheCopiedEdge()
    {
        var mesh = new GeometryData();
        GeometryData.Vertex a = mesh.AddVertex(0, 0, 0), b = mesh.AddVertex(1, 0, 0);
        mesh.AddFace(a, b, mesh.AddVertex(1, 0, 1), mesh.AddVertex(0, 0, 1));
        var after = new ElementSelection();

        Operators.ExtrudeEdges(mesh, [mesh.FindEdge(a, b)!], after);

        Assert.Equal(2, mesh.Faces.Count);
        Assert.Equal(6, mesh.Vertices.Count);
        GeometryData.Edge copy = Assert.Single(after.Edges);
        Assert.DoesNotContain(copy.Vert1, new[] { a, b });
        Assert.Equal(2, after.Vertices.Count);
    }

    [Fact]
    public void BridgingTwoChainsOfEdgesJoinsThemWithFaces()
    {
        var mesh = new GeometryData();
        GeometryData.Vertex a = mesh.AddVertex(0, 0, 0), b = mesh.AddVertex(1, 0, 0), c = mesh.AddVertex(2, 0, 0);
        GeometryData.Vertex d = mesh.AddVertex(0, 0, 1), e = mesh.AddVertex(1, 0, 1), f = mesh.AddVertex(2, 0, 1);
        var selection = new ElementSelection();
        selection.Edges.UnionWith([mesh.AddEdge(a, b), mesh.AddEdge(b, c), mesh.AddEdge(d, e), mesh.AddEdge(e, f)]);

        Operators.BridgeEdges(mesh, selection);

        Assert.Equal(2, mesh.Faces.Count);
        Assert.Equal(2, selection.Faces.Count);
    }

    private static GeometryData Strip()
    {
        var mesh = new GeometryData();
        for (int z = 0; z <= 1; z++)
            for (int x = 0; x <= 2; x++)
                mesh.AddVertex(x, 0, z);
        mesh.AddFace(0, 3, 4, 1);
        mesh.AddFace(1, 4, 5, 2);
        return mesh;
    }

    [Fact]
    public void RippingAnEdgeSplitsTheSurfaceAndSelectsTheCopies()
    {
        GeometryData mesh = Strip();
        var selection = new ElementSelection();
        selection.Edges.Add(mesh.FindEdge(mesh.Vertices[1], mesh.Vertices[4])!);

        Operators.Rip(mesh, selection);

        Assert.Equal(8, mesh.Vertices.Count);
        Assert.Equal(2, selection.Vertices.Count);
        Assert.Single(selection.Edges);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    public void RippingKeepsTheSideNearerThePointer(float pointerX)
    {
        GeometryData mesh = Strip();
        var selection = new ElementSelection();
        selection.Edges.Add(mesh.FindEdge(mesh.Vertices[1], mesh.Vertices[4])!);

        Operators.Rip(mesh, selection, p => MathF.Abs(p.X - pointerX));

        Assert.Equal(2, selection.Vertices.Count);
        foreach (GeometryData.Vertex vertex in selection.Vertices)
            Assert.All(vertex.NeighborFaces(), f => Assert.Contains(f.NeighborVertices(), v => v.Point.X == pointerX));
    }

    [Fact]
    public void SubdividingSmoothRoundsTheWholeMeshInPlace()
    {
        GeometryData mesh = Primitives.Make(PrimitiveKind.Cube);
        var selection = new ElementSelection();
        selection.Faces.Add(mesh.Faces[0]);

        Operators.SubdivideSmooth(mesh, 1, selection);

        Assert.Equal(24, mesh.Faces.Count);
        Assert.Equal(26, mesh.Vertices.Count);
        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void DuplicatingAFaceCopiesItAndSelectsTheCopy()
    {
        GeometryData mesh = Strip();
        var selection = new ElementSelection();
        selection.Faces.Add(mesh.Faces[0]);
        selection.Vertices.UnionWith(mesh.Faces[0].NeighborVertices());
        selection.Edges.UnionWith(mesh.Faces[0].NeighborEdges());

        Operators.Duplicate(mesh, selection);

        Assert.Equal(3, mesh.Faces.Count);
        Assert.Equal(10, mesh.Vertices.Count);
        Assert.DoesNotContain(mesh.Faces[0], selection.Faces);
        Assert.Single(selection.Faces);
    }

    [Fact]
    public void ShadingFlatAndSmoothSetsTheEdgesBetweenFaces()
    {
        GeometryData mesh = Strip();
        GeometryData.Edge middle = mesh.FindEdge(mesh.Vertices[1], mesh.Vertices[4])!;

        Operators.Shade(mesh, [.. mesh.Faces], smooth: true);
        Assert.True(middle.IsSoft);
        Operators.Shade(mesh, [.. mesh.Faces], smooth: false);
        Assert.True(middle.IsSharp);
        Assert.False(middle.IsSoft);
    }

    [Fact]
    public void GridFillingARingMakesQuads()
    {
        var mesh = new GeometryData();
        var ring = new List<GeometryData.Vertex>();
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.PI / 4f;
            ring.Add(mesh.AddVertex(MathF.Cos(a), 0, MathF.Sin(a)));
        }
        var selection = new ElementSelection();
        for (int i = 0; i < 8; i++) selection.Edges.Add(mesh.AddEdge(ring[i], ring[(i + 1) % 8]));

        Operators.GridFill(mesh, selection);

        Assert.NotEmpty(mesh.Faces);
        Assert.All(mesh.Faces, f => Assert.Equal(4, f.VertCount));
    }

    [Fact]
    public void ADrawnLineSplitsTheEdgeItEndsOnAndJoinsThePointItStartsOn()
    {
        GeometryData mesh = Strip();
        GeometryData.Vertex corner = mesh.Vertices[0];
        GeometryData.Edge far = mesh.FindEdge(mesh.Vertices[2], mesh.Vertices[5])!;
        var after = new ElementSelection();

        Operators.AddLine(mesh, [corner.Point, new Float3(1, 1, 0.25f), new Float3(2, 0, 0.5f)],
            new LineAnchor(corner.Point, corner), new LineAnchor(new Float3(2, 0, 0.5f), Edge: far), after);

        Assert.Equal(8, mesh.Vertices.Count);
        Assert.Equal(2, after.Edges.Count);
        GeometryData.Vertex split = mesh.Vertices.First(v => Float3.Distance(v.Point, new Float3(2, 0, 0.5f)) < 1e-5f);
        Assert.Equal(3, split.NeighborEdges().Count);
        Assert.Contains(after.Edges, e => e.Vert1 == corner || e.Vert2 == corner);
    }

    [Fact]
    public void ADrawnQuadFacesTheViewAndSharesTheCornersItSnappedTo()
    {
        GeometryData mesh = Strip();
        GeometryData.Vertex shared = mesh.Vertices[2];
        var after = new ElementSelection();

        GeometryData.Face made = Operators.AddQuad(mesh, [shared.Point, new Float3(3, 0, 0), new Float3(3, 0, 1), new Float3(2, 0, 1)],
            [shared, null, null, null], new Float3(0, -1, 0), after);

        Assert.Contains(shared, made.NeighborVertices());
        Assert.True(made.Normal().Y > 0.99f);
        Assert.Single(after.Faces);
    }

    [Fact]
    public void ShrinkwrapMovesPointsOntoTheNearestSurfaceAlongTheirNormal()
    {
        GeometryData mesh = Strip();
        float? Hit(Ray ray, float reach)
        {
            // A floor at y = -0.5
            if (MathF.Abs(ray.Direction.Y) < 1e-6f) return null;
            float t = (-0.5f - ray.Origin.Y) / ray.Direction.Y;
            return t >= 0f && t <= reach ? t : null;
        }

        int moved = Operators.Shrinkwrap(mesh, [.. mesh.Vertices], Float4x4.Identity, Hit, 0.01f, 1f);

        Assert.Equal(6, moved);
        Assert.All(mesh.Vertices, v => Assert.Equal(-0.49f, v.Point.Y, 4));
    }

    [Fact]
    public void FillingTwoPointsJoinsThemWithAnEdge()
    {
        var mesh = new GeometryData();
        GeometryData.Vertex a = mesh.AddVertex(0, 0, 0), b = mesh.AddVertex(1, 0, 0);
        var selection = new ElementSelection();
        selection.Vertices.UnionWith([a, b]);

        Operators.Fill(mesh, selection);

        Assert.NotNull(mesh.FindEdge(a, b));
    }

    [Fact]
    public void FillingAChainOfEdgesMakesAFaceFacingItsNeighbour()
    {
        var mesh = new GeometryData();
        GeometryData.Vertex a = mesh.AddVertex(0, 0, 0), b = mesh.AddVertex(1, 0, 0), c = mesh.AddVertex(1, 0, 1), d = mesh.AddVertex(0, 0, 1);
        GeometryData.Vertex e = mesh.AddVertex(2, 0, 0), f = mesh.AddVertex(2, 0, 1);
        GeometryData.Face first = mesh.AddFace(a, d, c, b)!;
        var selection = new ElementSelection();
        selection.Edges.UnionWith([mesh.FindEdge(b, c)!, mesh.AddEdge(b, e), mesh.AddEdge(e, f), mesh.AddEdge(f, c)]);
        selection.Vertices.UnionWith([b, c, e, f]);

        Operators.Fill(mesh, selection);

        GeometryData.Face made = Assert.Single(selection.Faces);
        Assert.Equal(4, made.VertCount);
        Assert.True(Float3.Dot(made.Normal(), first.Normal()) > 0.99f);
    }

    [Fact]
    public void ExtrudingAPointMakesAnEdgeToACopyAndSelectsIt()
    {
        var mesh = new GeometryData();
        GeometryData.Vertex a = mesh.AddVertex(0, 0, 0);
        mesh.AddFace(a, mesh.AddVertex(1, 0, 0), mesh.AddVertex(1, 0, 1));
        var after = new ElementSelection();

        Operators.ExtrudePoints(mesh, [a], after);

        GeometryData.Vertex copy = Assert.Single(after.Vertices);
        Assert.NotSame(a, copy);
        Assert.NotNull(mesh.FindEdge(a, copy));
        Assert.Single(mesh.Faces);
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

    private static Float3 CornerNormal(GeometryData.Loop loop)
    {
        float[] n = ((GeometryData.FloatAttributeValue)loop.Attributes[MeshGeometry.Normal]).Data;
        return new Float3(n[0], n[1], n[2]);
    }

    [Fact]
    public void ABoxSoftenedAt30DegreesHasFlatCornerNormals()
    {
        GeometryData mesh = Box().ToMesh(out _);
        GeometryOperators.SoftenByAngle(mesh, mesh.Edges, 30f);

        Operators.CornerNormals(mesh);

        foreach (GeometryData.Face face in mesh.Faces)
        {
            GeometryData.Loop loop = face.Loop!;
            do
            {
                Assert.True(Float3.Dot(CornerNormal(loop), face.Normal()) > 0.999f);
                loop = loop.Next!;
            } while (loop != face.Loop);
        }
    }

    [Fact]
    public void AManySidedPrismIsSmoothRoundItsSides()
    {
        var outline = Enumerable.Range(0, 32).Select(i => new Float2(MathF.Cos(i * MathF.PI / 16f), MathF.Sin(i * MathF.PI / 16f))).ToList();
        var points = outline.Select(p => new Float3(p.X, 0, p.Y)).Concat(outline.Select(p => new Float3(p.X, 1, p.Y))).ToList();
        var polygons = new List<int[]> { Enumerable.Range(0, 32).ToArray(), Enumerable.Range(32, 32).Reverse().ToArray() };
        for (int i = 0; i < 32; i++) polygons.Add([(i + 1) % 32, i, 32 + i, 32 + (i + 1) % 32]);
        CSGControlMesh prism = CSGControlMesh.FromPolygons(points, polygons);
        if (prism.Volume() < 0) prism.ReverseWinding();
        GeometryData mesh = prism.ToMesh(out _);
        GeometryOperators.SoftenByAngle(mesh, mesh.Edges, 30f);

        Operators.CornerNormals(mesh);

        GeometryData.Face side = mesh.Faces.First(f => MathF.Abs(f.Normal().Y) < 0.1f);
        GeometryData.Loop loop = side.Loop!;
        do
        {
            Float3 radial = Float3.Normalize(new Float3(loop.Vert.Point.X, 0, loop.Vert.Point.Z));
            Assert.True(Float3.Dot(CornerNormal(loop), radial) > 0.999f);
            loop = loop.Next!;
        } while (loop != side.Loop);
    }

    [Fact]
    public void SeparatingFacesMovesThemIntoAMeshOfTheirOwn()
    {
        GeometryData mesh = Primitives.Make(PrimitiveKind.Cube);
        GeometryData.Face top = mesh.Faces.First(f => f.Normal().Y > 0.99f);

        GeometryData part = Operators.Separate(mesh, [top]);

        Assert.Equal(5, mesh.Faces.Count);
        Assert.Single(part.Faces);
        Assert.Equal(4, part.Vertices.Count);
        Assert.True(part.HasLoopAttribute(MeshGeometry.UV));
        Assert.True(part.Faces[0].Normal().Y > 0.99f);
        Assert.Equal(8, mesh.Vertices.Count);
    }

    /// <summary>A mesh joined into another lands where it was in the world, its slots merged by material and its faces facing out still.</summary>
    [Fact]
    public void JoiningMovesTheOtherMeshIntoTheTargetsSpace()
    {
        GeometryData target = Primitives.Make(PrimitiveKind.Cube);
        GeometryData other = Primitives.Make(PrimitiveKind.Cube);
        Guid shared = Guid.NewGuid(), own = Guid.NewGuid();
        foreach (GeometryData.Face face in other.Faces) face.Attributes[MeshGeometry.SubMesh] = new GeometryData.IntAttributeValue(1);
        var targetSlots = new List<Guid> { shared };
        Float4x4 otherToTarget = Float4x4.CreateTranslation(new Float3(3, 0, 0)) * Float4x4.CreateScale(new Float3(-1, 1, 1));

        Operators.Join(target, targetSlots, other, [own, shared], otherToTarget);

        Assert.Equal(12, target.Faces.Count);
        Assert.Equal(new List<Guid> { shared, own }, targetSlots);
        var moved = target.Faces.Skip(6).ToList();
        Assert.All(moved, f => Assert.Equal(0, ((GeometryData.IntAttributeValue)f.Attributes[MeshGeometry.SubMesh]).Data[0]));
        Assert.All(moved, f => Assert.True(Float3.Dot(f.Normal(), f.Center() - new Float3(3, 0, 0)) > 0f));
    }

    private static (Dictionary<GeometryData.Vertex, Float3> Points, HashSet<GeometryData.Face> Faces) Before(GeometryData mesh)
        => (mesh.Vertices.ToDictionary(v => v, v => v.Point), new HashSet<GeometryData.Face>(mesh.Faces));

    /// <summary>Normals a mesh came with are worked out again only around points an edit moved.</summary>
    [Fact]
    public void MovingAPointWorksOutNormalsAroundItAgain()
    {
        GeometryData mesh = Primitives.Make(PrimitiveKind.Cube);
        GeometryOperators.SoftenByAngle(mesh, mesh.Edges, 30f);
        Operators.CornerNormals(mesh);
        var (points, faces) = Before(mesh);
        GeometryData.Vertex corner = mesh.Vertices.First(v => v.Point == new Float3(0.5f, 0.5f, 0.5f));
        GeometryData.Face bottom = mesh.Faces.First(f => f.Normal().Y < -0.99f);

        corner.Point = new Float3(0.5f, 1.5f, 0.5f);
        Operators.FinishEdit(mesh, points, faces);

        GeometryData.Face top = mesh.Faces.First(f => f.NeighborVertices().Contains(corner) && f.Normal().Y > 0.3f);
        Assert.True(CornerNormal(top.Loop!).Y < 0.99f);
        Assert.True(CornerNormal(bottom.Loop!).Y < -0.99f);
    }

    /// <summary>A face made from nothing takes the slot of its neighbour and the texture coordinates of the corners it stands on.</summary>
    [Fact]
    public void AFaceMadeFromNothingTakesItsNeighboursSlotAndCorners()
    {
        GeometryData mesh = Primitives.Make(PrimitiveKind.Cube);
        foreach (GeometryData.Face face in mesh.Faces) face.SetAttribute(MeshGeometry.SubMesh, new GeometryData.IntAttributeValue(2));
        GeometryData.Face top = mesh.Faces.First(f => f.Normal().Y > 0.99f);
        GeometryData.Vertex[] ring = [.. top.NeighborVertices()];
        GeometryOperators.DeleteFaces(mesh, [top]);
        var (points, faces) = Before(mesh);

        GeometryData.Face made = mesh.AddFace(ring)!;
        Operators.FinishEdit(mesh, points, faces);

        Assert.Equal(2, ((GeometryData.IntAttributeValue)made.Attributes[MeshGeometry.SubMesh]).Data[0]);
        GeometryData.Loop loop = made.Loop!;
        bool anyNonZero = false;
        do
        {
            float[] uv = ((GeometryData.FloatAttributeValue)loop.Attributes[MeshGeometry.UV]).Data;
            anyNonZero |= uv[0] != 0f || uv[1] != 0f;
            loop = loop.Next!;
        } while (loop != made.Loop);
        Assert.True(anyNonZero);
    }
}
