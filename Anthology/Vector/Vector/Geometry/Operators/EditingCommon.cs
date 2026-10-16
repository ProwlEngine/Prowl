// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Edge = Prowl.Vector.Geometry.GeometryData.Edge;
using Face = Prowl.Vector.Geometry.GeometryData.Face;
using Vertex = Prowl.Vector.Geometry.GeometryData.Vertex;

namespace Prowl.Vector.Geometry.Operators;

/// <summary>Pieces shared by the modelling operators: finding the planes an edit touched and rebuilding them.</summary>
internal static class EditingCommon
{
    internal const float Eps = GeometryData.Epsilon;

    internal readonly record struct PlaneKey(Float3 Normal, Float3 Origin);

    /// <summary>Horizontal faces drawn in the air face up, others face the camera.</summary>
    internal static Float3 OrientNew(Float3 normal, Float3 viewDirection)
    {
        if (MathF.Abs(normal.Y) > 0.999f) return normal.Y > 0f ? normal : -normal;
        return Float3.Dot(normal, viewDirection) > 0f ? -normal : normal;
    }

    internal static void AddPlane(List<PlaneKey> planes, Float3 normal, Float3 origin)
    {
        normal = Float3.Normalize(normal);
        foreach (PlaneKey p in planes)
            if (MathF.Abs(Float3.Dot(p.Normal, normal)) > 1f - 1e-5f && MathF.Abs(Float3.Dot(origin - p.Origin, p.Normal)) <= Eps)
                return;
        planes.Add(new PlaneKey(normal, origin));
    }

    internal static void RebuildAround(GeometryData mesh, IReadOnlyCollection<Vertex> vertices)
    {
        foreach (Face face in vertices.SelectMany(v => v.NeighborFaces()).Distinct().ToList())
            if (!face.IsPlanar()) Fold(mesh, face);

        List<Vertex> moved = mesh.MergeCoincident(vertices);

        var planes = new List<PlaneKey>();
        foreach (Face face in moved.SelectMany(v => v.NeighborFaces()).Distinct())
            if (face.IsPlanar()) AddPlane(planes, face.Normal(), face.Loop!.Vert.Point);

        foreach (PlaneKey plane in planes)
            PlaneRebuildOp.Run(mesh, plane.Normal, plane.Origin, moved.Where(v => mesh.Vertices.Contains(v)), [], 0);

        mesh.RemoveUnused();
        mesh.MarkChanged();
    }

    /// <summary>Splits a face that is no longer flat into triangles joined by hidden edges.</summary>
    internal static void Fold(GeometryData mesh, Face face)
    {
        List<Vertex> verts = face.NeighborVertices();
        Float3 normal = face.Normal();
        if (Float3.LengthSquared(normal) < 0.5f) normal = Float3.UnitY;

        var frame = new PlaneFrame(normal, verts[0].Point);
        List<int> triangles = Polygon2D.Triangulate(verts.Select(v => frame.To2D(v.Point)).ToList());
        var outline = new HashSet<(Vertex, Vertex)>();
        for (int i = 0; i < verts.Count; i++)
        {
            outline.Add((verts[i], verts[(i + 1) % verts.Count]));
            outline.Add((verts[(i + 1) % verts.Count], verts[i]));
        }

        mesh.RemoveFace(face);
        for (int t = 0; t < triangles.Count; t += 3)
        {
            Vertex a = verts[triangles[t]], b = verts[triangles[t + 1]], c = verts[triangles[t + 2]];
            mesh.AddFace([a, b, c], face);
            foreach ((Vertex x, Vertex y) in new[] { (a, b), (b, c), (c, a) })
                if (!outline.Contains((x, y)) && mesh.FindEdge(x, y) is Edge diagonal) diagonal.IsHidden = true;
        }
    }

    /// <summary>The edges laid end to end as points, with whether they close into a loop. Null when they branch or split apart.</summary>
    internal static List<Vertex>? OrderChain(IEnumerable<Edge> edges, out bool closed)
    {
        closed = false;
        var list = edges.Where(e => !e.IsHidden).Distinct().ToList();
        if (list.Count == 0) return null;

        var next = new Dictionary<Vertex, List<Edge>>();
        foreach (Edge e in list)
        {
            foreach (Vertex v in new[] { e.Vert1, e.Vert2 })
            {
                if (!next.TryGetValue(v, out List<Edge>? l)) next[v] = l = [];
                l.Add(e);
            }
        }
        if (next.Values.Any(l => l.Count > 2)) return null;

        List<Vertex> ends = next.Where(p => p.Value.Count == 1).Select(p => p.Key).ToList();
        if (ends.Count != 0 && ends.Count != 2) return null;
        closed = ends.Count == 0;

        Vertex start = closed ? list[0].Vert1 : ends[0];
        var chain = new List<Vertex> { start };
        var used = new HashSet<Edge>();
        Vertex at = start;
        while (true)
        {
            Edge? step = next[at].FirstOrDefault(e => !used.Contains(e));
            if (step == null) break;
            used.Add(step);
            at = step.OtherVertex(at);
            if (closed && at == start) break;
            chain.Add(at);
        }
        return used.Count == list.Count ? chain : null;
    }
}
