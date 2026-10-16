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

internal static class IntersectFacesOp
{
    /// <summary>
    /// Cuts faces where they pass through each other, adding the edges of the crossing so each side can be
    /// selected on its own. With a selection only those faces are cut against the rest. Returns how many
    /// crossing lines were added.
    /// </summary>
    internal static int IntersectFaces(GeometryData mesh, IReadOnlyCollection<Face>? only)
    {
        List<Face> faces = mesh.Faces.ToList();
        var points = faces.Select(f => f.NeighborVertices().Select(v => v.Point).ToList()).ToList();
        var normals = faces.Select(f => Float3.Normalize(f.Normal())).ToList();
        var boxes = points.Select(p => (Min: p.Aggregate(Maths.Min), Max: p.Aggregate(Maths.Max))).ToList();
        var chosen = only == null ? null : only.SelectMany(f => f.LogicalFace()).ToHashSet();

        var segments = new List<(Float3 A, Float3 B)>();
        for (int i = 0; i < faces.Count; i++)
        {
            for (int j = i + 1; j < faces.Count; j++)
            {
                if (chosen != null && !chosen.Contains(faces[i]) && !chosen.Contains(faces[j])) continue;
                if (Float3.Length(Float3.Cross(normals[i], normals[j])) < 1e-4f) continue;
                if (boxes[i].Min.X > boxes[j].Max.X + Eps || boxes[j].Min.X > boxes[i].Max.X + Eps
                    || boxes[i].Min.Y > boxes[j].Max.Y + Eps || boxes[j].Min.Y > boxes[i].Max.Y + Eps
                    || boxes[i].Min.Z > boxes[j].Max.Z + Eps || boxes[j].Min.Z > boxes[i].Max.Z + Eps) continue;

                Float3 direction = Float3.Cross(normals[i], normals[j]);
                float lengthSq = Float3.LengthSquared(direction);
                float di = Float3.Dot(normals[i], points[i][0]), dj = Float3.Dot(normals[j], points[j][0]);
                Float3 origin = (Float3.Cross(normals[j], direction) * di + Float3.Cross(direction, normals[i]) * dj) / lengthSq;
                direction /= MathF.Sqrt(lengthSq);

                List<(float, float)> a = Intervals(points[i], normals[i], origin, direction);
                List<(float, float)> b = Intervals(points[j], normals[j], origin, direction);
                foreach ((float a0, float a1) in a)
                {
                    foreach ((float b0, float b1) in b)
                    {
                        float t0 = MathF.Max(a0, b0), t1 = MathF.Min(a1, b1);
                        if (t1 - t0 <= Eps) continue;
                        Float3 p = origin + direction * t0, q = origin + direction * t1;
                        Vertex? vp = mesh.FindVertex(p), vq = mesh.FindVertex(q);
                        if (vp != null && vq != null && mesh.FindEdge(vp, vq) != null) continue;
                        segments.Add((p, q));
                    }
                }
            }
        }
        if (segments.Count == 0) return 0;

        var seeds = new List<Vertex>();
        var planes = new List<PlaneKey>();
        foreach ((Float3 a, Float3 b) in segments)
        {
            Vertex va = mesh.GetOrCreateVertex(a), vb = mesh.GetOrCreateVertex(b);
            if (va == vb) continue;
            mesh.AddEdge(va, vb, false);
            seeds.Add(va);
            seeds.Add(vb);
        }

        for (int i = 0; i < faces.Count; i++)
        {
            Float3 origin = points[i][0];
            bool touched = segments.Any(s => MathF.Abs(Float3.Dot(s.A - origin, normals[i])) <= Eps && MathF.Abs(Float3.Dot(s.B - origin, normals[i])) <= Eps);
            if (touched) AddPlane(planes, normals[i], origin);
        }

        foreach (PlaneKey plane in planes)
            PlaneRebuildOp.Run(mesh, plane.Normal, plane.Origin, seeds.Where(v => mesh.Vertices.Contains(v)), [], 0);

        mesh.RemoveUnused();
        mesh.MarkChanged();
        return segments.Count;
    }

    /// <summary>The stretches of a line inside a flat polygon, closed so a polygon edge lying on the line is included.</summary>
    private static List<(float, float)> Intervals(List<Float3> polygon, Float3 normal, Float3 origin, Float3 direction)
    {
        var all = new List<(float, float)>();
        foreach (float shift in new[] { -2f * Eps, 2f * Eps })
        {
            var ts = new List<float>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Float3 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                float sa = Float3.Dot(Float3.Cross(a - origin, direction), normal) - shift;
                float sb = Float3.Dot(Float3.Cross(b - origin, direction), normal) - shift;
                if ((sa >= 0f) == (sb >= 0f)) continue;

                // Crossings that the shift only nudged onto the line land exactly on the vertex
                float s0a = sa + shift, s0b = sb + shift;
                Float3 x = MathF.Abs(s0a) <= 2.5f * Eps ? a : MathF.Abs(s0b) <= 2.5f * Eps ? b : a + (b - a) * (s0a / (s0a - s0b));
                ts.Add(Float3.Dot(x - origin, direction));
            }
            ts.Sort();
            for (int i = 0; i + 1 < ts.Count; i += 2) all.Add((ts[i], ts[i + 1]));
        }

        all.Sort();
        var merged = new List<(float, float)>();
        foreach ((float t0, float t1) in all)
        {
            if (merged.Count > 0 && t0 <= merged[^1].Item2 + Eps) merged[^1] = (merged[^1].Item1, MathF.Max(merged[^1].Item2, t1));
            else merged.Add((t0, t1));
        }
        return merged;
    }
}
