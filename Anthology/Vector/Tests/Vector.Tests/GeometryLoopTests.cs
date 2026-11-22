// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

using Vertex = Prowl.Vector.Geometry.GeometryData.Vertex;

namespace Prowl.Vector.Tests;

public class GeometryLoopTests
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

    private static GeometryData Cube() => GeometryGenerator.Box(new Float3(2, 2, 2));

    private static GeometryData.Edge EdgeAt(GeometryData mesh, Float3 a, Float3 b)
        => mesh.Edges.First(e => (e.Vert1.Point == a && e.Vert2.Point == b) || (e.Vert1.Point == b && e.Vert2.Point == a));

    [Fact]
    public void EdgeLoop_RunsAcrossASheet()
    {
        GeometryData mesh = Grid(4, 2);

        var loop = GeometryOperators.EdgeLoop(EdgeAt(mesh, new Float3(1, 1, 0), new Float3(2, 1, 0)));

        Assert.Equal(4, loop.Count);
        Assert.All(loop, e => Assert.True(e.Vert1.Point.Y == 1 && e.Vert2.Point.Y == 1));
    }

    [Fact]
    public void EdgeLoop_FollowsABorder_AllTheWayRound()
    {
        GeometryData mesh = Grid(3, 2);

        var loop = GeometryOperators.EdgeLoop(EdgeAt(mesh, new Float3(0, 0, 0), new Float3(1, 0, 0)));

        Assert.Equal(10, loop.Count);
        Assert.All(loop, e => Assert.Single(e.NeighborFaces()));
    }

    [Fact]
    public void EdgeLoop_StopsWhereThreeEdgesMeet()
    {
        GeometryData cube = Cube();

        Assert.Single(GeometryOperators.EdgeLoop(cube.Edges[0]));
    }

    [Fact]
    public void EdgeRing_RunsAcrossASheet_AndRoundACube()
    {
        GeometryData mesh = Grid(4, 2);
        var ring = GeometryOperators.EdgeRing(EdgeAt(mesh, new Float3(1, 0, 0), new Float3(1, 1, 0)));
        Assert.Equal(5, ring.Count);
        Assert.All(ring, e => Assert.True(Math.Abs(e.Vert1.Point.Y - e.Vert2.Point.Y) == 1));

        Assert.Equal(4, GeometryOperators.EdgeRing(Cube().Edges[0]).Count);
    }

    [Fact]
    public void LoopCut_SplitsARingOfACube()
    {
        GeometryData cube = Cube();

        var cut = GeometryOperators.LoopCut(cube, cube.Edges[0]);

        Assert.Equal(4, cut.Count);
        Assert.Equal(12, cube.Vertices.Count);
        Assert.Equal(20, cube.Edges.Count);
        Assert.Equal(10, cube.Faces.Count);
        Assert.All(cube.Faces, f => Assert.Equal(4, f.VertCount));
        Assert.True(cube.IsClosed());
        Assert.Equal(8f, cube.Volume(), 3);
    }

    [Fact]
    public void LoopCut_WithSeveralCuts_SpacesThemEvenly()
    {
        GeometryData mesh = Grid(1, 3);
        GeometryData.Edge side = EdgeAt(mesh, new Float3(0, 0, 0), new Float3(1, 0, 0));

        var cut = GeometryOperators.LoopCut(mesh, side, 3);

        // Three cuts across four ring edges make three runs of three new edges
        Assert.Equal(9, cut.Count);
        Assert.Equal(12, mesh.Faces.Count);
        Assert.All(mesh.Faces, f => Assert.Equal(4, f.VertCount));
        float[] columns = mesh.Vertices.Select(v => v.Point.X).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal([0f, 0.25f, 0.5f, 0.75f, 1f], columns.Select(c => MathF.Round(c, 4)).ToArray());
    }

    [Fact]
    public void LoopCut_Slide_MovesTheCutAlongTheRing()
    {
        GeometryData mesh = Grid(1, 2);
        GeometryData.Edge side = EdgeAt(mesh, new Float3(0, 0, 0), new Float3(1, 0, 0));

        GeometryOperators.LoopCut(mesh, side, 1, 0.5f);

        float cutAt = mesh.Vertices.Select(v => v.Point.X).Distinct().Single(x => x > 0.01f && x < 0.99f);
        Assert.True(Math.Abs(cutAt - 0.5f) > 0.2f);
        Assert.Single(mesh.Vertices.Select(v => MathF.Round(v.Point.X, 3)).Distinct().Where(x => x > 0.01f && x < 0.99f));
    }

    [Fact]
    public void LoopCut_InterpolatesLoopAttributes()
    {
        GeometryData mesh = Grid(1, 1);
        mesh.AddLoopAttribute(GeometryData.UvAttribute, GeometryData.AttributeBaseType.Float, 2);
        foreach (GeometryData.Loop loop in mesh.Loops)
            loop.Attributes[GeometryData.UvAttribute] = new GeometryData.FloatAttributeValue(loop.Vert.Point.X, loop.Vert.Point.Y);

        GeometryOperators.LoopCut(mesh, EdgeAt(mesh, new Float3(0, 0, 0), new Float3(1, 0, 0)));

        Assert.Equal(2, mesh.Faces.Count);
        foreach (GeometryData.Loop loop in mesh.Loops)
        {
            float[] uv = loop.Attributes[GeometryData.UvAttribute].AsFloat()!.Data;
            Assert.Equal(loop.Vert.Point.X, uv[0], 3);
            Assert.Equal(loop.Vert.Point.Y, uv[1], 3);
        }
    }

    [Fact]
    public void SlideEdges_MovesALoopTowardsItsNeighbours()
    {
        GeometryData mesh = Grid(2, 2);
        var loop = GeometryOperators.EdgeLoop(EdgeAt(mesh, new Float3(0, 1, 0), new Float3(1, 1, 0)));

        Assert.Equal(3, GeometryOperators.SlideEdges(mesh, loop, 0.5f));

        // All three vertices of the middle row went the same way by half a row
        float[] rows = mesh.Vertices.Select(v => v.Point.Y).Distinct().OrderBy(y => y).ToArray();
        Assert.Equal(3, rows.Length);
        Assert.True(Math.Abs(Math.Abs(rows[1] - 1) - 0.5f) < 1e-4f);

        GeometryOperators.SlideEdges(mesh, loop, -1f);
        Assert.Equal(2, mesh.Vertices.Select(v => MathF.Round(v.Point.Y, 3)).Distinct().Count());
    }

    [Fact]
    public void BridgeEdges_JoinsTwoRings_IntoATube()
    {
        GeometryData mesh = new();
        var lower = new[] { mesh.AddVertex(0, 0, 0), mesh.AddVertex(1, 0, 0), mesh.AddVertex(1, 0, 1), mesh.AddVertex(0, 0, 1) };
        var upper = new[] { mesh.AddVertex(1, 2, 0), mesh.AddVertex(1, 2, 1), mesh.AddVertex(0, 2, 1), mesh.AddVertex(0, 2, 0) };
        for (int i = 0; i < 4; i++)
        {
            mesh.AddEdge(lower[i], lower[(i + 1) % 4]);
            mesh.AddEdge(upper[i], upper[(i + 1) % 4]);
        }

        var faces = GeometryOperators.BridgeEdges(mesh, mesh.Edges.ToList());

        Assert.Equal(4, faces.Count);
        Assert.Equal(12, mesh.Edges.Count);
        // Every side stands upright, so the rings were lined up and not twisted
        Assert.All(faces, f => Assert.True(Math.Abs(f.Normal().Y) < 1e-4f));
        Assert.All(faces, f => Assert.Equal(2f, f.Area(), 3));
    }

    private static Vertex[] RingOf(GeometryData mesh, int count, float radius, float height)
    {
        Vertex[] ring = Enumerable.Range(0, count)
            .Select(i => mesh.AddVertex(MathF.Cos(i * MathF.Tau / count) * radius, height, MathF.Sin(i * MathF.Tau / count) * radius)).ToArray();
        for (int i = 0; i < count; i++)
            mesh.AddEdge(ring[i], ring[(i + 1) % count]);
        return ring;
    }

    [Theory]
    [InlineData(12, 8)]
    [InlineData(8, 12)]
    [InlineData(12, 4)]
    [InlineData(9, 5)]
    public void BridgeEdges_RingsOfDifferentSize_AreJoinedWithQuadsOnly(int lower, int upper)
    {
        GeometryData mesh = new();
        RingOf(mesh, lower, 2, 0);
        RingOf(mesh, upper, 1, 2);
        int narrowings = Math.Abs(lower - upper) / 2;

        var faces = GeometryOperators.BridgeEdges(mesh, mesh.Edges.ToList());

        Assert.Equal(Math.Min(lower, upper) + narrowings * 3, faces.Count);
        Assert.All(faces, f => Assert.Equal(4, f.VertCount));
        Assert.Equal(lower + upper + narrowings * 2, mesh.Vertices.Count);

        // A closed strip between the two rings: only the ring edges are borders, and every face looks outwards
        Assert.Equal(lower + upper, mesh.Edges.Count(e => e.NeighborFaces().Count == 1));
        Assert.All(mesh.Edges, e => Assert.InRange(e.NeighborFaces().Count, 1, 2));
        int outwards = faces.Count(f => Float3.Dot(f.Normal(), f.Center() - new Float3(0, f.Center().Y, 0)) > 0);
        Assert.True(outwards == 0 || outwards == faces.Count);
    }

    [Fact]
    public void BridgeEdges_OpenRunsOfDifferentLength_NarrowBetweenTheirEnds()
    {
        GeometryData mesh = new();
        Vertex[] wide = Enumerable.Range(0, 6).Select(i => mesh.AddVertex(i, 0, 0)).ToArray();
        Vertex[] narrow = Enumerable.Range(0, 4).Select(i => mesh.AddVertex(i * 5f / 3f, 2, 0)).ToArray();
        for (int i = 0; i < 5; i++) mesh.AddEdge(wide[i], wide[i + 1]);
        for (int i = 0; i < 3; i++) mesh.AddEdge(narrow[i], narrow[i + 1]);

        var faces = GeometryOperators.BridgeEdges(mesh, mesh.Edges.ToList());

        Assert.Equal(6, faces.Count);
        Assert.All(faces, f => Assert.Equal(4, f.VertCount));
        Assert.NotNull(mesh.FindEdge(wide[0], narrow[0]));
        Assert.NotNull(mesh.FindEdge(wide[5], narrow[3]));
        Assert.All(faces, f => Assert.True(f.Normal().Z * faces[0].Normal().Z > 0));
    }

    [Fact]
    public void BridgeEdges_NewVerticesOfANarrowing_BlendWhatTheirNeighboursCarry()
    {
        GeometryData mesh = new();
        Vertex[] wide = RingOf(mesh, 6, 2, 0);
        Vertex[] narrow = RingOf(mesh, 4, 1, 2);
        mesh.AddVertexAttribute("heat", GeometryData.AttributeBaseType.Float, 1);
        foreach (Vertex vertex in wide) vertex.SetAttribute("heat", new GeometryData.FloatAttributeValue { Data = [1] });
        foreach (Vertex vertex in narrow) vertex.SetAttribute("heat", new GeometryData.FloatAttributeValue { Data = [3] });

        GeometryOperators.BridgeEdges(mesh, mesh.Edges.ToList());

        Assert.Equal(12, mesh.Vertices.Count);
        foreach (Vertex made in mesh.Vertices.Skip(10))
        {
            Assert.InRange(made.Point.Y, 0.9f, 1.1f);
            Assert.True(made.TryGetAttribute("heat", out var value));
            Assert.Equal(2f, ((GeometryData.FloatAttributeValue)value!).Data[0], 4);
        }
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(14, 4)]
    public void BridgeEdges_RingsThatCannotBeJoinedWithQuads_Throw(int lower, int upper)
    {
        GeometryData mesh = new();
        RingOf(mesh, lower, 2, 0);
        RingOf(mesh, upper, 1, 2);

        Assert.Throws<InvalidOperationException>(() => GeometryOperators.BridgeEdges(mesh, mesh.Edges.ToList()));
        Assert.Empty(mesh.Faces);
        Assert.Equal(lower + upper, mesh.Vertices.Count);
    }

    [Fact]
    public void BridgeEdges_RunsOfDifferentLength_Throw()
    {
        GeometryData mesh = new();
        mesh.AddEdge(mesh.AddVertex(0, 0, 0), mesh.AddVertex(1, 0, 0));
        var b = new[] { mesh.AddVertex(0, 1, 0), mesh.AddVertex(1, 1, 0), mesh.AddVertex(2, 1, 0) };
        mesh.AddEdge(b[0], b[1]);
        mesh.AddEdge(b[1], b[2]);

        Assert.Throws<InvalidOperationException>(() => GeometryOperators.BridgeEdges(mesh, mesh.Edges.ToList()));
        Assert.Empty(mesh.Faces);
    }

    [Fact]
    public void SmoothVertices_FlattensABump_AndKeepsBorderVerticesOnTheBorder()
    {
        GeometryData mesh = Grid(2, 2);
        mesh.Vertices[4].Point = new Float3(1, 1, 1);

        GeometryOperators.SmoothVertices(mesh, mesh.Vertices.ToList(), 1f);

        Assert.Equal(0f, mesh.Vertices[4].Point.Z, 4);
        // The middle of each side only follows the corners either side of it, so it stays where it was
        Assert.Equal(new Float3(1, 0, 0), mesh.Vertices[1].Point);
        Assert.Equal(new Float3(0, 1, 0), mesh.Vertices[3].Point);
        Assert.All(mesh.Vertices, v => Assert.Equal(0f, v.Point.Z, 4));
    }

    [Fact]
    public void MirrorCopy_SharesVerticesOnThePlane()
    {
        GeometryData mesh = Grid(1, 1);

        GeometryData mirrored = GeometryOperators.MirrorCopy(mesh, 0);

        Assert.Equal(6, mirrored.Vertices.Count);
        Assert.Equal(2, mirrored.Faces.Count);
        Assert.Equal(7, mirrored.Edges.Count);
        Assert.All(mirrored.Faces, f => Assert.True(f.Normal().Z > 0.99f));
        Assert.Equal(-1f, mirrored.Vertices.Min(v => v.Point.X));
        Assert.Single(mesh.Faces);
    }

    [Fact]
    public void SubdivideSmooth_OfACube_IsAllQuads_AndRounder()
    {
        GeometryData cube = Cube();

        GeometryData once = GeometryOperators.SubdivideSmooth(cube);

        Assert.Equal(26, once.Vertices.Count);
        Assert.Equal(48, once.Edges.Count);
        Assert.Equal(24, once.Faces.Count);
        Assert.All(once.Faces, f => Assert.Equal(4, f.VertCount));
        Assert.True(once.IsClosed());
        Assert.InRange(once.Volume(), 3f, cube.Volume() - 0.01f);
        // The corners pull in towards the middle
        Assert.True(once.Vertices.Max(v => Float3.Length(v.Point)) < Float3.Length(new Float3(1, 1, 1)));

        GeometryData twice = GeometryOperators.SubdivideSmooth(cube, 2);
        Assert.Equal(96, twice.Faces.Count);
        Assert.Equal(6, cube.Faces.Count);
    }

    [Fact]
    public void SubdivideSmooth_KeepsBorders_AndSharpEdges()
    {
        GeometryData sheet = Grid(2, 2);
        GeometryData smoothed = GeometryOperators.SubdivideSmooth(sheet);
        Assert.Equal(16, smoothed.Faces.Count);
        Assert.All(smoothed.Vertices, v => Assert.Equal(0f, v.Point.Z, 5));
        Assert.Equal(0f, smoothed.Vertices.Min(v => v.Point.X), 5);
        Assert.Equal(2f, smoothed.Vertices.Max(v => v.Point.X), 5);

        GeometryData cube = Cube();
        cube.AddEdgeAttribute(GeometryData.SharpAttribute, GeometryData.AttributeBaseType.Int, 1);
        foreach (GeometryData.Edge edge in cube.Edges) edge.IsSharp = true;
        GeometryData hard = GeometryOperators.SubdivideSmooth(cube);
        Assert.Equal(cube.Volume(), hard.Volume(), 3);
    }

    [Fact]
    public void SubdivideSmooth_InterpolatesLoopAttributes()
    {
        GeometryData mesh = Grid(1, 1);
        mesh.AddLoopAttribute(GeometryData.UvAttribute, GeometryData.AttributeBaseType.Float, 2);
        foreach (GeometryData.Loop loop in mesh.Loops)
            loop.Attributes[GeometryData.UvAttribute] = new GeometryData.FloatAttributeValue(loop.Vert.Point.X, loop.Vert.Point.Y);

        GeometryData smoothed = GeometryOperators.SubdivideSmooth(mesh);

        // The corners of the sheet round off, but their coordinates stay. Everywhere else a corner's coordinate is still its position.
        Assert.Equal(16, smoothed.Loops.Count);
        foreach (GeometryData.Loop loop in smoothed.Loops)
        {
            float[] uv = loop.Attributes[GeometryData.UvAttribute].AsFloat()!.Data;
            bool corner = (uv[0] == 0 || uv[0] == 1) && (uv[1] == 0 || uv[1] == 1);
            if (corner) continue;
            Assert.Equal(loop.Vert.Point.X, uv[0], 4);
            Assert.Equal(loop.Vert.Point.Y, uv[1], 4);
        }
        Assert.Equal(4, smoothed.Loops.Count(l => l.Attributes[GeometryData.UvAttribute].AsFloat()!.Data is [0.5f, 0.5f]));
    }

    [Fact]
    public void MirrorCopy_AcrossAnOffsetPlane_MeetsThereAndReachesAsFarBeyond()
    {
        GeometryData mesh = new();
        mesh.AddFace(mesh.AddVertex(1, 0, 0), mesh.AddVertex(3, 0, 0), mesh.AddVertex(3, 1, 0), mesh.AddVertex(1, 1, 0));

        GeometryData mirrored = GeometryOperators.MirrorCopy(mesh, 0, 0.001f, 1f);

        Assert.Equal(6, mirrored.Vertices.Count);
        Assert.Equal(2, mirrored.Faces.Count);
        Assert.Equal(-1f, mirrored.Vertices.Min(v => v.Point.X), 4);
        Assert.Equal(3f, mirrored.Vertices.Max(v => v.Point.X), 4);
    }

    private static GeometryData.Face Top(GeometryData mesh) => mesh.Faces.First(f => f.Normal().Y > 0.99f);

    [Fact]
    public void SubdivideFaces_SplitsOnlyTheGivenFaces_AndStaysClosed()
    {
        GeometryData cube = Cube();

        List<GeometryData.Face> made = GeometryOperators.SubdivideFaces(cube, [Top(cube)]);

        Assert.Equal(4, made.Count);
        Assert.All(made, f => Assert.Equal(4, f.VertCount));
        Assert.Equal(9, cube.Faces.Count);
        Assert.Equal(13, cube.Vertices.Count);
        Assert.True(cube.IsClosed());
        Assert.Equal(8f, cube.Volume(), 3);
    }

    [Fact]
    public void SubdivideFaces_TwiceMakesSixteen()
    {
        GeometryData cube = Cube();

        List<GeometryData.Face> made = GeometryOperators.SubdivideFaces(cube, [Top(cube)], 2);

        Assert.Equal(16, made.Count);
        Assert.True(cube.IsClosed());
        Assert.Equal(8f, cube.Volume(), 3);
    }

    [Fact]
    public void Triangulate_OnlyTheGivenFaces()
    {
        GeometryData cube = Cube();

        List<GeometryData.Face> made = GeometryOperators.Triangulate(cube, [Top(cube)]);

        Assert.Equal(2, made.Count);
        Assert.Equal(7, cube.Faces.Count);
        Assert.Equal(5, cube.Faces.Count(f => f.VertCount == 4));
        Assert.True(cube.IsClosed());
    }

    [Fact]
    public void TrisToQuads_JoinsATriangulatedCubeBack()
    {
        GeometryData cube = Cube();
        GeometryOperators.Triangulate(cube);
        Assert.Equal(12, cube.Faces.Count);

        List<GeometryData.Face> made = GeometryOperators.TrisToQuads(cube, null, 5f);

        Assert.Equal(6, made.Count);
        Assert.Equal(6, cube.Faces.Count);
        Assert.All(cube.Faces, f => Assert.Equal(4, f.VertCount));
        Assert.True(cube.IsClosed());
    }

    [Fact]
    public void TrisToQuads_KeepsToTheGivenFaces()
    {
        GeometryData cube = Cube();
        GeometryOperators.Triangulate(cube);
        var top = cube.Faces.Where(f => f.Normal().Y > 0.99f).ToList();

        GeometryOperators.TrisToQuads(cube, top, 5f);

        Assert.Equal(11, cube.Faces.Count);
    }
}
