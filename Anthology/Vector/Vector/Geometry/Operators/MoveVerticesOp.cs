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

/// <summary>Moving vertices, after which faces bent out of flat are folded and the planes touched are rebuilt.</summary>
internal static class MoveVerticesOp
{
    /// <summary>Moves vertices. Faces bent out of flat are folded along hidden edges, then touched planes are rebuilt.</summary>
    internal static void MoveVertices(GeometryData mesh, IReadOnlyCollection<Vertex> vertices, Float3 delta)
    {
        if (Float3.Length(delta) <= 1e-7f) return;
        TransformVertices(mesh, vertices, p => p + delta);
    }

    /// <summary>Moves vertices by any transform, folding and rebuilding like <see cref="MoveVertices"/>.</summary>
    internal static void TransformVertices(GeometryData mesh, IReadOnlyCollection<Vertex> vertices, Func<Float3, Float3> transform)
    {
        if (vertices.Count == 0) return;

        foreach (Vertex v in vertices) v.Point = transform(v.Point);
        RebuildAround(mesh, vertices);
    }

    /// <summary>Scales vertices about a pivot, per axis in the given frame.</summary>
    internal static void Scale(GeometryData mesh, IReadOnlyCollection<Vertex> vertices, Float3 pivot, Float3 factors, Quaternion frame)
    {
        Quaternion inverse = Quaternion.Inverse(frame);
        TransformVertices(mesh, vertices, p =>
        {
            Float3 local = inverse * (p - pivot);
            local = new Float3(local.X * factors.X, local.Y * factors.Y, local.Z * factors.Z);
            return pivot + frame * local;
        });
    }

    /// <summary>Joins vertices into one at a point. Faces that collapse away with them are removed.</summary>
    internal static void MergeAt(GeometryData mesh, IReadOnlyCollection<Vertex> vertices, Float3 target)
    {
        if (vertices.Count < 2) return;
        foreach (Vertex v in vertices) v.Point = target;
        RebuildAround(mesh, vertices);
    }

    /// <summary>
    /// Collapses each group of connected selected edges to a single point at its center. Returns how many
    /// vertices were joined.
    /// </summary>
    internal static int CollapseEdges(GeometryData mesh, IEnumerable<Edge> edges)
    {
        var groups = new Dictionary<Vertex, Vertex>();
        Vertex Find(Vertex v)
        {
            if (!groups.TryGetValue(v, out Vertex? parent)) { groups[v] = v; return v; }
            while (parent != v)
            {
                Vertex grand = groups[parent];
                groups[v] = grand;
                v = parent;
                parent = grand;
            }
            return v;
        }

        foreach (Edge e in edges)
        {
            if (!mesh.Edges.Contains(e)) continue;
            Vertex a = Find(e.Vert1), b = Find(e.Vert2);
            if (a != b) groups[a] = b;
        }

        var clusters = groups.Keys.GroupBy(Find).Where(g => g.Count() > 1).Select(g => g.ToList()).ToList();
        int joined = 0;
        foreach (List<Vertex> cluster in clusters)
        {
            Float3 center = GeometryData.Centroid(cluster);
            foreach (Vertex v in cluster) v.Point = center;
            joined += cluster.Count - 1;
        }

        if (clusters.Count > 0) RebuildAround(mesh, clusters.SelectMany(c => c).ToList());
        return joined;
    }

    /// <summary>
    /// Welds vertices that lie within a distance of each other onto their average. With no scope every
    /// vertex is considered. Returns how many vertices were joined.
    /// </summary>
    internal static int WeldByDistance(GeometryData mesh, IReadOnlyCollection<Vertex>? scope, float distance)
    {
        List<Vertex> verts = (scope ?? mesh.Vertices).Where(v => mesh.Vertices.Contains(v)).ToList();
        if (verts.Count < 2 || distance <= 0f) return 0;

        var parent = Enumerable.Range(0, verts.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }

        float cell = MathF.Max(distance, 1e-4f);
        var grid = new Dictionary<(int, int, int), List<int>>();
        (int, int, int) Key(Float3 p) => ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
        for (int i = 0; i < verts.Count; i++)
        {
            (int x, int y, int z) = Key(verts[i].Point);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (grid.TryGetValue((x + dx, y + dy, z + dz), out List<int>? bucket))
                            foreach (int j in bucket)
                                if (Float3.Length(verts[i].Point - verts[j].Point) <= distance)
                                    parent[Find(i)] = Find(j);
            if (!grid.TryGetValue((x, y, z), out List<int>? own)) grid[(x, y, z)] = own = [];
            own.Add(i);
        }

        var clusters = Enumerable.Range(0, verts.Count).GroupBy(Find).Where(g => g.Count() > 1).Select(g => g.Select(i => verts[i]).ToList()).ToList();
        int joined = 0;
        foreach (List<Vertex> cluster in clusters)
        {
            Float3 center = GeometryData.Centroid(cluster);
            foreach (Vertex v in cluster) v.Point = center;
            joined += cluster.Count - 1;
        }

        if (clusters.Count > 0) RebuildAround(mesh, clusters.SelectMany(c => c).ToList());
        return joined;
    }
}
