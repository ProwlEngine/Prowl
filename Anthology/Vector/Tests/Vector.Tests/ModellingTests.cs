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

/// <summary>The modelling operators run on whole shapes: drawing, push pull, holes, bevels, sweeps and the rest.</summary>
public class ModellingTests
{
    private static List<Edge> LooseLoop(GeometryData mesh, params Float3[] points)
    {
        var verts = points.Select(mesh.AddVertex).ToList();
        var edges = new List<Edge>();
        for (int i = 0; i < verts.Count; i++) edges.Add(mesh.AddEdge(verts[i], verts[(i + 1) % verts.Count])!);
        return edges;
    }

    [Fact]
    public void FillFlat()
    {
        var mesh = new GeometryData();
        List<Edge> loop = LooseLoop(mesh, new(0, 0, 0), new(4, 0, 0), new(4, 0, 4), new(0, 0, 4));
        int made = GeometryOperators.FillLoop(mesh, loop, 0);
        Check(made == 1 && mesh.Faces.Count == 1, $"made {made}");
        Check(MathF.Abs(Area(mesh.Faces[0]) - 16f) < 1e-3f, "area");
    }

    [Fact]
    public void FillBent()
    {
        var mesh = new GeometryData();
        List<Edge> loop = LooseLoop(mesh, new(0, 0, 0), new(4, 0, 0), new(4, 1, 4), new(0, 0, 4));
        int made = GeometryOperators.FillLoop(mesh, loop, 0);
        Check(made == 2, $"made {made}");
        Check(mesh.Edges.Count(e => e.IsHidden) == 1, "diagonal not hidden");
        Check(mesh.Edges.All(e => e.FaceCount >= 1), "an edge has no face");
    }
    [Fact]
    public void OrientBox()
    {
        GeometryData mesh = Box(2f);
        GeometryOperators.FlipFaces(mesh, [Top(mesh, 2f)]);
        GeometryOperators.FlipFaces(mesh, [mesh.Faces.First(f => f.Normal().X > 0.9f)]);
        int turned = GeometryOperators.OrientFaces(mesh);
        Check(turned == 2, $"turned {turned}");
        CheckSolid(mesh, 8f);
    }

    [Fact]
    public void OrientInsideOut()
    {
        GeometryData mesh = Box(2f);
        GeometryOperators.FlipFaces(mesh, mesh.Faces.ToList());
        Check(Volume(mesh) < 0f, "not inside out");
        GeometryOperators.OrientFaces(mesh);
        CheckSolid(mesh, 8f);
    }

    [Fact]
    public void HiddenSurvives()
    {
        GeometryData mesh = Box(1f);
        Face top = Top(mesh, 1f);
        top.IsHidden = true;
        GeometryOperators.PushPull(mesh, [top], 1f, false);
        Face moved = Top(mesh, 2f);
        Check(moved.IsHidden, "the moved face lost its hidden flag");
        GeometryOperators.Paint(mesh, [moved], 3);
        Check(moved.IsHidden && moved.Material == 3, "paint changed the hidden flag");
    }

