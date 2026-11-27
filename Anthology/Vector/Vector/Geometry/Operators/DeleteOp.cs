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

internal static class DeleteOp
{
    /// <summary>Deletes faces, along with the rest of any face they were joined to by hidden edges. Their edges stay.</summary>
    internal static void DeleteFaces(GeometryData mesh, IEnumerable<Face> faces)
    {
        var all = GeometryData.LogicalFaces(faces.Where(f => mesh.Contains(f))).ToHashSet();
        foreach (Face f in all) mesh.RemoveFace(f);
        mesh.RemoveUnused();
        mesh.MarkChanged();
    }

    /// <summary>
    /// Deletes edges. An edge between two faces lying flat together merges them, any other edge takes the
    /// faces it bounds with it. Lines left running straight through a vertex are joined back up.
    /// </summary>
    internal static void DeleteEdges(GeometryData mesh, IEnumerable<Edge> edges)
    {
        var ends = new List<Vertex>();
        foreach (Edge e in edges.ToList())
        {
            if (!mesh.Contains(e) || e.IsHidden) continue;
            Vertex a = e.Vert1, b = e.Vert2;
            ends.Add(a);
            ends.Add(b);

            List<Face> faces = e.NeighborFaces();
            if (faces.Count == 2 && faces[0].IsCoplanarWith(faces[1]))
            {
                Float3 normal = Float3.Normalize(faces[0].Normal());
                PlaneRebuildOp.Run(mesh, normal, a.Point, [a, b], [], 0, new HashSet<Edge> { e });
                if (mesh.Contains(e)) RemoveWithLogical(mesh, e);
            }
            else
            {
                RemoveWithLogical(mesh, e);
            }
        }

        foreach (Vertex v in ends.Distinct())
            if (mesh.Contains(v)) mesh.DissolveVertex(v);

        mesh.RemoveUnused();
        mesh.MarkChanged();
    }

    private static void RemoveWithLogical(GeometryData mesh, Edge e)
    {
        var faces = GeometryData.LogicalFaces(e.NeighborFaces()).ToHashSet();
        foreach (Face f in faces) mesh.RemoveFace(f);
        mesh.RemoveEdge(e);
    }

    /// <summary>Deletes vertices. One sitting in the middle of a straight line just joins the line back up.</summary>
    internal static void DeleteVertices(GeometryData mesh, IEnumerable<Vertex> vertices)
    {
        foreach (Vertex v in vertices.ToList())
        {
            if (!mesh.Contains(v) || mesh.DissolveVertex(v)) continue;
            foreach (Edge e in v.NeighborEdges())
                foreach (Face f in GeometryData.LogicalFaces(e.NeighborFaces()))
                    if (mesh.Contains(f)) mesh.RemoveFace(f);
            mesh.RemoveVertex(v);
        }
        mesh.RemoveUnused();
        mesh.MarkChanged();
    }
}
