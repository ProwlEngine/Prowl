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

internal static class ExtrudeEdgesOp
{
    /// <summary>
    /// Pushes edges out into a strip of new quads, joining each edge to a copy moved by the offset.
    /// Returns the copies of the vertices.
    /// </summary>
    internal static List<Vertex> ExtrudeEdges(GeometryData mesh, IReadOnlyCollection<Edge> edges, Float3 offset, int material)
    {
        var map = new Dictionary<Vertex, Vertex>();
        Vertex Copy(Vertex v)
        {
            if (!map.TryGetValue(v, out Vertex? copy)) map[v] = copy = mesh.AddVertex(v.Point + offset);
            return copy;
        }

        foreach (Edge e in edges.ToList())
        {
            if (e.IsHidden || !mesh.Edges.Contains(e)) continue;
            Vertex a = e.Vert1, b = e.Vert2;
            Vertex ac = Copy(a), bc = Copy(b);
            mesh.AddEdge(ac, bc, false);

            // Wind the new face against the existing one along the shared edge, so the surface stays consistent
            bool reversed = false;
            if (e.Loop != null)
            {
                var loop = e.Loop;
                reversed = loop.Vert == a;
            }
            else
            {
                reversed = Float3.Dot(Float3.Cross(b.Point - a.Point, offset), Float3.UnitY) < 0f;
            }

            Face? face = reversed ? mesh.AddFace([b, a, ac, bc], material) : mesh.AddFace([a, b, bc, ac], material);
            if (face == null) continue;
        }

        List<Vertex> copies = map.Values.ToList();
        RebuildAround(mesh, copies);
        return copies.Where(mesh.Vertices.Contains).ToList();
    }
}
