// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Unwrapper;

/// <summary>Unwrapping and packing the texture coordinates of a mesh's faces, keeping them in its UV attribute.</summary>
public static class GeometryUnwrap
{
    /// <summary>
    /// Lays the faces out flat, cut apart at seams, and packs them into the unit square. Faces stay joined across an edge
    /// only when both are unwrapped and the edge is no seam. Pinned corners keep their UVs, and a piece with two pins or
    /// more is laid out around them.
    /// </summary>
    public static void Unwrap(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces)
    {
        if (faces.Count == 0) return;
        GeometryUVs.EnsureUV(mesh);
        var inSet = new HashSet<GeometryData.Face>(faces);

        var corners = new List<GeometryData.Loop>();
        var cornerIndex = new Dictionary<GeometryData.Loop, int>();
        foreach (GeometryData.Face face in faces)
            foreach (GeometryData.Loop corner in face.Corners())
            {
                cornerIndex[corner] = corners.Count;
                corners.Add(corner);
            }

        // Corners at one point are one point of the layout where their faces meet across an edge that is no seam
        var parent = new int[corners.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }
        foreach (GeometryData.Face face in faces)
            foreach (GeometryData.Loop corner in face.Corners())
            {
                if (GeometryUVs.IsSeam(corner.Edge)) continue;
                foreach (GeometryData.Face other in corner.Edge.NeighborFaces())
                {
                    if (other == face || !inSet.Contains(other)) continue;
                    foreach (GeometryData.Vertex end in (GeometryData.Vertex[])[corner.Edge.Vert1, corner.Edge.Vert2])
                        if (face.GetLoop(end) is { } mine && other.GetLoop(end) is { } theirs)
                            parent[Find(cornerIndex[mine])] = Find(cornerIndex[theirs]);
                }
            }

        var pointOf = new int[corners.Count];
        var positions = new List<Double3>();
        var pins = new Dictionary<int, Double2>();
        var rootPoint = new Dictionary<int, int>();
        for (int i = 0; i < corners.Count; i++)
        {
            int root = Find(i);
            if (!rootPoint.TryGetValue(root, out int point))
            {
                point = positions.Count;
                rootPoint[root] = point;
                Float3 p = corners[i].Vert.Point;
                positions.Add(new Double3(p.X, p.Y, p.Z));
            }
            pointOf[i] = point;
            if (GeometryUVs.IsPinned(corners[i]))
            {
                Float2 uv = GeometryUVs.Get(corners[i]);
                pins[point] = new Double2(uv.X, uv.Y);
            }
        }

        var triangles = new List<int>();
        var triangleCorners = new List<GeometryData.Loop>();
        foreach (GeometryData.Face face in faces)
            foreach ((GeometryData.Loop a, GeometryData.Loop b, GeometryData.Loop c) in GeometryUVs.Triangles(face))
                foreach (GeometryData.Loop corner in (GeometryData.Loop[])[a, b, c])
                {
                    triangles.Add(pointOf[cornerIndex[corner]]);
                    triangleCorners.Add(corner);
                }

        ChartUnwrapResult result = ChartUnwrapper.Unwrap([.. positions], [.. triangles], null, pins.Count > 0 ? pins : null);
        for (int t = 0; t < result.TriangleCharts.Length; t++)
        {
            if (result.TriangleCharts[t] < 0) continue;
            for (int k = 0; k < 3; k++)
            {
                Double2 uv = result.PerCornerUVs[3 * t + k];
                GeometryData.Loop corner = triangleCorners[3 * t + k];
                if (!GeometryUVs.IsPinned(corner)) GeometryUVs.Set(corner, new Float2((float)uv.X, (float)uv.Y));
            }
        }
        mesh.MarkChanged();
    }

    /// <summary>Lays the faces' islands out in the unit square, each keeping its turn and its size next to the others.</summary>
    public static void Pack(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, float margin = 1f / 256f)
    {
        List<List<GeometryData.Face>> islands = GeometryUVs.Islands(mesh, faces);
        var layouts = new List<Double2[]>(islands.Count);
        var corners = new List<List<GeometryData.Loop>>(islands.Count);
        foreach (List<GeometryData.Face> island in islands)
        {
            var at = new List<GeometryData.Loop>();
            foreach (GeometryData.Face face in island)
                foreach ((GeometryData.Loop a, GeometryData.Loop b, GeometryData.Loop c) in GeometryUVs.Triangles(face))
                    at.AddRange([a, b, c]);
            corners.Add(at);
            layouts.Add([.. at.Select(c => { Float2 uv = GeometryUVs.Get(c); return new Double2(uv.X, uv.Y); })]);
        }
        ChartUnwrapper.Pack(layouts, margin);
        for (int i = 0; i < islands.Count; i++)
            for (int k = 0; k < corners[i].Count; k++)
                GeometryUVs.Set(corners[i][k], new Float2((float)layouts[i][k].X, (float)layouts[i][k].Y));
        mesh.MarkChanged();
    }

    /// <summary>
    /// Groups joined faces whose normals are within the angle of the group's first face, projects each group flat onto
    /// the plane of its average normal, and packs the groups into the unit square.
    /// </summary>
    public static void ByAngle(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, float degrees, float margin = 1f / 256f)
    {
        GeometryUVs.EnsureUV(mesh);
        var inSet = new HashSet<GeometryData.Face>(faces);
        var done = new HashSet<GeometryData.Face>();
        float limit = MathF.Cos(degrees * MathF.PI / 180f);
        var corners = new List<List<GeometryData.Loop>>();
        var layouts = new List<Double2[]>();
        foreach (GeometryData.Face start in faces)
        {
            if (!done.Add(start)) continue;
            Float3 lead = start.Normal();
            var group = new List<GeometryData.Face> { start };
            for (int i = 0; i < group.Count; i++)
                foreach (GeometryData.Loop corner in group[i].Corners())
                    foreach (GeometryData.Face other in corner.Edge.NeighborFaces())
                        if (inSet.Contains(other) && !done.Contains(other) && Float3.Dot(other.Normal(), lead) >= limit)
                        {
                            done.Add(other);
                            group.Add(other);
                        }

            Float3 sum = Float3.Zero;
            foreach (GeometryData.Face face in group) sum += face.Normal() * GeometryUVs.Areas(face).Surface;
            var frame = new PlaneFrame(Float3.LengthSquared(sum) > 1e-12f ? sum : lead, Float3.Zero);
            var at = new List<GeometryData.Loop>();
            foreach (GeometryData.Face face in group)
                foreach ((GeometryData.Loop a, GeometryData.Loop b, GeometryData.Loop c) in GeometryUVs.Triangles(face))
                    at.AddRange([a, b, c]);
            corners.Add(at);
            layouts.Add([.. at.Select(c => { Float2 p = frame.To2D(c.Vert.Point); return new Double2(p.X, p.Y); })]);
        }
        ChartUnwrapper.Pack(layouts, margin);
        for (int i = 0; i < corners.Count; i++)
            for (int k = 0; k < corners[i].Count; k++)
                GeometryUVs.Set(corners[i][k], new Float2((float)layouts[i][k].X, (float)layouts[i][k].Y));
        mesh.MarkChanged();
    }
}
