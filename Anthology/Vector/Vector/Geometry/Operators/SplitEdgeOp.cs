// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Prowl.Vector.Geometry.Operators;

internal static class SplitEdgeOp
{
    /// <summary>
    /// Splits an edge by adding a vertex part of the way along it. The faces either side gain a corner there, with
    /// loop attributes interpolated along the edge, and both halves keep the attributes of the original edge.
    /// </summary>
    internal static GeometryData.Vertex SplitEdge(GeometryData mesh, GeometryData.Edge edge,
        GeometryData.Vertex fromVertex, float factor, out GeometryData.Edge newEdge)
    {
        Debug.Assert(edge.ContainsVertex(fromVertex));
        Debug.Assert(factor >= 0.0f && factor <= 1.0f);

        GeometryData.Vertex otherVertex = edge.OtherVertex(fromVertex);
        GeometryData.Vertex made = mesh.AddVertex(Maths.Lerp(fromVertex.Point, otherVertex.Point, factor));
        GeometryOperators.AttributeLerp(mesh, made, fromVertex, otherVertex, factor);

        var edgeAttributes = AttributeTransfer.Copy(edge.Attributes);
        List<GeometryData.Face> faces = edge.NeighborFaces().Distinct().ToList();
        List<FaceSnapshot> snapshots = faces.Select(f => FaceSnapshot.Capture(mesh, f)).ToList();

        foreach (var face in faces) mesh.RemoveFace(face);
        mesh.RemoveEdge(edge);

        GeometryData.Edge firstHalf = mesh.AddEdge(fromVertex, made);
        newEdge = mesh.AddEdge(made, otherVertex);
        AttributeTransfer.Apply(edgeAttributes, firstHalf.Attributes);
        AttributeTransfer.Apply(edgeAttributes, newEdge.Attributes);

        foreach (FaceSnapshot snapshot in snapshots)
        {
            var verts = new List<GeometryData.Vertex>(snapshot.Vertices.Count + 1);
            for (int i = 0; i < snapshot.Vertices.Count; i++)
            {
                GeometryData.Vertex current = snapshot.Vertices[i];
                GeometryData.Vertex next = snapshot.Vertices[(i + 1) % snapshot.Vertices.Count];
                verts.Add(current);
                if ((current == fromVertex && next == otherVertex) || (current == otherVertex && next == fromVertex))
                    verts.Add(made);
            }

            GeometryData.Face? created = mesh.AddFace(verts.ToArray());
            if (created == null) continue;

            snapshot.ApplyFaceAttributes(created);
            var fromLoop = snapshot.LoopOf(fromVertex);
            var otherLoop = snapshot.LoopOf(otherVertex);
            FaceSnapshot.ApplyLoops(created, v =>
                v != made ? snapshot.LoopOf(v) : fromLoop != null && otherLoop != null ? AttributeTransfer.Lerp(fromLoop, otherLoop, factor) : fromLoop ?? otherLoop);
        }

        return made;
    }
}
