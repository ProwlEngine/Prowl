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

internal static class SubdivideEdgesOp
{
    /// <summary>Adds evenly spaced vertices to edges, splitting every face that uses them. Returns the new vertices.</summary>
    internal static List<Vertex> SubdivideEdges(GeometryData mesh, IEnumerable<Edge> edges, int cuts)
    {
        var created = new List<Vertex>();
        if (cuts < 1) return created;

        foreach (Edge edge in edges.ToList())
        {
            if (!mesh.Edges.Contains(edge)) continue;
            Vertex start = edge.Vert1, end = edge.Vert2;
            Float3 a = start.Point, b = end.Point;
            Edge current = edge;
            Vertex from = start;
            for (int i = 1; i <= cuts; i++)
            {
                Vertex v = mesh.AddVertex(Maths.Lerp(a, b, i / (float)(cuts + 1)));
                mesh.InsertVertexInEdge(current, v);
                created.Add(v);
                from = v;
                current = mesh.FindEdge(from, end)!;
            }
        }

        mesh.MarkChanged();
        return created;
    }
}
