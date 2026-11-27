// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry.Operators;

internal static class InsetOp
{
    private sealed class EdgeInfo
    {
        public int UsageCount;
        public bool IsBoundary => UsageCount == 1;
    }

    private sealed class VertexInfo
    {
        public List<int> Faces = [];
        public HashSet<GeometryData.Vertex> SharedEdgeNeighbors = [];
        public bool IsShared => Faces.Count > 1;
    }

    /// <summary>
    /// Shrinks faces inside their own outline and joins them to the old outline with walls. By fraction, every
    /// corner moves that part of the way to its face's centre. By distance, every border of the new outline is
    /// that far inside the old one. Faces joined by hidden edges are one face, so they are inset together, and
    /// in shared mode the whole selection is inset as one region. Faces and walls carry what the original
    /// faces did, with loop attributes sampled across the face so textures stay in place, and nothing is left
    /// behind inside a region.
    /// </summary>
    internal static void InsetFaces(GeometryData mesh, IEnumerable<GeometryData.Face> facesToInset, float thickness, InsetMode mode = InsetMode.Shared, InsetMeasure measure = InsetMeasure.Fraction)
    {
        List<GeometryData.Face> faces = GeometryData.LogicalFaces(facesToInset.Where(mesh.Contains));
        if (faces.Count == 0) return;

        thickness = measure == InsetMeasure.Fraction ? Maths.Max(0f, Maths.Min(1f, thickness)) : Maths.Max(0f, thickness);
        if (thickness < 1e-10f) return;

        if (mode == InsetMode.Shared)
        {
            InsetRegion(mesh, faces, thickness, measure);
            return;
        }

        var done = new HashSet<GeometryData.Face>();
        foreach (GeometryData.Face face in faces)
        {
            if (done.Contains(face)) continue;
            List<GeometryData.Face> group = face.LogicalFace();
            done.UnionWith(group);
            InsetRegion(mesh, group, thickness, measure);
        }
    }

    private static void InsetRegion(GeometryData mesh, List<GeometryData.Face> faces, float thickness, InsetMeasure measure)
    {
        var snapshots = faces.Select(f => FaceSnapshot.Capture(mesh, f)).ToList();
        var normals = faces.Select(f => f.Normal()).ToList();
        var centers = faces.Select(f => f.Center()).ToList();
        var oldEdges = faces.SelectMany(f => f.NeighborEdges()).Distinct().ToList();
        var edgeAttributes = oldEdges.ToDictionary(e => GeometryOperators.GetCanonicalEdge(e.Vert1, e.Vert2), e => AttributeTransfer.Copy(e.Attributes));
        var oldVertices = snapshots.SelectMany(s => s.Vertices).Distinct().ToList();
        Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo> edgeInfo = BuildEdgeInfo(snapshots);

        Dictionary<GeometryData.Vertex, Float3> targets = measure == InsetMeasure.Fraction
            ? FractionTargets(snapshots, centers, edgeInfo, thickness)
            : DistanceTargets(snapshots, normals, edgeInfo, thickness);

        var inset = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();
        foreach (GeometryData.Vertex? v in oldVertices)
        {
            GeometryData.Vertex made = mesh.AddVertex(targets[v]);
            GeometryOperators.AttributeLerp(mesh, made, v, v, 1f);
            inset[v] = made;
        }

        foreach (GeometryData.Face face in faces) mesh.RemoveFace(face);

        foreach (FaceSnapshot snapshot in snapshots)
        {
            GeometryData.Face? inner = mesh.AddFace(snapshot.Vertices.Select(v => inset[v]).ToArray());
            if (inner == null) continue;

            snapshot.ApplyFaceAttributes(inner);
            snapshot.ApplyLoopsByPosition(mesh, inner);
        }

        // The new outline keeps what the old one carried, hidden joins between the pieces included
        foreach (KeyValuePair<(GeometryData.Vertex, GeometryData.Vertex), Dictionary<string, GeometryData.AttributeValue>> pair in edgeAttributes)
            if (mesh.FindEdge(inset[pair.Key.Item1], inset[pair.Key.Item2]) is { } moved)
                AttributeTransfer.Apply(pair.Value, moved.Attributes);

        foreach (FaceSnapshot snapshot in snapshots)
        {
            List<GeometryData.Vertex> verts = snapshot.Vertices;
            for (int k = 0; k < verts.Count; k++)
            {
                GeometryData.Vertex v1 = verts[k];
                GeometryData.Vertex v2 = verts[(k + 1) % verts.Count];
                if (!edgeInfo[GeometryOperators.GetCanonicalEdge(v1, v2)].IsBoundary) continue;

                GeometryData.Vertex i1 = inset[v1];
                GeometryData.Vertex i2 = inset[v2];
                GeometryData.Face? wall = mesh.AddFace(i1, v1, v2, i2);
                if (wall == null) continue;

                snapshot.ApplyFaceAttributes(wall);
                FaceSnapshot.ApplyLoops(wall, v => v == i1 || v == i2 ? snapshot.Sample(mesh, v.Point) : snapshot.LoopOf(v));
            }
        }

        foreach (GeometryData.Edge? edge in oldEdges)
            if (edge.Loop == null && mesh.Contains(edge)) mesh.RemoveEdge(edge);
        foreach (GeometryData.Vertex? vertex in oldVertices)
            if (vertex.Edge == null && mesh.Contains(vertex)) mesh.RemoveVertex(vertex);
    }

