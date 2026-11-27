// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using static Prowl.Vector.Geometry.Operators.EditingCommon;

using Edge = Prowl.Vector.Geometry.GeometryData.Edge;
using Face = Prowl.Vector.Geometry.GeometryData.Face;
using Vertex = Prowl.Vector.Geometry.GeometryData.Vertex;

namespace Prowl.Vector.Geometry.Operators;

internal static class SoftenOp
{
    /// <summary>Marks the edges along a run of points as soft, the way a drawn circle or arc is a smooth curve and not a pile of flat sides.</summary>
    internal static void SoftenPolyline(GeometryData mesh, IReadOnlyList<Float3> points, bool closed)
    {
        int count = closed ? points.Count : points.Count - 1;
        for (int i = 0; i < count; i++)
        {
            Vertex? a = mesh.FindVertex(points[i]), b = mesh.FindVertex(points[(i + 1) % points.Count]);
            if (a == null || b == null || a == b) continue;
            if (mesh.FindEdge(a, b) is Edge e) e.IsSoft = true;
        }
        mesh.MarkChanged();
    }

    /// <summary>Makes edges soft where their two faces meet at less than an angle, so rounded surfaces shade smooth.</summary>
    internal static int SoftenByAngle(GeometryData mesh, IEnumerable<Edge> edges, float degrees)
    {
        float limit = MathF.Cos(degrees * Maths.Deg2Rad);
        int count = 0;
        foreach (Edge e in edges)
        {
            if (!mesh.Contains(e) || e.IsHidden) continue;
            if (e.DihedralCosine() is not float cosine || cosine > 0.99999f) continue;
            bool soft = cosine > limit;
            if (e.IsSoft != soft) count++;
            e.IsSoft = soft;
        }
        mesh.MarkChanged();
        return count;
    }

    internal static void SetSoft(GeometryData mesh, IEnumerable<Edge> edges, bool soft)
    {
        foreach (Edge e in edges)
            if (mesh.Contains(e)) e.IsSoft = soft;
        mesh.MarkChanged();
    }

    internal static HashSet<Vertex> Shade(GeometryData mesh, IEnumerable<Face> faces, bool smooth)
    {
        var vertices = new HashSet<Vertex>();
        foreach (Face face in faces)
        {
            vertices.UnionWith(face.NeighborVertices());
            foreach (Edge edge in face.NeighborEdges())
            {
                if (edge.IsHidden) continue;
                edge.IsSoft = smooth;
                edge.IsSharp = !smooth;
            }
        }
        mesh.MarkChanged();
        return vertices;
    }

    internal static void CornerNormals(GeometryData mesh, string attribute, ISet<Vertex>? only)
    {
        if (!mesh.HasLoopAttribute(attribute)) mesh.AddLoopAttribute(attribute, GeometryData.AttributeBaseType.Float, 3);

        var areaNormals = new Dictionary<Face, Float3>(mesh.Faces.Count);
        foreach (Face face in mesh.Faces) areaNormals[face] = face.Normal() * face.Area();

        var fan = new HashSet<Face>();
        var stack = new Stack<Face>();
        foreach (Face face in mesh.Faces)
            foreach (GeometryData.Loop loop in face.Corners())
            {
                Vertex vertex = loop.Vert;
                if (only != null && !only.Contains(vertex)) continue;
                fan.Clear();
                fan.Add(face);
                stack.Push(face);
                while (stack.Count > 0)
                {
                    Face at = stack.Pop();
                    foreach (Edge edge in at.NeighborEdges())
                    {
                        if (!edge.ContainsVertex(vertex) || !edge.IsSoft || edge.IsSharp) continue;
                        foreach (Face next in edge.NeighborFaces())
                            if (fan.Add(next)) stack.Push(next);
                    }
                }

                Float3 sum = Float3.Zero;
                foreach (Face member in fan) sum += areaNormals[member];
                Float3 normal = Float3.LengthSquared(sum) > 1e-20f ? Float3.Normalize(sum) : Float3.UnitY;
                loop.Attributes[attribute] = new GeometryData.FloatAttributeValue(normal.X, normal.Y, normal.Z);
            }
    }
}
