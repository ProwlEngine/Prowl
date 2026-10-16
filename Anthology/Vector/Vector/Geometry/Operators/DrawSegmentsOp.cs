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

internal static class DrawSegmentsOp
{
    /// <summary>
    /// Draws lines into the mesh. Any closed loop they make on a plane becomes a face, and lines across an
    /// existing face split it.
    /// </summary>
    /// <param name="planeNormal">The plane the lines were drawn on, when the tool knows it.</param>
    /// <param name="viewDirection">Where the camera looks, so new faces in the air face it.</param>
    internal static void DrawSegments(GeometryData mesh, IReadOnlyList<(Float3 A, Float3 B)> segments, Float3? planeNormal, Float3 viewDirection, int material)
    {
        var drawn = segments.Where(s => Float3.Length(s.B - s.A) > Eps).ToList();
        if (drawn.Count == 0) return;

        var seeds = new List<Vertex>();
        var newEdges = new List<Edge>();
        foreach ((Float3 a, Float3 b) in drawn)
        {
            Vertex va = mesh.GetOrCreateVertex(a);
            Vertex vb = mesh.GetOrCreateVertex(b);
            seeds.Add(va);
            seeds.Add(vb);
            Edge? edge = mesh.AddEdge(va, vb, false);
            if (edge != null) newEdges.Add(edge);
        }

        var planes = new List<PlaneKey>();
        if (planeNormal is Float3 given)
            AddPlane(planes, given, drawn[0].A);

        foreach ((Float3 a, Float3 b) in drawn)
        {
            foreach (Face face in mesh.Faces)
            {
                Float3 normal = face.Normal();
                Float3 origin = face.Loop!.Vert.Point;
                if (MathF.Abs(Float3.Dot(a - origin, normal)) > Eps || MathF.Abs(Float3.Dot(b - origin, normal)) > Eps) continue;
                AddPlane(planes, normal, origin);
            }
        }

        // A line that closes a loop in the air makes a plane with the lines it joins
        foreach (Edge edge in newEdges.ToList())
        {
            if (!mesh.Edges.Contains(edge)) continue;
            Float3 direction = Float3.Normalize(edge.Vert2.Point - edge.Vert1.Point);
            foreach (Vertex end in new[] { edge.Vert1, edge.Vert2 })
            {
                foreach (Edge other in end.NeighborEdges())
                {
                    if (other == edge || other.IsHidden) continue;
                    Float3 otherDirection = Float3.Normalize(other.OtherVertex(end).Point - end.Point);
                    Float3 normal = Float3.Cross(direction, otherDirection);
                    if (Float3.Length(normal) < 1e-3f) continue;
                    AddPlane(planes, OrientNew(Float3.Normalize(normal), viewDirection), end.Point);
                }
            }
        }

        foreach (PlaneKey plane in planes)
        {
            var inPlane = drawn.Where(s => MathF.Abs(Float3.Dot(s.A - plane.Origin, plane.Normal)) <= Eps
                                        && MathF.Abs(Float3.Dot(s.B - plane.Origin, plane.Normal)) <= Eps).ToList();
            if (inPlane.Count == 0) continue;
            PlaneRebuildOp.Run(mesh, plane.Normal, plane.Origin, seeds.Where(v => mesh.Vertices.Contains(v)), inPlane, material);
        }

        mesh.RemoveUnused();
        mesh.MarkChanged();
    }

    /// <summary>
    /// Joins selected vertices that share a face with new edges, in the order they run around it. Returns
    /// how many edges were drawn.
    /// </summary>
    internal static int ConnectVertices(GeometryData mesh, IReadOnlyCollection<Vertex> vertices, int material, Float3 viewDirection)
    {
        var chosen = vertices.ToHashSet();
        var segments = new List<(Float3, Float3)>();
        foreach (Face face in mesh.Faces.ToList())
        {
            List<Vertex> loop = face.NeighborVertices();
            List<int> picked = Enumerable.Range(0, loop.Count).Where(i => chosen.Contains(loop[i])).ToList();
            if (picked.Count < 2) continue;

            for (int k = 0; k < picked.Count; k++)
            {
                if (picked.Count == 2 && k == 1) break;
                int i = picked[k], j = picked[(k + 1) % picked.Count];
                int gap = (j - i + loop.Count) % loop.Count;
                if (gap <= 1 || gap >= loop.Count - 1) continue;
                if (mesh.FindEdge(loop[i], loop[j]) != null) continue;
                segments.Add((loop[i].Point, loop[j].Point));
            }
        }

        if (segments.Count == 0) return 0;
        DrawSegments(mesh, segments, null, viewDirection, material);
        return segments.Count;
    }
}