    private static Float3 Miter(Float3 n1, Float3 n2, float distance) =>
        (n1 + n2) * (distance / Maths.Max(1f + Float3.Dot(n1, n2), 0.2f));

    private static Float3 Inward(Float3 normal, Float3 from, Float3 to)
    {
        Float3 direction = to - from;
        Float3 side = Float3.Cross(normal, direction);
        return Float3.LengthSquared(side) > 1e-18f ? Float3.Normalize(side) : Float3.Zero;
    }

    /// <summary>Corners on the edge of the region move inwards by the distance, corners inside it stay put.</summary>
    private static Dictionary<GeometryData.Vertex, Float3> DistanceTargets(List<FaceSnapshot> snapshots, List<Float3> normals,
        Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo> edgeInfo, float distance)
    {
        var arriving = new Dictionary<GeometryData.Vertex, List<Float3>>();
        var leaving = new Dictionary<GeometryData.Vertex, List<Float3>>();
        for (int i = 0; i < snapshots.Count; i++)
        {
            List<GeometryData.Vertex> verts = snapshots[i].Vertices;
            for (int k = 0; k < verts.Count; k++)
            {
                GeometryData.Vertex a = verts[k];
                GeometryData.Vertex b = verts[(k + 1) % verts.Count];
                if (!edgeInfo[GeometryOperators.GetCanonicalEdge(a, b)].IsBoundary) continue;

                Float3 inward = Inward(normals[i], a.Point, b.Point);
                if (!leaving.TryGetValue(a, out List<Float3>? l)) leaving[a] = l = [];
                if (!arriving.TryGetValue(b, out List<Float3>? r)) arriving[b] = r = [];
                l.Add(inward);
                r.Add(inward);
            }
        }

        var targets = new Dictionary<GeometryData.Vertex, Float3>();
        foreach (GeometryData.Vertex? v in snapshots.SelectMany(s => s.Vertices).Distinct())
        {
            bool onEdge = arriving.TryGetValue(v, out List<Float3>? inList) && inList.Count == 1 && leaving.TryGetValue(v, out List<Float3>? outList) && outList.Count == 1;
            targets[v] = onEdge ? v.Point + Miter(arriving[v][0], leaving[v][0], distance) : v.Point;
        }
        return targets;
    }

    private static Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo> BuildEdgeInfo(List<FaceSnapshot> snapshots)
    {
        var map = new Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo>();
        foreach (FaceSnapshot snapshot in snapshots)
        {
            List<GeometryData.Vertex> verts = snapshot.Vertices;
            for (int i = 0; i < verts.Count; i++)
            {
                (GeometryData.Vertex, GeometryData.Vertex) key = GeometryOperators.GetCanonicalEdge(verts[i], verts[(i + 1) % verts.Count]);
                if (!map.TryGetValue(key, out EdgeInfo? info)) map[key] = info = new EdgeInfo();
                info.UsageCount++;
            }
        }
        return map;
    }

    /// <summary>The original behaviour: corners on shared edges slide along them and corners of single faces move to the face centre.</summary>
    private static Dictionary<GeometryData.Vertex, Float3> FractionTargets(List<FaceSnapshot> snapshots, List<Float3> centers,
        Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo> edgeInfo, float amount)
    {
        var infoMap = new Dictionary<GeometryData.Vertex, VertexInfo>();
        for (int i = 0; i < snapshots.Count; i++)
            foreach (GeometryData.Vertex v in snapshots[i].Vertices)
            {
                if (!infoMap.TryGetValue(v, out VertexInfo? info)) infoMap[v] = info = new VertexInfo();
                info.Faces.Add(i);
            }

        foreach (FaceSnapshot snapshot in snapshots)
        {
            List<GeometryData.Vertex> verts = snapshot.Vertices;
            for (int i = 0; i < verts.Count; i++)
            {
                GeometryData.Vertex a = verts[i];
                GeometryData.Vertex b = verts[(i + 1) % verts.Count];
                if (edgeInfo[GeometryOperators.GetCanonicalEdge(a, b)].UsageCount > 1 && infoMap[a].IsShared && infoMap[b].IsShared)
                {
                    infoMap[a].SharedEdgeNeighbors.Add(b);
                    infoMap[b].SharedEdgeNeighbors.Add(a);
                }
            }
        }

        var targets = new Dictionary<GeometryData.Vertex, Float3>();
        foreach (KeyValuePair<GeometryData.Vertex, VertexInfo> pair in infoMap)
        {
            GeometryData.Vertex vert = pair.Key;
            VertexInfo info = pair.Value;
            if (!info.IsShared)
            {
                targets[vert] = Maths.Lerp(vert.Point, centers[info.Faces[0]], amount);
                continue;
            }

            var neighbors = info.SharedEdgeNeighbors.ToList();
            if (neighbors.Count == 1)
                targets[vert] = Maths.Lerp(vert.Point, (vert.Point + neighbors[0].Point) * 0.5f, amount);
            else if (neighbors.Count == 2)
                targets[vert] = Maths.Lerp(vert.Point, (neighbors[0].Point + neighbors[1].Point) * 0.5f, amount);
            else
                targets[vert] = vert.Point;
        }
        return targets;
    }
}
