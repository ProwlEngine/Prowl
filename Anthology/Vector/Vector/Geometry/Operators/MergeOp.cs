// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry.Operators;

internal static class MergeOp
{
    /// <summary>
    /// Copies everything in another mesh into this one: vertices, edges and faces with all their attributes.
    /// Attributes the other mesh has that this one lacks are added here first.
    /// </summary>
    internal static void Merge(GeometryData mesh, GeometryData other)
    {
        foreach (GeometryData.AttributeDefinition a in other.VertexAttributes) mesh.AddVertexAttribute(a.Name, a.Type.BaseType, a.Type.Dimensions);
        foreach (GeometryData.AttributeDefinition a in other.EdgeAttributes) mesh.AddEdgeAttribute(a.Name, a.Type.BaseType, a.Type.Dimensions);
        foreach (GeometryData.AttributeDefinition a in other.LoopAttributes) mesh.AddLoopAttribute(a.Name, a.Type.BaseType, a.Type.Dimensions);
        foreach (GeometryData.AttributeDefinition a in other.FaceAttributes) mesh.AddFaceAttribute(a.Name, a.Type.BaseType, a.Type.Dimensions);

        var map = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>(other.Vertices.Count);
        foreach (GeometryData.Vertex v in other.Vertices)
        {
            GeometryData.Vertex copy = mesh.AddVertex(v.Point);
            AttributeTransfer.Apply(v.Attributes, copy.Attributes);
            map[v] = copy;
        }

        foreach (GeometryData.Edge e in other.Edges)
        {
            GeometryData.Edge copy = mesh.AddEdge(map[e.Vert1], map[e.Vert2]);
            AttributeTransfer.Apply(e.Attributes, copy.Attributes);
        }

        foreach (GeometryData.Face f in other.Faces)
        {
            FaceSnapshot snapshot = FaceSnapshot.Capture(other, f);
            GeometryData.Face? copy = mesh.AddFace(snapshot.Vertices.ConvertAll(v => map[v]).ToArray());
            if (copy == null) continue;

            snapshot.ApplyFaceAttributes(copy);
            var original = snapshot.Vertices.ToDictionary(v => map[v]);
            FaceSnapshot.ApplyLoops(copy, v => snapshot.LoopOf(original[v]));
        }
    }
}
