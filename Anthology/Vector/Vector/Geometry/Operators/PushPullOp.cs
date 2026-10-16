// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Edge = Prowl.Vector.Geometry.GeometryData.Edge;
using Face = Prowl.Vector.Geometry.GeometryData.Face;
using Vertex = Prowl.Vector.Geometry.GeometryData.Vertex;

using static Prowl.Vector.Geometry.Operators.EditingCommon;

namespace Prowl.Vector.Geometry.Operators;

internal static class PushPullOp
{
    /// <summary>
    /// Pushes or pulls a flat region along its normal. Walls are added around it, walls that line up with
    /// faces beside them merge into them, and faces that end up overlapping facing opposite ways cancel,
    /// so pushing through a solid cuts a hole. A region standing on its own keeps its base, making a solid.
    /// </summary>
    internal static void PushPull(GeometryData mesh, IReadOnlyCollection<Face> region, float distance, bool keepBase)
    {
        if (MathF.Abs(distance) <= Eps || region.Count == 0) return;

        var set = new HashSet<Face>(region);
        Float3 normal = Float3.Normalize(set.First().Normal());
        Float3 offset = normal * distance;

        var boundary = GeometryData.Boundary(set);
        bool standalone = boundary.All(b => b.Edge.FaceCount == 1);
        bool keep = keepBase || standalone;
        bool flipNew = standalone && distance < 0f;

        var regionFaces = set.Select(f => (Verts: f.NeighborVertices(), Look: f)).ToList();
        var regionEdges = set.SelectMany(f => f.NeighborEdges()).Distinct().ToList();
        var hiddenPairs = regionEdges.Where(e => e.IsHidden).Select(e => (e.Vert1, e.Vert2)).ToList();
        var walls = boundary.Select(b => (b.A, b.B, Look: b.Face)).ToList();

        // Where two soft outline edges meet the wall edge between them is soft too, so curves stay smooth when pushed
        var softEnds = new Dictionary<Vertex, int>();
        foreach ((Vertex a, Vertex b, _, Edge edge) in boundary.Select(x => (x.A, x.B, x.Face, x.Edge)))
        {
            if (!edge.IsSoft) continue;
            softEnds[a] = softEnds.GetValueOrDefault(a) + 1;
            softEnds[b] = softEnds.GetValueOrDefault(b) + 1;
        }

        var moved = new Dictionary<Vertex, Vertex>();
        foreach ((List<Vertex> verts, _) in regionFaces)
            foreach (Vertex v in verts)
                if (!moved.ContainsKey(v)) moved[v] = mesh.AddVertex(v.Point + offset);

        if (!keep)
        {
            foreach (Face f in set) mesh.RemoveFace(f);
        }
        else if (standalone && distance > 0f)
        {
            foreach (Face f in set)
            {
                List<Vertex> verts = f.NeighborVertices();
                verts.Reverse();
                mesh.ReplaceFace(f, verts);
            }
        }

        foreach ((List<Vertex> verts, Face look) in regionFaces)
        {
            List<Vertex> cap = verts.Select(v => moved[v]).ToList();
            if (flipNew) cap.Reverse();
            mesh.AddFace(cap, look);
        }
        foreach ((Vertex a, Vertex b) in hiddenPairs)
            if (mesh.FindEdge(moved[a], moved[b]) is Edge capEdge) capEdge.IsHidden = true;

        foreach ((Vertex a, Vertex b, Face look) in walls)
        {
            List<Vertex> wall = [a, b, moved[b], moved[a]];
            if (flipNew) wall.Reverse();
            // A wall stands across the face it grew from, so the face's texture mapping does not fit it
            if (mesh.AddFace(wall, look) is Face made) made.Mapping = null;
        }

        foreach (Edge e in regionEdges)
            if (e.Loop == null && mesh.Edges.Contains(e)) mesh.RemoveEdge(e);

        foreach ((Vertex v, int count) in softEnds)
            if (count == 2 && moved.TryGetValue(v, out Vertex? lifted) && mesh.Vertices.Contains(v) && mesh.Vertices.Contains(lifted) && mesh.FindEdge(v, lifted) is Edge post)
                post.IsSoft = true;

        List<Vertex> capVerts = mesh.MergeCoincident(moved.Values);
        if (capVerts.Count > 0)
            PlaneRebuildOp.Run(mesh, normal, capVerts[0].Point, capVerts, [], 0);

        // Walls on one line share a plane, so each plane is rebuilt once with every edge it may merge across
        var wallPlanes = new List<(PlaneKey Plane, List<Vertex> Seeds, List<(Vertex, Vertex)> Dissolve)>();
        foreach ((Vertex a, Vertex b, _) in walls)
        {
            if (!mesh.Vertices.Contains(a) || !mesh.Vertices.Contains(b)) continue;
            Float3 wallNormal = Float3.Cross(b.Point - a.Point, offset);
            if (Float3.Length(wallNormal) < 1e-9f) continue;
            wallNormal = Float3.Normalize(wallNormal);

            int found = wallPlanes.FindIndex(w => MathF.Abs(Float3.Dot(w.Plane.Normal, wallNormal)) > 1f - 1e-5f
                && MathF.Abs(Float3.Dot(a.Point - w.Plane.Origin, w.Plane.Normal)) <= Eps);
            if (found < 0)
            {
                wallPlanes.Add((new PlaneKey(wallNormal, a.Point), [], []));
                found = wallPlanes.Count - 1;
            }
            wallPlanes[found].Seeds.AddRange([a, b]);
            wallPlanes[found].Dissolve.Add((a, b));
        }

        foreach ((PlaneKey plane, List<Vertex> seeds, List<(Vertex, Vertex)> pairs) in wallPlanes)
        {
            var dissolve = pairs.Select(p => mesh.Vertices.Contains(p.Item1) && mesh.Vertices.Contains(p.Item2) ? mesh.FindEdge(p.Item1, p.Item2) : null)
                .OfType<Edge>().ToHashSet();
            PlaneRebuildOp.Run(mesh, plane.Normal, plane.Origin, seeds.Where(v => mesh.Vertices.Contains(v)), [], 0, dissolve);
        }

        foreach (Vertex v in walls.SelectMany(w => new[] { w.A, w.B }).Concat(capVerts).Distinct().ToList())
            if (mesh.Vertices.Contains(v)) mesh.DissolveVertex(v);

        mesh.RemoveUnused();
        mesh.MarkChanged();
    }
}
