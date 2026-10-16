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

internal static class CopyOp
{
    /// <summary>
    /// Copies faces and loose edges through a transform. Copies that land on existing faces merge into
    /// them like drawn geometry. Returns the new vertices.
    /// </summary>
    internal static List<Vertex> Copy(GeometryData mesh, IReadOnlyCollection<Face> faces, IReadOnlyCollection<Edge> edges, Func<Float3, Float3> transform, bool flip = false)
    {
        var map = new Dictionary<Vertex, Vertex>();
        Vertex Map(Vertex v)
        {
            if (!map.TryGetValue(v, out Vertex? copy)) map[v] = copy = mesh.AddVertex(transform(v.Point));
            return copy;
        }

        var all = faces.SelectMany(f => f.LogicalFace()).ToHashSet();
        foreach (Face f in all)
        {
            List<Vertex> copy = f.NeighborVertices().Select(Map).ToList();
            if (flip) copy.Reverse();
            mesh.AddFace(copy, f);
            foreach (Edge e in f.NeighborEdges())
                if (e.IsHidden && mesh.FindEdge(Map(e.Vert1), Map(e.Vert2)) is Edge hiddenCopy) hiddenCopy.IsHidden = true;
        }
        foreach (Edge e in edges)
            if (!e.IsHidden) mesh.AddEdge(Map(e.Vert1), Map(e.Vert2), false);

        List<Vertex> copies = map.Values.ToList();
        RebuildAround(mesh, copies);
        return copies.Where(v => mesh.Vertices.Contains(v)).ToList();
    }

    /// <summary>A mirrored copy of faces and edges across a plane. The copies are wound to face the same way as the originals.</summary>
    internal static List<Vertex> Mirror(GeometryData mesh, IReadOnlyCollection<Face> faces, IReadOnlyCollection<Edge> edges, Plane plane)
    {
        Float3 Reflect(Float3 p) => p - plane.Normal * (2f * plane.GetSignedDistanceToPoint(p));
        return Copy(mesh, faces, edges, Reflect, flip: true);
    }

    /// <summary>Copies faces and edges several times, each by the next multiple of a transform.</summary>
    internal static void Array(GeometryData mesh, IReadOnlyCollection<Face> faces, IReadOnlyCollection<Edge> edges, int copies, Func<Float3, int, Float3> transform)
    {
        for (int i = 1; i <= copies; i++)
        {
            int step = i;
            Copy(mesh, faces, edges, p => transform(p, step));
        }
    }
}
