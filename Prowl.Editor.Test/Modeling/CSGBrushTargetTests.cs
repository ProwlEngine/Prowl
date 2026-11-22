// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class CSGBrushTargetTests
{
    private static CSGControlMesh Box() => CSGControlMesh.FromPlanes(GeometryGenerator.BoxPlanes(new Float3(2f), Float3.Zero))!;

    /// <summary>Each polygon's surface, marked so a test can tell which surface a face carries.</summary>
    private static SurfaceData[] Marked(CSGControlMesh mesh)
        => Enumerable.Range(0, mesh.Polygons.Count).Select(p => new SurfaceData { Scale = Float2.One, Offset = new Float2(p, 0f) }).ToArray();

    private static int Facing(CSGControlMesh mesh, Float3 normal)
        => Enumerable.Range(0, mesh.Polygons.Count).First(p => Float3.Dot(mesh.PolygonNormal(p), normal) > 0.99f);

    private static GeometryData.Face FaceFacing(GeometryData mesh, Float3 normal)
        => mesh.Faces.First(f => Float3.Dot(f.Normal(), normal) > 0.99f);

    [Fact]
    public void ABrushComesBackAsItWent()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));

        (CSGControlMesh mesh, SurfaceData[] surfaces) = target.ToBrush();

        Assert.Equal(box.Polygons.Count, mesh.Polygons.Count);
        Assert.Equal(box.Points, mesh.Points);
        Assert.Equal(box.Polygons, mesh.Polygons);
        Assert.Equal(8f, mesh.Volume(), 4);
        for (int p = 0; p < mesh.Polygons.Count; p++)
            Assert.Equal(Facing(box, mesh.PolygonNormal(p)), (int)surfaces[p].Offset.X);
    }

    /// <summary>A face split into pieces joined by a hidden edge goes back as the one polygon it reads as.</summary>
    [Fact]
    public void PiecesJoinedByHiddenEdgesGoBackAsOnePolygon()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData.Face top = FaceFacing(target.Target.Mesh, Float3.UnitY);
        var corners = top.NeighborVertices();
        GeometryData.Face? piece = GeometryOperators.SplitFace(target.Target.Mesh, top, corners[0], corners[2], out GeometryData.Edge? cut);
        Assert.NotNull(piece);
        cut!.IsHidden = true;

        (CSGControlMesh mesh, _) = target.ToBrush();

        Assert.Equal(6, mesh.Polygons.Count);
        Assert.Equal(8f, mesh.Volume(), 4);
    }

    /// <summary>A face made from nothing takes the surface nearest it by normal.</summary>
    [Fact]
    public void AFaceMadeFromNothingTakesTheNearestSurface()
    {
        CSGControlMesh box = Box();
        SurfaceData[] marked = Marked(box);
        var target = new CSGBrushTarget(box, marked);
        GeometryData geometry = target.Target.Mesh;

        GeometryData.Face bottom = FaceFacing(geometry, -Float3.UnitY);
        var ring = bottom.NeighborVertices().ToArray();
        geometry.RemoveFace(bottom);
        geometry.AddFace(ring);

        (CSGControlMesh mesh, SurfaceData[] surfaces) = target.ToBrush();

        int made = Facing(mesh, -Float3.UnitY);
        Assert.Equal(Facing(box, -Float3.UnitY), (int)surfaces[made].Offset.X);
    }

    /// <summary>Points no face uses any more are dropped on the way back.</summary>
    [Fact]
    public void LoosePointsAreDropped()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        target.Target.Mesh.AddVertex(new Float3(5f, 5f, 5f));

        (CSGControlMesh mesh, _) = target.ToBrush();

        Assert.Equal(8, mesh.Points.Count);
    }

    /// <summary>An edit that folds a brush through itself is refused and the mesh put back.</summary>
    [Fact]
    public void AnEditFoldingTheBrushIsRefusedAndUndone()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData geometry = target.Target.Mesh;
        Float3[] before = geometry.Vertices.Select(v => v.Point).ToArray();

        string? problem = MeshEdit.Run(target.Target, (mesh, selected) =>
        {
            GeometryData.Vertex corner = mesh.Vertices.First(v => Float3.Distance(v.Point, new Float3(1f, 1f, 1f)) < 1e-4f);
            corner.Point += new Float3(-1.5f, -3.5f, -1.5f);
        }, new ElementSelection());

        Assert.NotNull(problem);
        Assert.Equal(before, target.Target.Mesh.Vertices.Select(v => v.Point).ToArray());
    }

    /// <summary>An edit leaving a hole is kept, since the brush repairs it when it builds.</summary>
    [Fact]
    public void AnEditLeavingAHoleIsKept()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));

        string? problem = MeshEdit.Run(target.Target, (mesh, selected) => mesh.RemoveFace(FaceFacing(mesh, Float3.UnitY)), new ElementSelection());

        Assert.Null(problem);
        Assert.Equal(5, target.Target.Mesh.Faces.Count);
    }

    /// <summary>A selection carried through an edit comes back as the indices its points and faces end up at.</summary>
    [Fact]
    public void ASelectionIsCarriedToItsNewIndices()
    {
        CSGControlMesh box = Box();
        var target = new CSGBrushTarget(box, Marked(box));
        GeometryData geometry = target.Target.Mesh;

        // The first point goes, with its faces, so every later point and face moves down
        GeometryData.Vertex first = geometry.Vertices[0];
        GeometryData.Vertex corner = geometry.Vertices.OrderByDescending(v => Float3.Distance(v.Point, first.Point)).First();
        GeometryData.Face away = geometry.Faces.First(f => !f.NeighborVertices().Contains(first));
        Float3 awayNormal = away.Normal();
        geometry.RemoveVertex(first);

        var carry = new ElementSelection();
        carry.Vertices.Add(corner);
        carry.Faces.Add(away);

        (CSGControlMesh mesh, _) = target.ToBrush(carry, out HashSet<int> points, out List<int> faces);

        int point = Assert.Single(points);
        Assert.Equal(corner.Point, mesh.Points[point]);
        int face = Assert.Single(faces);
        Assert.True(Float3.Dot(mesh.PolygonNormal(face), awayNormal) > 0.99f);
    }
}
