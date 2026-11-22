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
}
