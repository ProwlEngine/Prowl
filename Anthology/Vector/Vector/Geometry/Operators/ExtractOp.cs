// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Edge = Prowl.Vector.Geometry.GeometryData.Edge;
using Face = Prowl.Vector.Geometry.GeometryData.Face;
using Vertex = Prowl.Vector.Geometry.GeometryData.Vertex;

namespace Prowl.Vector.Geometry.Operators;

internal static class ExtractOp
{
    /// <summary>
    /// Copies part of a mesh into a new mesh: the vertices, edges and faces given, with the edges and corners of
    /// those faces and the ends of those edges, and all their attributes. The mesh itself is left as it was.
    /// </summary>
    internal static GeometryData Extract(GeometryData mesh, IEnumerable<Vertex> vertices, IEnumerable<Edge> edges, IEnumerable<Face> faces)
    {
        var result = new GeometryData();
        foreach (var a in mesh.VertexAttributes) result.AddVertexAttribute(a.Name, a.Type.BaseType, a.Type.Dimensions);
        foreach (var a in mesh.EdgeAttributes) result.AddEdgeAttribute(a.Name, a.Type.BaseType, a.Type.Dimensions);
        foreach (var a in mesh.LoopAttributes) result.AddLoopAttribute(a.Name, a.Type.BaseType, a.Type.Dimensions);
        foreach (var a in mesh.FaceAttributes) result.AddFaceAttribute(a.Name, a.Type.BaseType, a.Type.Dimensions);

        var wantedFaces = new HashSet<Face>(faces);
        var wantedEdges = new HashSet<Edge>(edges);
        var wantedVertices = new HashSet<Vertex>(vertices);
        foreach (Face face in wantedFaces)
            wantedEdges.UnionWith(face.NeighborEdges());
        foreach (Edge edge in wantedEdges)
        {
            wantedVertices.Add(edge.Vert1);
            wantedVertices.Add(edge.Vert2);
        }

        // Walking the mesh's own lists keeps the copies in the order the originals are in
        var map = new Dictionary<Vertex, Vertex>(wantedVertices.Count);
        foreach (Vertex vertex in mesh.Vertices)
        {
            if (!wantedVertices.Contains(vertex)) continue;
            Vertex copy = result.AddVertex(vertex.Point);
            AttributeTransfer.Apply(vertex.Attributes, copy.Attributes);
            map[vertex] = copy;
        }

        foreach (Edge edge in mesh.Edges)
        {
            if (!wantedEdges.Contains(edge)) continue;
            Edge copy = result.AddEdge(map[edge.Vert1], map[edge.Vert2]);
            AttributeTransfer.Apply(edge.Attributes, copy.Attributes);
        }

        foreach (Face face in mesh.Faces)
        {
            if (!wantedFaces.Contains(face)) continue;
            FaceSnapshot snapshot = FaceSnapshot.Capture(mesh, face);
            Face? copy = result.AddFace(snapshot.Vertices.ConvertAll(v => map[v]).ToArray());
            if (copy == null) continue;

            snapshot.ApplyFaceAttributes(copy);
            var original = snapshot.Vertices.ToDictionary(v => map[v]);
            FaceSnapshot.ApplyLoops(copy, v => snapshot.LoopOf(original[v]));
        }
        return result;
    }
}