    [Fact]
    public void SoftenAngles()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 4, 2), Float3.UnitY, Down, 0);
        // A gentle fold along the middle and a sharp one on the end
        GeometryOperators.DrawSegments(mesh, [(new Float3(2, 0, 0), new Float3(2, 0, 2))], null, Down, 0);
        foreach (Vertex v in mesh.Vertices.Where(v => v.Point.X > 3.9f).ToList()) v.Point += new Float3(0, 0.3f, 0);
        mesh.MarkChanged();
        int changed = GeometryOperators.SoftenByAngle(mesh, mesh.Edges.ToList(), 45f);
        Check(changed >= 1, $"changed {changed}");
    }
    private static List<(Float3, Float3)> YzRect(float x, float y0, float z0, float y1, float z1)
        => Loop(new(x, y0, z0), new(x, y1, z0), new(x, y1, z1), new(x, y0, z1));

    private static Edge PathEdge(GeometryData mesh, Float3 a, Float3 b)
    {
        Vertex va = mesh.FindVertex(a) ?? mesh.AddVertex(a), vb = mesh.FindVertex(b) ?? mesh.AddVertex(b);
        return mesh.AddEdge(va, vb)!;
    }

    [Fact]
    public void FollowStraight()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, YzRect(0, 0, 0, 1, 1), Float3.UnitX, -Float3.UnitX, 0);
        Face profile = mesh.Faces[0];
        Edge path = PathEdge(mesh, new(0, 0.5f, 0.5f), new(4, 0.5f, 0.5f));
        GeometryOperators.FollowMe(mesh, [profile], [path]);
        mesh.RemoveEdge(path);
        CheckSolid(mesh, 4f);
        Check(mesh.Faces.Count == 6, $"faces {mesh.Faces.Count}");
    }

    [Fact]
    public void FollowElbow()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, YzRect(0, 0, 0, 1, 1), Float3.UnitX, -Float3.UnitX, 0);
        Face profile = mesh.Faces[0];
        Edge a = PathEdge(mesh, new(0, 0.5f, 0.5f), new(4, 0.5f, 0.5f));
        Edge b = PathEdge(mesh, new(4, 0.5f, 0.5f), new(4, 0.5f, 4.5f));
        GeometryOperators.FollowMe(mesh, [profile], [a, b]);
        mesh.RemoveEdge(a);
        mesh.RemoveEdge(b);
        CheckSolid(mesh, 8f);
    }

    [Fact]
    public void FollowLoop()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, YzRect(0, -0.1f, -0.1f, 0.1f, 0.1f), Float3.UnitX, -Float3.UnitX, 0);
        Face profile = mesh.Faces[0];
        Float3[] corners = [new(0, 0, 0), new(4, 0, 0), new(4, 0, 4), new(0, 0, 4)];
        var path = new List<Edge>();
        for (int i = 0; i < 4; i++) path.Add(PathEdge(mesh, corners[i], corners[(i + 1) % 4]));
        GeometryOperators.FollowMe(mesh, [profile], path);
        foreach (Edge e in path) mesh.RemoveEdge(e);
        CheckClosed(mesh);
        float volume = MathF.Abs(Volume(mesh));
        Check(MathF.Abs(volume - 0.64f) < 1e-2f, $"volume {volume}");
    }

    [Fact]
    public void FollowHollow()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, YzRect(0, 0, 0, 2, 2), Float3.UnitX, -Float3.UnitX, 0);
        GeometryOperators.DrawSegments(mesh, YzRect(0, 0.5f, 0.5f, 1.5f, 1.5f), Float3.UnitX, -Float3.UnitX, 0);
        Face ring = mesh.Faces.OrderByDescending(Area).First();
        List<Face> region = ring.LogicalFace();
        GeometryOperators.DeleteFaces(mesh, mesh.Faces.Where(f => !region.Contains(f)).ToList());
        Edge path = PathEdge(mesh, new(0, 1f, 1f), new(3, 1f, 1f));
        GeometryOperators.FollowMe(mesh, region, [path]);
        mesh.RemoveEdge(path);
        CheckClosed(mesh);
        float volume = MathF.Abs(Volume(mesh));
        Check(MathF.Abs(volume - 9f) < 1e-2f, $"volume {volume}");
    }
    private static bool PointInside(GeometryData mesh, Float3 p)
    {
        // Even odd count of crossings along a skewed ray, which a closed mesh makes reliable
        Float3 dir = Float3.Normalize(new Float3(1f, 0.3719f, 0.2143f));
        int hits = 0;
        foreach (Face f in mesh.Faces)
        {
            List<Float3> pts = f.NeighborVertices().Select(v => v.Point).ToList();
            var frame = new PlaneFrame(f.Normal(), pts[0]);
            List<int> tris = Polygon2D.Triangulate(pts.Select(frame.To2D).ToList());
            for (int t = 0; t < tris.Count; t += 3)
                if (Prowl.Vector.Geometry.Intersection.RayTriangle(p, dir, pts[tris[t]], pts[tris[t + 1]], pts[tris[t + 2]], out _, out _, out _)) hits++;
        }
        return hits % 2 == 1;
    }

    private static void SameSolid(GeometryData mesh, Func<Float3, bool> inside, float size, float tolerance)
    {
        var rng = new Random(7);
        int wrong = 0, samples = 6000;
        for (int i = 0; i < samples; i++)
        {
            var p = new Float3(rng.NextSingle() * size, rng.NextSingle() * size, rng.NextSingle() * size);
            if (PointInside(mesh, p) != inside(p)) wrong++;
        }
        Check(wrong <= samples * tolerance, $"{wrong} of {samples} sample points disagree with the expected solid");
    }

    private static Edge BoxEdge(GeometryData mesh, Float3 a, Float3 b) => mesh.Edges.First(e =>
        (Float3.Length(e.Vert1.Point - a) < 1e-3f && Float3.Length(e.Vert2.Point - b) < 1e-3f)
        || (Float3.Length(e.Vert1.Point - b) < 1e-3f && Float3.Length(e.Vert2.Point - a) < 1e-3f));

    private static bool ChamferInside(Float3 p, float w, float size, bool corners = true)
    {
        float[] d = [p.X, size - p.X, p.Y, size - p.Y, p.Z, size - p.Z];
        for (int i = 0; i < 6; i++)
            for (int j = i + 1; j < 6; j++)
            {
                if (i / 2 == j / 2) continue;
                if (d[i] + d[j] < w) return false;
            }
        if (!corners) return true;
        for (int sx = 0; sx < 2; sx++)
            for (int sy = 2; sy < 4; sy++)
                for (int sz = 4; sz < 6; sz++)
                    if (d[sx] + d[sy] + d[sz] < 2f * w) return false;
        return true;
    }

    [Fact]
    public void BevelOneEdge()
    {
        GeometryData mesh = Box(2f);
        Edge e = BoxEdge(mesh, new(2, 0, 0), new(2, 2, 0));
        GeometryOperators.BevelEdges(mesh, [e], 0.5f);
        Check(mesh.Faces.Count == 7, $"faces {mesh.Faces.Count}");
        CheckClosed(mesh);
        float volume = Volume(mesh);
        Check(MathF.Abs(volume - 7.75f) < 1e-3f, $"volume {volume}");
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
    }

    [Fact]
    public void BevelRoundEdge()
    {
        GeometryData mesh = Box(2f);
        Edge e = BoxEdge(mesh, new(2, 0, 0), new(2, 2, 0));
        GeometryOperators.BevelEdges(mesh, [e], 0.6f, 4);
        CheckClosed(mesh);
        float w = 0.6f;
        bool Inside(Float3 p)
        {
            float a = 2f - p.X, b = p.Z;
            if (a < w && b < w) return (a - w) * (a - w) + (b - w) * (b - w) <= w * w;
            return true;
        }
        SameSolid(mesh, Inside, 2f, 0.01f);
    }

    [Fact]
    public void BevelAllEdges()
    {
        GeometryData mesh = Box(2f);
        GeometryOperators.BevelEdges(mesh, mesh.Edges.Where(e => !e.IsHidden).ToList(), 0.3f);
        Check(mesh.Faces.Count == 26, $"faces {mesh.Faces.Count}");
        CheckClosed(mesh);
        SameSolid(mesh, p => ChamferInside(p, 0.3f, 2f), 2f, 0.005f);
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
    }

    [Fact]
    public void BevelAllRounded()
    {
        GeometryData mesh = Box(2f);
        GeometryOperators.BevelEdges(mesh, mesh.Edges.Where(e => !e.IsHidden).ToList(), 0.4f, 3);
        CheckClosed(mesh);
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
        float volume = Volume(mesh);
        Check(volume > 6.5f && volume < 7.9f, $"volume {volume}");
    }

    [Fact]
    public void BevelCornerPair()
    {
        GeometryData mesh = Box(2f);
        Edge a = BoxEdge(mesh, new(2, 0, 0), new(2, 2, 0)), b = BoxEdge(mesh, new(2, 0, 0), new(0, 0, 0));
        GeometryOperators.BevelEdges(mesh, [a, b], 0.4f);
        CheckClosed(mesh);
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
    }

    [Fact]
    public void BevelCornerTriple()
    {
        GeometryData mesh = Box(2f);
        Float3 c = new(2, 0, 0);
        var edges = mesh.Edges.Where(e => Float3.Length(e.Vert1.Point - c) < 1e-3f || Float3.Length(e.Vert2.Point - c) < 1e-3f).ToList();
        Check(edges.Count == 3, $"edges {edges.Count}");
        GeometryOperators.BevelEdges(mesh, edges, 0.4f);
        CheckClosed(mesh);
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
    }

    [Fact]
    public void BevelAroundFace()
    {
        GeometryData mesh = Box(2f);
        Face top = Top(mesh, 2f);
        GeometryOperators.BevelEdges(mesh, top.NeighborEdges(), 0.3f);
        CheckClosed(mesh);
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
        Check(mesh.Faces.Count == 6 + 4, $"faces {mesh.Faces.Count}");
        SameSolid(mesh, p => 2f - p.Y + MathF.Min(MathF.Min(p.X, 2f - p.X), MathF.Min(p.Z, 2f - p.Z)) >= 0.3f || p.Y < 1.7f, 2f, 0.01f);
    }

    [Fact]
    public void BevelLPrism()
    {
        GeometryData mesh = Pulled(1f, Poly(LPoints));
        // The outer corner of the L
        Edge e = BoxEdge(mesh, new(4, 0, 0), new(4, 1, 0));
        GeometryOperators.BevelEdges(mesh, [e], 0.5f);
        CheckClosed(mesh);
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
    }

    [Fact]
    public void BevelVertexCorner()
    {
        GeometryData mesh = Box(2f);
        Vertex corner = mesh.Vertices.First(v => Float3.Length(v.Point - new Float3(2, 0, 0)) < 1e-3f);
        GeometryOperators.BevelVertices(mesh, [corner], 0.5f, relative: false);
        Check(mesh.Faces.Count == 7, $"faces {mesh.Faces.Count}");
        CheckClosed(mesh);
        float volume = Volume(mesh);
        Check(MathF.Abs(volume - (8f - 0.5f * 0.5f * 0.5f / 6f)) < 1e-3f, $"volume {volume}");
    }

    [Fact]
    public void BevelAgainstWall()
    {
        // A box standing on a floor, bevelling a vertical edge ending on the floor
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(-4, -4, 6, 6), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 2, 2), Float3.UnitY, Down, 0);
        Face inner = mesh.Faces.First(f => MathF.Abs(Area(f) - 4f) < 1e-3f);
        GeometryOperators.PushPull(mesh, [inner], 2f, false);
        Edge e = BoxEdge(mesh, new(2, 0, 0), new(2, 2, 0));
        GeometryOperators.BevelEdges(mesh, [e], 0.4f);
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
    }
    [Fact]
    public void CollapseBoxEdge()
    {
        GeometryData mesh = Box(2f);
        Edge vertical = mesh.Edges.First(e => MathF.Abs(e.Vert1.Point.X) < 1e-3f && MathF.Abs(e.Vert1.Point.Z) < 1e-3f && MathF.Abs(e.Vert2.Point.X) < 1e-3f && MathF.Abs(e.Vert2.Point.Z) < 1e-3f);
        int joined = GeometryOperators.CollapseEdges(mesh, [vertical]);
        Check(joined == 1, $"joined {joined}");
        Check(mesh.Vertices.Count == 7, $"verts {mesh.Vertices.Count}");
        CheckClosed(mesh);
    }

    [Fact]
    public void MergeCorners()
    {
        GeometryData mesh = Box(2f);
        var pair = mesh.Vertices.Where(v => MathF.Abs(v.Point.Z) < 1e-3f && MathF.Abs(v.Point.Y) < 1e-3f).ToList();
        GeometryOperators.MergeVertices(mesh, pair, GeometryData.Centroid(pair));
        Check(mesh.Vertices.Count == 7, $"verts {mesh.Vertices.Count}");
        CheckClosed(mesh);
    }

    [Fact]
    public void WeldClose()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 2, 2), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, Rect(2.001f, 0, 4, 2), Float3.UnitY, Down, 0);
        Check(mesh.Faces.Count == 2, $"faces {mesh.Faces.Count}");
        int joined = GeometryOperators.MergeByDistance(mesh, null, 0.01f);
        Check(joined == 2, $"joined {joined}");
        Check(mesh.Vertices.Count == 6, $"verts {mesh.Vertices.Count}");
    }

    [Fact]
    public void SubdivideBoxEdge()
    {
        GeometryData mesh = Box(2f);
        Edge top = mesh.Edges.First(e => MathF.Abs(e.Vert1.Point.Y - 2f) < 1e-3f && MathF.Abs(e.Vert2.Point.Y - 2f) < 1e-3f);
        var created = GeometryOperators.SubdivideEdges(mesh, [top], 3);
        Check(created.Count == 3, $"created {created.Count}");
        Check(mesh.Vertices.Count == 11, $"verts {mesh.Vertices.Count}");
        CheckClosed(mesh);
        Check(MathF.Abs(Volume(mesh) - 8f) < 1e-3f, $"volume {Volume(mesh)}");
    }

    [Fact]
    public void ConnectTwo()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 2, 2), Float3.UnitY, Down, 0);
        var diagonal = mesh.Vertices.Where(v => (v.Point.X < 0.1f && v.Point.Z < 0.1f) || (v.Point.X > 1.9f && v.Point.Z > 1.9f)).ToList();
        int drawn = GeometryOperators.ConnectVertices(mesh, diagonal, 0, Down);
        Check(drawn == 1, $"drawn {drawn}");
        Check(mesh.Faces.Count == 2, $"faces {mesh.Faces.Count}");
    }

    [Fact]
    public void FlipOne()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 2, 2), Float3.UnitY, Down, 0);
        GeometryOperators.FlipFaces(mesh, mesh.Faces.ToList());
        Check(mesh.Faces[0].Normal().Y < -0.99f, "not flipped");
    }

    [Fact]
    public void ExtrudeOneEdge()
    {
        var mesh = new GeometryData();
        Float3 a = new(0, 0, 0), b = new(2, 0, 0);
        mesh.AddEdge(mesh.AddVertex(a), mesh.AddVertex(b));
        GeometryOperators.ExtrudeEdges(mesh, mesh.Edges.ToList(), new Float3(0, 3, 0), 0);
        Check(mesh.Faces.Count == 1, $"faces {mesh.Faces.Count}");
        Check(MathF.Abs(Area(mesh.Faces[0]) - 6f) < 1e-3f, "area");
    }

    [Fact]
    public void MirrorBox()
    {
        GeometryData mesh = Box(1f);
        GeometryOperators.Mirror(mesh, mesh.Faces.ToList(), [], Plane.FromNormalAndPoint(Float3.UnitX, new Float3(3, 0, 0)));
        Check(mesh.Faces.Count == 12, $"faces {mesh.Faces.Count}");
        float volume = Volume(mesh);
        Check(MathF.Abs(volume - 8f) < 1e-3f, $"volume {volume}");
    }
    private static float Volume(GeometryData mesh)
    {
        float volume = 0f;
        foreach (Face f in mesh.Faces)
        {
            List<Float3> p = f.NeighborVertices().Select(v => v.Point).ToList();
            var frame = new PlaneFrame(f.Normal(), p[0]);
            List<int> tris = Polygon2D.Triangulate(p.Select(frame.To2D).ToList());
            for (int t = 0; t < tris.Count; t += 3)
                volume += Float3.Dot(p[tris[t]], Float3.Cross(p[tris[t + 1]], p[tris[t + 2]])) / 6f;
        }
        return volume;
    }

    private static float PolygonArea(IReadOnlyList<Float3> points)
    {
        var frame = new PlaneFrame(Float3.UnitY, points[0]);
        return MathF.Abs(Polygon2D.SignedArea(points.Select(frame.To2D).ToList()));
    }

    private static void CheckSolid(GeometryData mesh, float expectedVolume)
    {
        CheckClosed(mesh);
        float volume = Volume(mesh);
        Check(MathF.Abs(volume - expectedVolume) < 1e-3f * MathF.Max(1f, expectedVolume), $"volume {volume}, expected {expectedVolume}");
    }

    private static GeometryData Pulled(float height, params List<(Float3, Float3)>[] loops)
    {
        var mesh = new GeometryData();
        foreach (var loop in loops) GeometryOperators.DrawSegments(mesh, loop, Float3.UnitY, Down, 0);
        Face outer = mesh.Faces.OrderByDescending(Area).First();
        List<Face> ring = outer.LogicalFace();
        GeometryOperators.DeleteFaces(mesh, mesh.Faces.Where(f => !ring.Contains(f)).ToList());
        GeometryOperators.PushPull(mesh, ring, height, false);
        return mesh;
    }

    private static List<(Float3, Float3)> Poly(params (float X, float Z)[] pts) => Loop(pts.Select(p => new Float3(p.X, 0f, p.Z)).ToArray());

    private static readonly (float, float)[] LPoints = [(0, 0), (4, 0), (4, 2), (2, 2), (2, 4), (0, 4)];

    [Fact]
    public void LShape()
    {
        GeometryData mesh = Pulled(2f, Poly(LPoints));
        Check(mesh.Faces.Count == 8, $"faces {mesh.Faces.Count}");
        CheckSolid(mesh, 12f * 2f);
    }

    [Fact]
    public void UShape()
    {
        GeometryData mesh = Pulled(1.5f, Poly((0, 0), (6, 0), (6, 4), (4, 4), (4, 2), (2, 2), (2, 4), (0, 4)));
        CheckSolid(mesh, (24f - 4f) * 1.5f);
    }

    [Fact]
    public void Star()
    {
        var pts = new List<(float, float)>();
        for (int i = 0; i < 10; i++)
        {
            float r = i % 2 == 0 ? 4f : 1.6f, a = i * MathF.PI / 5f;
            pts.Add((r * MathF.Cos(a), r * MathF.Sin(a)));
        }
        var loop = Poly(pts.ToArray());
        float area = PolygonArea(loop.Select(s => s.Item1).ToList());
        GeometryData mesh = Pulled(1f, loop);
        CheckSolid(mesh, area);
    }

    [Fact]
    public void TwoHoles()
    {
        GeometryData mesh = Pulled(1f, Rect(0, 0, 10, 6), Rect(1, 1, 3, 5), Rect(5, 2, 9, 4));
        CheckSolid(mesh, (60f - 8f - 8f) * 1f);
    }

    [Fact]
    public void ConcaveHole()
    {
        var hole = Poly((2, 2), (6, 2), (6, 6), (4, 6), (4, 4), (2, 4));
        GeometryData mesh = Pulled(1f, Rect(0, 0, 8, 8), hole);
        CheckSolid(mesh, (64f - 12f) * 1f);
    }

    [Fact]
    public void ConcavePocket()
    {
        GeometryData mesh = Box(2f);
        var l = Poly(LPoints).Select(s => (new Float3(s.Item1.X * 0.4f + 0.2f, 2f, s.Item1.Z * 0.4f + 0.2f), new Float3(s.Item2.X * 0.4f + 0.2f, 2f, s.Item2.Z * 0.4f + 0.2f))).ToList();
        GeometryOperators.DrawSegments(mesh, l, Float3.UnitY, Down, 0);
        Face inner = mesh.Faces.First(f => f.Normal().Y > 0.9f && MathF.Abs(Area(f) - 1.92f) < 0.01f && f.LogicalFace().Count == 1);
        float innerArea = Area(inner);
        GeometryOperators.PushPull(mesh, inner.LogicalFace(), -1f, false);
        CheckSolid(mesh, 8f - innerArea);
    }

    [Fact]
    public void ConcaveBoss()
    {
        GeometryData mesh = Box(2f);
        var l = Poly(LPoints).Select(s => (new Float3(s.Item1.X * 0.4f + 0.2f, 2f, s.Item1.Z * 0.4f + 0.2f), new Float3(s.Item2.X * 0.4f + 0.2f, 2f, s.Item2.Z * 0.4f + 0.2f))).ToList();
        GeometryOperators.DrawSegments(mesh, l, Float3.UnitY, Down, 0);
        Face inner = mesh.Faces.First(f => f.Normal().Y > 0.9f && MathF.Abs(Area(f) - 1.92f) < 0.01f && f.LogicalFace().Count == 1);
        float innerArea = Area(inner);
        GeometryOperators.PushPull(mesh, inner.LogicalFace(), 1f, false);
        CheckSolid(mesh, 8f + innerArea);
    }

    [Fact]
    public void Spiral()
    {
        var pts = new (float, float)[] { (0, 0), (6, 0), (6, 6), (1, 6), (1, 2), (4, 2), (4, 4), (3, 4), (3, 3), (2, 3), (2, 5), (5, 5), (5, 1), (0, 1) };
        var loop = Poly(pts);
        float area = PolygonArea(loop.Select(s => s.Item1).ToList());
        GeometryData mesh = Pulled(1f, loop);
        CheckSolid(mesh, area);
    }

    [Fact]
    public void Comb()
    {
        var pts = new List<(float, float)> { (0, 0), (11, 0), (11, 3) };
        for (int i = 4; i >= 0; i--) { pts.Add((i * 2 + 2f, 3)); pts.Add((i * 2 + 2f, 1.2f)); pts.Add((i * 2 + 1f, 1.2f)); pts.Add((i * 2 + 1f, 3)); }
        pts.Add((0, 3));
        var loop = Poly(pts.ToArray());
        float area = PolygonArea(loop.Select(s => s.Item1).ToList());
        GeometryData mesh = Pulled(1f, loop);
        CheckSolid(mesh, area);
    }

    [Fact]
    public void ConcavePit()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(-5, -5, 10, 10), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, Poly(LPoints), Float3.UnitY, Down, 0);
        Face inner = mesh.Faces.First(f => f.VertCount == 6);
        GeometryOperators.PushPull(mesh, inner.LogicalFace(), -1.5f, false);
        float up = mesh.Faces.Where(f => f.Normal().Y > 0.9f).Sum(Area);
        Check(MathF.Abs(up - 225f) < 1e-2f, $"upward area {up}");
        int walls = mesh.Faces.Count(f => MathF.Abs(f.Normal().Y) < 0.1f);
        Check(walls == 6, $"walls {walls}");
    }

    [Fact]
    public void ConcaveTwice()
    {
        GeometryData mesh = Pulled(1f, Poly(LPoints));
        Face top = mesh.Faces.First(f => f.Normal().Y > 0.9f && MathF.Abs(f.Loop!.Vert.Point.Y - 1f) < 1e-3f);
        GeometryOperators.PushPull(mesh, top.LogicalFace(), 1f, false);
        CheckSolid(mesh, 24f);
        top = mesh.Faces.First(f => f.Normal().Y > 0.9f && MathF.Abs(f.Loop!.Vert.Point.Y - 2f) < 1e-3f);
        GeometryOperators.PushPull(mesh, top.LogicalFace(), -1.5f, false);
        CheckSolid(mesh, 6f);
    }

    [Fact]
    public void Pinch()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 2, 2), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, Rect(2, 0, 4, 2), Float3.UnitY, Down, 0);
        Check(mesh.Faces.Count == 2, $"faces {mesh.Faces.Count}");
        GeometryOperators.PushPull(mesh, mesh.Faces.ToList(), 1f, false);
        CheckSolid(mesh, 8f);
    }

    private static void Check(bool condition, string message) => Assert.True(condition, message);

    private static List<(Float3, Float3)> Loop(params Float3[] points)
    {
        var segments = new List<(Float3, Float3)>();
        for (int i = 0; i < points.Length; i++) segments.Add((points[i], points[(i + 1) % points.Length]));
        return segments;
    }

    private static List<(Float3, Float3)> Rect(float x0, float z0, float x1, float z1, float y = 0f)
        => Loop(new(x0, y, z0), new(x1, y, z0), new(x1, y, z1), new(x0, y, z1));

    private static readonly Float3 Down = new(0f, -1f, 0f);

    private static float Area(Face face)
    {
        List<Float3> p = face.NeighborVertices().Select(v => v.Point).ToList();
        Float3 sum = Float3.Zero;
        for (int i = 0; i < p.Count; i++) sum += Float3.Cross(p[i], p[(i + 1) % p.Count]);
        return Float3.Length(sum) * 0.5f;
    }

    private static int Visible(GeometryData mesh) => mesh.Edges.Count(e => !e.IsHidden);

    private static void CheckClosed(GeometryData mesh)
    {
        foreach (Edge e in mesh.Edges)
            Check(e.FaceCount == 2, $"edge {e.Vert1.Point} to {e.Vert2.Point} has {e.FaceCount} faces");

        // Every face points away from the middle of the solid
        Float3 center = Float3.Zero;
        foreach (var v in mesh.Vertices) center += v.Point;
        center /= mesh.Vertices.Count;
        float volume = 0f;
        foreach (Face f in mesh.Faces)
        {
            List<Float3> p = f.NeighborVertices().Select(v => v.Point - center).ToList();
            for (int i = 1; i + 1 < p.Count; i++) volume += Float3.Dot(p[0], Float3.Cross(p[i], p[i + 1])) / 6f;
        }
        Check(volume > 0f, $"solid is inside out, volume {volume}");
    }

    private static GeometryData Box(float height = 1f)
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 2, 2), Float3.UnitY, Down, 0);
        GeometryOperators.PushPull(mesh, mesh.Faces.ToList(), height, false);
        return mesh;
    }

    private static Face Top(GeometryData mesh, float y) => mesh.Faces.First(f => f.Normal().Y > 0.9f && MathF.Abs(f.Center().Y - y) < 1e-3f);

    [Fact]
    public void RectangleMakesFace()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 2, 1), Float3.UnitY, Down, 0);
        Check(mesh.Faces.Count == 1, $"faces {mesh.Faces.Count}");
        Check(mesh.Faces[0].Normal().Y > 0.99f, "face points down");
        Check(MathF.Abs(Area(mesh.Faces[0]) - 2f) < 1e-4f, "area");
    }

    private static GeometryData WithHole()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 4, 4), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, Rect(1, 1, 2, 2), Float3.UnitY, Down, 0);
        return mesh;
    }

    [Fact]
    public void InnerRectangle()
    {
        GeometryData mesh = WithHole();
        Check(Visible(mesh) == 8, $"visible edges {Visible(mesh)}");
        float total = mesh.Faces.Sum(Area);
        Check(MathF.Abs(total - 16f) < 1e-3f, $"total area {total}");
        Face inner = mesh.Faces.First(f => MathF.Abs(Area(f) - 1f) < 1e-3f);
        Check(inner.LogicalFace().Count == 1, "inner face joined to outer");
        Face outer = mesh.Faces.First(f => f != inner);
        Check(MathF.Abs(outer.LogicalFace().Sum(Area) - 15f) < 1e-3f, "outer area");
        Check(mesh.Faces.All(f => f.Normal().Y > 0.99f), "faces point down");
    }

    [Fact]
    public void DeleteInner()
    {
        GeometryData mesh = WithHole();
        Face inner = mesh.Faces.First(f => MathF.Abs(Area(f) - 1f) < 1e-3f);
        GeometryOperators.DeleteFaces(mesh, [inner]);
        float total = mesh.Faces.Sum(Area);
        Check(MathF.Abs(total - 15f) < 1e-3f, $"total area {total}");
        Check(Visible(mesh) == 8, $"visible edges {Visible(mesh)}");

        // Drawing elsewhere must not bring the deleted face back
        GeometryOperators.DrawSegments(mesh, [(new Float3(3, 0, 0), new Float3(3, 0, 4))], Float3.UnitY, Down, 0);
        total = mesh.Faces.Sum(Area);
        Check(MathF.Abs(total - 15f) < 1e-3f, $"hole came back, total area {total}");
    }

    [Fact]
    public void PullBox()
    {
        GeometryData mesh = Box();
        Check(mesh.Faces.Count == 6, $"faces {mesh.Faces.Count}");
        Check(mesh.Vertices.Count == 8, $"verts {mesh.Vertices.Count}");
        CheckClosed(mesh);
    }

    [Fact]
    public void PullTop()
    {
        GeometryData mesh = Box();
        GeometryOperators.PushPull(mesh, [Top(mesh, 1f)], 1f, false);
        Check(mesh.Faces.Count == 6, $"faces {mesh.Faces.Count}");
        Check(mesh.Vertices.Count == 8, $"verts {mesh.Vertices.Count}");
        Check(mesh.Vertices.Max(v => v.Point.Y) > 1.99f, "top did not move");
        CheckClosed(mesh);
    }

    [Fact]
    public void PushTopDown()
    {
        GeometryData mesh = Box();
        GeometryOperators.PushPull(mesh, [Top(mesh, 1f)], -0.5f, false);
        Check(mesh.Faces.Count == 6, $"faces {mesh.Faces.Count}");
        Check(mesh.Vertices.Count == 8, $"verts {mesh.Vertices.Count}");
        Check(MathF.Abs(mesh.Vertices.Max(v => v.Point.Y) - 0.5f) < 1e-4f, "top in wrong place");
        CheckClosed(mesh);
    }

    [Fact]
    public void PushThrough()
    {
        GeometryData mesh = Box();
        GeometryOperators.DrawSegments(mesh, Rect(0.5f, 0.5f, 1.5f, 1.5f, 1f), Float3.UnitY, Down, 0);
        Face inner = mesh.Faces.First(f => f.Normal().Y > 0.9f && MathF.Abs(Area(f) - 1f) < 1e-3f);
        GeometryOperators.PushPull(mesh, [inner], -1f, false);
        CheckClosed(mesh);
        float topArea = mesh.Faces.Where(f => f.Normal().Y > 0.9f).Sum(Area);
        float bottomArea = mesh.Faces.Where(f => f.Normal().Y < -0.9f).Sum(Area);
        Check(MathF.Abs(topArea - 3f) < 1e-3f, $"top area {topArea}");
        Check(MathF.Abs(bottomArea - 3f) < 1e-3f, $"bottom area {bottomArea}");
        Check(Visible(mesh) == 24, $"visible edges {Visible(mesh)}");
    }

    private static GeometryData Split()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 2, 2), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, [(new Float3(1, 0, 0), new Float3(1, 0, 2))], null, Down, 0);
        return mesh;
    }

    [Fact]
    public void LineSplits()
    {
        GeometryData mesh = Split();
        Check(mesh.Faces.Count == 2, $"faces {mesh.Faces.Count}");
        Check(mesh.Faces.All(f => MathF.Abs(Area(f) - 2f) < 1e-3f), "areas");
    }

    [Fact]
    public void DeleteLineMerges()
    {
        GeometryData mesh = Split();
        Edge line = mesh.Edges.First(e => MathF.Abs(e.Vert1.Point.X - 1f) < 1e-4f && MathF.Abs(e.Vert2.Point.X - 1f) < 1e-4f);
        GeometryOperators.DeleteEdges(mesh, [line]);
        Check(mesh.Faces.Count == 1, $"faces {mesh.Faces.Count}");
        Check(mesh.Vertices.Count == 4, $"verts {mesh.Vertices.Count}");
        Check(MathF.Abs(Area(mesh.Faces[0]) - 4f) < 1e-3f, "area");
    }

    [Fact]
    public void Pit()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 4, 4), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, Rect(1, 1, 2, 2), Float3.UnitY, Down, 0);
        Face inner = mesh.Faces.First(f => MathF.Abs(Area(f) - 1f) < 1e-3f);
        GeometryOperators.PushPull(mesh, [inner], -1f, false);
        Check(mesh.Faces.Where(f => f.Normal().Y > 0.9f).Sum(Area) is var up && MathF.Abs(up - 16f) < 1e-3f, "floor plus pit bottom area");
        int walls = mesh.Faces.Count(f => MathF.Abs(f.Normal().Y) < 0.1f);
        Check(walls == 4, $"walls {walls}");
        foreach (Face wall in mesh.Faces.Where(f => MathF.Abs(f.Normal().Y) < 0.1f))
        {
            Float3 toCenter = new Float3(1.5f, wall.Center().Y, 1.5f) - wall.Center();
            Check(Float3.Dot(wall.Normal(), toCenter) > 0f, "pit wall faces away from the pit");
        }
    }

    [Fact]
    public void MoveFolds()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 2, 2), Float3.UnitY, Down, 0);
        var corner = mesh.Vertices.First(v => v.Point.X > 1.9f && v.Point.Z > 1.9f);
        GeometryOperators.MoveVertices(mesh, [corner], new Float3(0, 1, 0));
        Check(mesh.Faces.Count == 2, $"faces {mesh.Faces.Count}");
        Check(mesh.Edges.Count(e => e.IsHidden) == 1, "fold edge not hidden");
        GeometryOperators.MoveVertices(mesh, [corner], new Float3(0, -1, 0));
        Check(mesh.Faces.Count == 1, $"flattened back, faces {mesh.Faces.Count}");
    }

    [Fact]
    public void Snapshot()
    {
        GeometryData mesh = WithHole();
        GeometrySnapshot snap = mesh.Capture();
        var copy = new GeometryData();
        copy.Restore(snap);
        Check(copy.Faces.Count == mesh.Faces.Count, "faces");
        Check(copy.Edges.Count == mesh.Edges.Count, "edges");
        Check(copy.Edges.Count(e => e.IsHidden) == mesh.Edges.Count(e => e.IsHidden), "hidden");
    }

    [Fact]
    public void Pocket()
    {
        GeometryData mesh = Box();
        GeometryOperators.DrawSegments(mesh, Rect(0.5f, 0.5f, 1.5f, 1.5f, 1f), Float3.UnitY, Down, 0);
        Face inner = mesh.Faces.First(f => f.Normal().Y > 0.9f && MathF.Abs(Area(f) - 1f) < 1e-3f);
        GeometryOperators.PushPull(mesh, [inner], -0.5f, false);
        CheckClosed(mesh);
        Check(mesh.Faces.Count(f => f.Normal().Y > 0.9f && MathF.Abs(f.Center().Y - 0.5f) < 1e-3f) == 1, "pocket floor");
    }

    [Fact]
    public void SideBoss()
    {
        GeometryData mesh = Box(2f);
        var side = Loop(new(2, 0.5f, 0.5f), new(2, 0.5f, 1.5f), new(2, 1.5f, 1.5f), new(2, 1.5f, 0.5f));
        GeometryOperators.DrawSegments(mesh, side, Float3.UnitX, -Float3.UnitX, 0);
        Face boss = mesh.Faces.First(f => f.Normal().X > 0.9f && MathF.Abs(Area(f) - 1f) < 1e-3f);
        GeometryOperators.PushPull(mesh, [boss], 1f, false);
        CheckClosed(mesh);
        Check(mesh.Vertices.Max(v => v.Point.X) > 2.99f, "boss did not come out");
    }

    [Fact]
    public void EdgeRectangle()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 4, 4), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 1, 1), Float3.UnitY, Down, 0);
        Check(mesh.Faces.Count == 2, $"faces {mesh.Faces.Count}");
        Face corner = mesh.Faces.First(f => MathF.Abs(Area(f) - 1f) < 1e-3f);
        GeometryOperators.PushPull(mesh, [corner], 1f, false);
        float floor = mesh.Faces.Where(f => f.Normal().Y > 0.9f && MathF.Abs(f.Center().Y) < 1e-3f).Sum(Area);
        Check(MathF.Abs(floor - 15f) < 1e-3f, $"floor area {floor}");
        Check(mesh.Faces.Count(f => MathF.Abs(f.Normal().Y) < 0.1f) == 4, "walls");
    }

    [Fact]
    public void PullTwice()
    {
        GeometryData mesh = Box();
        GeometryOperators.PushPull(mesh, [Top(mesh, 1f)], 1f, true);
        Check(mesh.Faces.Count == 11, $"faces {mesh.Faces.Count}");
        GeometryOperators.PushPull(mesh, [Top(mesh, 2f)], 0.5f, false);
        Check(mesh.Vertices.Max(v => v.Point.Y) > 2.49f, "second pull");
    }

    [Fact]
    public void CrossingRectangles()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 2, 2), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, Rect(1, 1, 3, 3), Float3.UnitY, Down, 0);
        Check(mesh.Faces.Count == 3, $"faces {mesh.Faces.Count}");
        float total = mesh.Faces.Sum(Area);
        Check(MathF.Abs(total - 7f) < 1e-3f, $"area {total}");
    }

    [Fact]
    public void TriangulateConcavePolygonCoversItsArea()
    {
        Float2[] l = [new(0, 0), new(4, 0), new(4, 1), new(1, 1), new(1, 4), new(0, 4)];
        List<int> tris = Polygon2D.Triangulate(l);
        float area = 0f;
        for (int i = 0; i < tris.Count; i += 3) area += MathF.Abs(Polygon2D.Cross(l[tris[i]], l[tris[i + 1]], l[tris[i + 2]])) * 0.5f;
        Assert.Equal(7f, area, 3);
    }

    [Fact]
    public void PlaneFrameRoundTripsPoints()
    {
        var frame = new PlaneFrame(Float3.Normalize(new Float3(1, 2, 3)), new Float3(1, 1, 1));
        Float3 on = frame.To3D(new Float2(2f, -3f));
        Assert.Equal(0f, frame.Distance(on), 4);
        Float2 back = frame.To2D(on);
        Assert.Equal(2f, back.X, 4);
        Assert.Equal(-3f, back.Y, 4);
    }

    private static GeometryData HoledFloor()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 4, 4), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, Rect(1, 1, 3, 3), Float3.UnitY, Down, 0);
        GeometryOperators.DeleteFaces(mesh, [mesh.Faces.First(f => MathF.Abs(f.Center().X - 2f) < 1e-3f && MathF.Abs(f.Center().Z - 2f) < 1e-3f && Area(f) < 4.01f)]);
        return mesh;
    }

    [Fact]
    public void ExtrudingOnePieceOfAHoledFaceMovesTheWholeFace()
    {
        GeometryData mesh = HoledFloor();
        Check(mesh.Faces.Count > 1 && mesh.Edges.Any(e => e.IsHidden), "the floor should be pieces joined by hidden edges");
        float area = mesh.SurfaceArea();

        GeometryOperators.ExtrudeFaces(mesh, [mesh.Faces[0]], 1f, ExtrudeMode.PerFace);

        var top = mesh.Faces.Where(f => f.Normal().Y > 0.9f).ToList();
        Check(top.All(f => MathF.Abs(f.Center().Y - 1f) < 1e-4f), "every piece of the floor should have moved up");
        Check(MathF.Abs(top.Sum(Area) - area) < 1e-3f, "the moved floor should keep its area");
        Check(mesh.Faces.Count(f => MathF.Abs(f.Normal().Y) < 0.1f) == 8, "there should be a wall on each outline edge and nothing between the pieces");
        Check(mesh.Edges.All(e => e.Loop != null), "nothing should be left where the floor was");
        Check(top[0].LogicalFace().Count == top.Count, "the moved pieces should read as one face");
    }

    [Fact]
    public void InsettingAHoledFaceByDistanceKeepsItOneFace()
    {
        GeometryData mesh = HoledFloor();
        GeometryOperators.InsetFaces(mesh, [mesh.Faces[0]], 0.25f, InsetMode.PerFace, InsetMeasure.Distance);

        Check(mesh.Edges.All(e => e.Loop != null), "nothing should be left loose");
        var walls = mesh.Faces.Where(f => !f.NeighborEdges().Any(e => e.IsHidden)).ToList();
        Check(walls.Count == 8, $"there should be a border strip along each of the 8 outline edges, found {walls.Count}");
        float inner = mesh.Faces.Except(walls).Sum(Area);
        Check(MathF.Abs(inner - (3.5f * 3.5f - 2.5f * 2.5f)) < 1e-3f, $"inner area {inner}");
    }

    private static void CheckUnchanged(GeometryData mesh, List<Face> faces, List<Vertex> verts, List<Float3> points)
    {
        Check(mesh.Faces.SequenceEqual(faces), "the faces should be the same objects as before");
        Check(mesh.Vertices.SequenceEqual(verts), "the vertices should be the same objects as before");
        Check(mesh.Vertices.Select(v => v.Point).SequenceEqual(points), "no vertex should have moved");
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
    }

    [Fact]
    public void BevelVertexAcrossALineOnTheFaceIsRefusedAndChangesNothing()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 4, 4), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, [(new Float3(0.3f, 0, 0.3f), new Float3(1.2f, 0, 1.2f))], Float3.UnitY, Down, 0);
        Check(mesh.Faces.Count == 1 && mesh.Edges.Count(e => e.Loop == null) == 1, "the line should lie loose on the face");
        var faces = mesh.Faces.ToList();
        var verts = mesh.Vertices.ToList();
        var points = verts.Select(v => v.Point).ToList();

        Vertex corner = mesh.Vertices.First(v => Float3.Length(v.Point) < 1e-4f);
        Assert.Throws<InvalidOperationException>(() => GeometryOperators.BevelVertices(mesh, [corner], 1.5f, relative: false));
        CheckUnchanged(mesh, faces, verts, points);
    }

    [Fact]
    public void BevelVertexClearOfAHoleStillWorks()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Rect(0, 0, 4, 4), Float3.UnitY, Down, 0);
        GeometryOperators.DrawSegments(mesh, Rect(2, 2, 3, 3), Float3.UnitY, Down, 0);
        GeometryOperators.DeleteFaces(mesh, [mesh.Faces.First(f => MathF.Abs(Area(f) - 1f) < 1e-3f)]);
        float area = mesh.SurfaceArea();

        Vertex corner = mesh.Vertices.First(v => Float3.Length(v.Point) < 1e-4f);
        GeometryOperators.BevelVertices(mesh, [corner], 1f, relative: false);
        Check(MathF.Abs(mesh.SurfaceArea() - (area - 0.5f)) < 1e-3f, $"area {mesh.SurfaceArea()}");
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
    }

    [Fact]
    public void BevelVertexOnAConcaveCornerKeepsTheFaceSimple()
    {
        var mesh = new GeometryData();
        GeometryOperators.DrawSegments(mesh, Loop(new(0, 0, 0), new(4, 0, 0), new(4, 0, 1), new(1, 0, 1), new(1, 0, 4), new(0, 0, 4)), Float3.UnitY, Down, 0);
        Vertex reflex = mesh.Vertices.First(v => Float3.Length(v.Point - new Float3(1, 0, 1)) < 1e-4f);
        GeometryOperators.BevelVertices(mesh, [reflex], 0.5f, relative: false);
        Check(mesh.FindDefect() == null, mesh.FindDefect() ?? "");
        Check(MathF.Abs(mesh.SurfaceArea() - (7f + 0.125f)) < 1e-3f, $"area {mesh.SurfaceArea()}");
    }

    [Fact]
    public void BevelVertexTooWideIsRefusedAndChangesNothing()
    {
        GeometryData mesh = Box(2f);
        var faces = mesh.Faces.ToList();
        var verts = mesh.Vertices.ToList();
        var points = verts.Select(v => v.Point).ToList();
        Vertex corner = mesh.Vertices.First(v => Float3.Length(v.Point - new Float3(2, 0, 0)) < 1e-3f);
        Assert.Throws<InvalidOperationException>(() => GeometryOperators.BevelVertices(mesh, [corner], 2.5f, relative: false));
        CheckUnchanged(mesh, faces, verts, points);
    }

    [Fact]
    public void BevelEdgeTooWideIsRefusedAndChangesNothing()
    {
        GeometryData mesh = Box(2f);
        var faces = mesh.Faces.ToList();
        var verts = mesh.Vertices.ToList();
        var points = verts.Select(v => v.Point).ToList();
        Edge edge = mesh.Edges.First(e => e.FaceCount == 2 && e.Vert1.Point.Y < 1e-3f && e.Vert2.Point.Y < 1e-3f);
        Assert.Throws<InvalidOperationException>(() => GeometryOperators.BevelEdges(mesh, [edge], 2.5f));
        CheckUnchanged(mesh, faces, verts, points);
    }

    [Fact]
    public void BevelEdgeIntoAShapeDrawnOnTheFaceIsRefusedAndChangesNothing()
    {
        GeometryData mesh = Box(2f);
        GeometryOperators.DrawSegments(mesh, Rect(0.4f, 0.4f, 0.8f, 0.8f, 2f), Float3.UnitY, Down, 0);
        var faces = mesh.Faces.ToList();
        var verts = mesh.Vertices.ToList();
        var points = verts.Select(v => v.Point).ToList();
        Edge edge = mesh.Edges.First(e => e.FaceCount == 2 && MathF.Abs(e.Vert1.Point.Y - 2f) < 1e-3f && MathF.Abs(e.Vert2.Point.Y - 2f) < 1e-3f
            && MathF.Abs(e.Vert1.Point.X) < 1e-3f && MathF.Abs(e.Vert2.Point.X) < 1e-3f);
        Assert.Throws<InvalidOperationException>(() => GeometryOperators.BevelEdges(mesh, [edge], 0.6f));
        CheckUnchanged(mesh, faces, verts, points);
    }
}
