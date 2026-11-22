// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class SelectionTests
{
    private static GeometryData Box()
    {
        CSGControlMesh box = CSGControlMesh.FromPlanes(GeometryGenerator.BoxPlanes(new Float3(2f), Float3.Zero))!;
        return box.ToMesh(out _);
    }

    /// <summary>An L shaped prism: a concave n gon cap with three edged corners all round, as brushes often are.</summary>
    private static GeometryData LPrism()
    {
        Float2[] outline = [new(0, 0), new(2, 0), new(2, 1), new(1, 1), new(1, 2), new(0, 2)];
        var points = new List<Float3>();
        foreach (Float2 p in outline) points.Add(new Float3(p.X, 0f, p.Y));
        foreach (Float2 p in outline) points.Add(new Float3(p.X, 1f, p.Y));
        int n = outline.Length;
        var polygons = new List<int[]>
        {
            Enumerable.Range(0, n).ToArray(),
            Enumerable.Range(n, n).Reverse().ToArray(),
        };
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            polygons.Add([j, i, n + i, n + j]);
        }
        CSGControlMesh mesh = CSGControlMesh.FromPolygons(points, polygons);
        if (mesh.Volume() < 0) mesh.ReverseWinding();
        return mesh.ToMesh(out _);
    }

    private static GeometryData.Face Facing(GeometryData mesh, Float3 normal)
        => mesh.Faces.First(f => Float3.Dot(f.Normal(), normal) > 0.99f);

    [Fact]
    public void SelectingAFaceSelectsItsEdgesAndCorners()
    {
        GeometryData mesh = Box();
        var selection = new ElementSelection();
        selection.Faces.Add(Facing(mesh, Float3.UnitY));

        Selecting.Normalize(selection, SelectMode.Face);

        Assert.Equal(4, selection.Vertices.Count);
        Assert.Equal(4, selection.Edges.Count);
    }

    [Fact]
    public void SwitchingFromVerticesToFacesKeepsFacesWithEveryCornerSelected()
    {
        GeometryData mesh = Box();
        var selection = new ElementSelection();
        foreach (GeometryData.Vertex v in Facing(mesh, Float3.UnitY).NeighborVertices()) selection.Vertices.Add(v);
        selection.Vertices.Add(Facing(mesh, -Float3.UnitY).NeighborVertices()[0]);

        Selecting.Normalize(selection, SelectMode.Vertex);
        Assert.Equal(5, selection.Edges.Count);
        Assert.Single(selection.Faces);

        Selecting.ToMode(selection, SelectMode.Vertex, SelectMode.Face);
        Assert.Single(selection.Faces);
        Assert.Equal(4, selection.Vertices.Count);
    }

    [Fact]
    public void InvertingFacesSelectsTheOthers()
    {
        GeometryData mesh = Box();
        var selection = new ElementSelection();
        selection.Faces.Add(Facing(mesh, Float3.UnitY));

        Selecting.Invert(mesh, selection, SelectMode.Face);

        Assert.Equal(5, selection.Faces.Count);
        Assert.DoesNotContain(Facing(mesh, Float3.UnitY), selection.Faces);
    }

    [Fact]
    public void MoreGrowsAndLessShrinksByOneRing()
    {
        GeometryData mesh = Box();
        var selection = new ElementSelection();
        selection.Faces.Add(Facing(mesh, Float3.UnitY));

        Selecting.More(selection, SelectMode.Face);
        Assert.Equal(5, selection.Faces.Count);

        Selecting.Less(mesh, selection, SelectMode.Face);
        Assert.Single(selection.Faces);
    }

    [Fact]
    public void LinkedSelectsTheWholeShell()
    {
        GeometryData mesh = Box();
        var selection = new ElementSelection();
        selection.Vertices.Add(mesh.Vertices[0]);

        Selecting.Linked(selection, SelectMode.Vertex);

        Assert.Equal(8, selection.Vertices.Count);
        Assert.Equal(6, selection.Faces.Count);
    }

    /// <summary>A loop from a cap's edge on a prism, whose corners have three edges, is the cap's outline on the cursor's side.</summary>
    [Fact]
    public void ALoopOnAPrismCapIsTheCapOutline()
    {
        GeometryData mesh = LPrism();
        GeometryData.Face top = Facing(mesh, Float3.UnitY);
        GeometryData.Edge edge = top.NeighborEdges()[0];

        List<GeometryData.Edge> loop = Selecting.Loop(edge, top);

        Assert.Equal(6, loop.Count);
        Assert.All(top.NeighborEdges(), e => Assert.Contains(e, loop));
    }

    /// <summary>A ring from a prism's upright edge goes round every upright edge, across its n gon sides.</summary>
    [Fact]
    public void ARingOnAPrismIsEveryUprightEdge()
    {
        GeometryData mesh = LPrism();
        GeometryData.Edge upright = mesh.Edges.First(e => MathF.Abs(e.Vert1.Point.Y - e.Vert2.Point.Y) > 0.5f);

        List<GeometryData.Edge> ring = Selecting.Ring(upright);

        Assert.Equal(6, ring.Count);
        Assert.All(ring, e => Assert.True(MathF.Abs(e.Vert1.Point.Y - e.Vert2.Point.Y) > 0.5f));
    }

    [Fact]
    public void SimilarFacesShareADirection()
    {
        GeometryData mesh = LPrism();
        var selection = new ElementSelection();
        GeometryData.Face side = mesh.Faces.First(f => Float3.Dot(f.Normal(), Float3.UnitX) > 0.99f);
        selection.Faces.Add(side);

        Selecting.Similar(mesh, selection, SelectMode.Face);

        Assert.Equal(2, selection.Faces.Count);
        Assert.All(selection.Faces, f => Assert.True(Float3.Dot(f.Normal(), Float3.UnitX) > 0.99f));
    }
}
