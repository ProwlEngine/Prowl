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
            if (!mesh.Edges.Contains(e) || e.IsHidden) continue;
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
            if (mesh.Edges.Contains(e)) e.IsSoft = soft;
        mesh.MarkChanged();
    }
}
