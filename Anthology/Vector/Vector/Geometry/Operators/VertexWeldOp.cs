// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry.Operators;

internal static class VertexWeldOp
{
    internal static int WeldVertices(GeometryData mesh, float threshold = 0.0001f) =>
        Weld(mesh, mesh.Vertices.ToList(), threshold);

    internal static int WeldVerticesAtPositions(GeometryData mesh, IEnumerable<Float3> targetPositions, float threshold)
    {
        if (threshold <= 0) return 0;

        var targets = targetPositions.ToList();
        var candidates = mesh.Vertices.Where(v => targets.Any(t => Float3.Distance(v.Point, t) <= threshold)).ToList();
        return Weld(mesh, candidates, threshold);
    }

    private readonly record struct Cell(int X, int Y, int Z);

    private static Cell CellOf(Float3 p, float size) =>
        new((int)MathF.Floor(p.X / size), (int)MathF.Floor(p.Y / size), (int)MathF.Floor(p.Z / size));

    /// <summary>
    /// Merges vertices within a distance of each other into one at their average position, averaging their
    /// attributes. Faces are rebuilt on the merged vertices keeping their attributes, with the loop attributes
    /// following the corners that survive. Edges keep their attributes and loose edges are kept.
    /// </summary>
    private static int Weld(GeometryData mesh, List<GeometryData.Vertex> candidates, float threshold)
    {
        if (threshold <= 0 || candidates.Count < 2) return 0;

        var grid = new Dictionary<Cell, List<int>>();
        for (int i = 0; i < candidates.Count; i++)
        {
            var cell = CellOf(candidates[i].Point, threshold);
            if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<int>();
            list.Add(i);
        }

        var removed = new HashSet<GeometryData.Vertex>();
        var mapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

        for (int i = 0; i < candidates.Count; i++)
        {
            var v1 = candidates[i];
            if (removed.Contains(v1)) continue;

            var members = new List<int> { i };
            var home = CellOf(v1.Point, threshold);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue(new Cell(home.X + dx, home.Y + dy, home.Z + dz), out var nearby)) continue;
                        foreach (int j in nearby)
                        {
                            var v2 = candidates[j];
                            if (j <= i || removed.Contains(v2) || Float3.Distance(v1.Point, v2.Point) > threshold) continue;
                            members.Add(j);
                        }
                    }
            if (members.Count < 2) continue;

            // Neighbouring cells are visited in a fixed order, so keep the cluster in mesh order
            members.Sort();
            var cluster = members.Select(m => candidates[m]).ToList();

            Float3 average = Float3.Zero;
            foreach (var v in cluster) average += v.Point;
            v1.Point = average / cluster.Count;
            AverageAttributes(mesh, cluster, v1);

            for (int j = 1; j < cluster.Count; j++)
            {
                mapping[cluster[j]] = v1;
                removed.Add(cluster[j]);
            }
        }

        if (mapping.Count == 0) return 0;

        GeometryData.Vertex Map(GeometryData.Vertex v) => mapping.TryGetValue(v, out var kept) ? kept : v;

        // Everything that touches a merged vertex is rebuilt on the vertex that is kept
        var affectedFaces = mesh.Faces.Where(f => f.NeighborVerticesEnumerable().Any(mapping.ContainsKey)).ToList();
        var snapshots = affectedFaces.Select(f => FaceSnapshot.Capture(mesh, f)).ToList();
        var edgeRecords = mesh.Edges
            .Where(e => mapping.ContainsKey(e.Vert1) || mapping.ContainsKey(e.Vert2))
            .Select(e => (A: Map(e.Vert1), B: Map(e.Vert2), Attributes: AttributeTransfer.Copy(e.Attributes), Loose: e.Loop == null))
            .ToList();

        foreach (var face in affectedFaces) mesh.RemoveFace(face);

        foreach (FaceSnapshot snapshot in snapshots)
        {
            var verts = new List<GeometryData.Vertex>();
            var loops = new List<Dictionary<string, GeometryData.AttributeValue>>();
            for (int i = 0; i < snapshot.Vertices.Count; i++)
            {
                var mapped = Map(snapshot.Vertices[i]);
                if (verts.Count > 0 && verts[verts.Count - 1] == mapped) continue;
                verts.Add(mapped);
                loops.Add(snapshot.Loops[i]);
            }
            if (verts.Count > 1 && verts[0] == verts[verts.Count - 1])
            {
                verts.RemoveAt(verts.Count - 1);
                loops.RemoveAt(loops.Count - 1);
            }
            if (verts.Count < 3 || verts.Distinct().Count() != verts.Count) continue;

            var face = mesh.AddFace(verts.ToArray());
            if (face == null) continue;

            snapshot.ApplyFaceAttributes(face);
            var byVertex = new Dictionary<GeometryData.Vertex, Dictionary<string, GeometryData.AttributeValue>>();
            for (int i = 0; i < verts.Count; i++) byVertex[verts[i]] = loops[i];
            FaceSnapshot.ApplyLoops(face, v => byVertex[v]);
        }

        var restored = new HashSet<GeometryData.Edge>();
        foreach (var record in edgeRecords)
        {
            if (record.A == record.B) continue;
            var edge = mesh.FindEdge(record.A, record.B);
            if (edge == null)
            {
                if (!record.Loose) continue;
                edge = mesh.AddEdge(record.A, record.B);
            }
            if (restored.Add(edge)) AttributeTransfer.Apply(record.Attributes, edge.Attributes);
        }

        foreach (var v in removed)
            if (mesh.Vertices.Contains(v)) mesh.RemoveVertex(v);

        return mapping.Count;
    }

    private static void AverageAttributes(GeometryData mesh, List<GeometryData.Vertex> cluster, GeometryData.Vertex target)
    {
        foreach (var attribute in mesh.VertexAttributes)
        {
            if (attribute.Type.BaseType == GeometryData.AttributeBaseType.Float)
            {
                var values = cluster.Select(v => v.Attributes.TryGetValue(attribute.Name, out var a) ? a as GeometryData.FloatAttributeValue : null).Where(a => a != null).ToList();
                if (values.Count == 0) continue;
                var data = new float[values[0]!.Data.Length];
                for (int k = 0; k < data.Length; k++)
                    data[k] = values.Sum(a => k < a!.Data.Length ? a.Data[k] : 0f) / values.Count;
                target.Attributes[attribute.Name] = new GeometryData.FloatAttributeValue { Data = data };
            }
            else if (attribute.Type.BaseType == GeometryData.AttributeBaseType.Int)
            {
                var values = cluster.Select(v => v.Attributes.TryGetValue(attribute.Name, out var a) ? a as GeometryData.IntAttributeValue : null).Where(a => a != null).ToList();
                if (values.Count == 0) continue;
                var data = new int[values[0]!.Data.Length];
                for (int k = 0; k < data.Length; k++)
                    data[k] = (int)MathF.Round(values.Sum(a => k < a!.Data.Length ? (float)a.Data[k] : 0f) / values.Count);
                target.Attributes[attribute.Name] = new GeometryData.IntAttributeValue { Data = data };
            }
        }
    }
}
