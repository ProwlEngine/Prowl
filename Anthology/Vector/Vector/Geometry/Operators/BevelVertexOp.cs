// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry.Operators
{
    internal static class BevelVertexOp
    {
        internal static void BevelVertices(GeometryData mesh, IEnumerable<GeometryData.Vertex> verticesToBevel, double offset = 0.3)
        {
            if (offset <= 0.0 || offset >= 1.0)
                throw new ArgumentException("Offset must be between 0.0 and 1.0", nameof(offset));

            verticesToBevel = verticesToBevel.Where(v => mesh.Vertices.Contains(v));
            if (verticesToBevel.Any() == false)
                return;

            var bevelSet = new HashSet<GeometryData.Vertex>(verticesToBevel);

            // For each vertex, collect data about edges and faces
            var vertexData = new Dictionary<GeometryData.Vertex, (
                List<GeometryData.Edge> edges,
                List<GeometryData.Face> faces,
                Dictionary<GeometryData.Vertex, GeometryData.Vertex> otherVertToNewVertex,
                List<GeometryData.Vertex> newVertices
            )>();

            // First pass: create new vertices for each beveled vertex
            foreach (var vertex in verticesToBevel)
            {
                var connectedEdges = vertex.NeighborEdges().ToList();
                if (connectedEdges.Count < 2)
                    continue;

                var connectedFaces = vertex.NeighborFaces().ToList();
                if (connectedFaces.Count == 0)
                    continue;

                var orderedEdges = OrderEdgesAroundVertex(vertex, connectedEdges, connectedFaces);
                var newVertices = new List<GeometryData.Vertex>();
                var otherVertToNewVertex = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

                foreach (var edge in orderedEdges)
                {
                    var otherVertex = edge.OtherVertex(vertex);

                    // Create new vertex at offset distance from the vertex being beveled
                    Double3 newPos = Maths.Lerp(vertex.Point, otherVertex.Point, offset);
                    var newVert = mesh.AddVertex(newPos);
                    GeometryOperators.AttributeLerp(mesh, newVert, vertex, otherVertex, offset);

                    newVertices.Add(newVert);
                    otherVertToNewVertex[otherVertex] = newVert;
                }

                vertexData[vertex] = (connectedEdges, connectedFaces, otherVertToNewVertex, newVertices);
            }

            // Second pass: collect all affected faces
            var allFaceUpdateData = new List<(GeometryData.Face face,
                Dictionary<string, GeometryData.AttributeValue> faceAttrs,
                List<GeometryData.Vertex> verts,
                List<Dictionary<string, GeometryData.AttributeValue>> loopAttrs)>();

            var processedFaces = new HashSet<GeometryData.Face>();

            foreach (var vertex in verticesToBevel)
            {
                if (!vertexData.ContainsKey(vertex))
                    continue;

                var (_, connectedFaces, _, _) = vertexData[vertex];

                foreach (var face in connectedFaces)
                {
                    if (processedFaces.Contains(face))
                        continue;
                    processedFaces.Add(face);

                    var verts = face.NeighborVertices();

                    // Store face attributes
                    var faceAttrs = new Dictionary<string, GeometryData.AttributeValue>();
                    foreach (var attr in mesh.FaceAttributes)
                    {
                        if (face.Attributes.ContainsKey(attr.Name))
                            faceAttrs[attr.Name] = GeometryData.AttributeValue.Copy(face.Attributes[attr.Name]);
                    }

                    // Store loop attributes
                    var loopAttrs = new List<Dictionary<string, GeometryData.AttributeValue>>();
                    if (face.Loop != null)
                    {
                        var it = face.Loop;
                        do
                        {
                            var attrs = new Dictionary<string, GeometryData.AttributeValue>();
                            foreach (var attr in mesh.LoopAttributes)
                            {
                                if (it.Attributes.ContainsKey(attr.Name))
                                    attrs[attr.Name] = GeometryData.AttributeValue.Copy(it.Attributes[attr.Name]);
                            }
                            loopAttrs.Add(attrs);
                            it = it.Next;
                        } while (it != face.Loop);
                    }

                    allFaceUpdateData.Add((face, faceAttrs, verts, loopAttrs));
                }
            }

            // Remove all affected faces
            foreach (var (face, _, _, _) in allFaceUpdateData)
            {
                mesh.RemoveFace(face);
            }

            // Remove all edges connected to beveled vertices
            var edgesToRemove = new HashSet<GeometryData.Edge>();
            foreach (var vertex in verticesToBevel)
            {
                if (!vertexData.ContainsKey(vertex))
                    continue;
                var (connectedEdges, _, _, _) = vertexData[vertex];
                foreach (var edge in connectedEdges)
                {
                    edgesToRemove.Add(edge);
                }
            }
            foreach (var edge in edgesToRemove)
            {
                mesh.RemoveEdge(edge);
            }

            // Create bevel faces for each beveled vertex
            foreach (var vertex in verticesToBevel)
            {
                if (!vertexData.ContainsKey(vertex))
                    continue;

                var (_, connectedFaces, _, newVertices) = vertexData[vertex];

                if (newVertices.Count >= 3)
                {
                    // Calculate face normal to determine correct winding order
                    // Use average of connected face normals
                    Double3 avgNormal = Double3.Zero;
                    foreach (var face in connectedFaces)
                    {
                        var faceVerts = face.NeighborVertices();
                        if (faceVerts.Count >= 3)
                        {
                            var v0 = faceVerts[0].Point;
                            var v1 = faceVerts[1].Point;
                            var v2 = faceVerts[2].Point;
                            avgNormal += Double3.Normalize(Double3.Cross(v1 - v0, v2 - v0));
                        }
                    }
                    if (avgNormal != Double3.Zero)
                        avgNormal = Double3.Normalize(avgNormal);

                    // Calculate the normal of the bevel face as currently ordered
                    var bevelV0 = newVertices[0].Point;
                    var bevelV1 = newVertices[1].Point;
                    var bevelV2 = newVertices[2].Point;
                    var bevelNormal = Double3.Normalize(Double3.Cross(bevelV1 - bevelV0, bevelV2 - bevelV0));

                    // If the normals are in opposite directions, reverse the vertex order
                    if (Double3.Dot(bevelNormal, avgNormal) < 0)
                    {
                        newVertices.Reverse();
                    }

                    mesh.AddFace(newVertices.ToArray());
                }
            }

            // Recreate faces with updated vertices
            foreach (var (_, faceAttrs, verts, loopAttrs) in allFaceUpdateData)
            {
                var newFaceVerts = new List<GeometryData.Vertex>();
                bool faceIsValid = true;

                for (int i = 0; i < verts.Count; i++)
                {
                    var currentVert = verts[i];

                    if (bevelSet.Contains(currentVert) && vertexData.ContainsKey(currentVert))
                    {
                        // This vertex was beveled, replace it with appropriate new vertices
                        int prevIdx = (i - 1 + verts.Count) % verts.Count;
                        int nextIdx = (i + 1) % verts.Count;

                        var prevVert = verts[prevIdx];
                        var nextVert = verts[nextIdx];

                        var (_, _, otherVertToNewVertex, _) = vertexData[currentVert];

                        // Find the new vertices along edges to prev and next vertices
                        // These should be in our mapping since we created them from this vertex
                        GeometryData.Vertex? newVertFromPrev = null;
                        GeometryData.Vertex? newVertToNext = null;

                        if (otherVertToNewVertex.ContainsKey(prevVert))
                            newVertFromPrev = otherVertToNewVertex[prevVert];
                        if (otherVertToNewVertex.ContainsKey(nextVert))
                            newVertToNext = otherVertToNewVertex[nextVert];

                        // If we didn't find both vertices, mark face as invalid
                        if (newVertFromPrev == null || newVertToNext == null)
                        {
                            faceIsValid = false;
                            break;
                        }

                        // Add the new vertices in the correct order
                        newFaceVerts.Add(newVertFromPrev);
                        if (newVertToNext != newVertFromPrev)
                            newFaceVerts.Add(newVertToNext);
                    }
                    else
                    {
                        // This vertex was not beveled, keep it
                        newFaceVerts.Add(currentVert);
                    }
                }

                // Skip this face if it's invalid
                if (!faceIsValid)
                    continue;

                // Clean up: remove consecutive duplicate vertices
                var cleanedVerts = new List<GeometryData.Vertex>();
                for (int i = 0; i < newFaceVerts.Count; i++)
                {
                    var current = newFaceVerts[i];
                    var next = newFaceVerts[(i + 1) % newFaceVerts.Count];

                    // Only add if it's not the same as the next vertex
                    if (current != next)
                    {
                        cleanedVerts.Add(current);
                    }
                }

                // Create the updated face
                if (cleanedVerts.Count >= 3)
                {
                    var newFace = mesh.AddFace(cleanedVerts.ToArray());
                    if (newFace != null)
                    {
                        // Restore face attributes
                        foreach (var kvp in faceAttrs)
                        {
                            newFace.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                        }

                        // Restore loop attributes
                        if (newFace.Loop != null && loopAttrs.Count > 0)
                        {
                            var it = newFace.Loop;
                            int loopIdx = 0;
                            do
                            {
                                if (loopIdx < loopAttrs.Count)
                                {
                                    foreach (var kvp in loopAttrs[loopIdx])
                                    {
                                        it.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                                    }
                                }
                                it = it.Next;
                                loopIdx++;
                            } while (it != newFace.Loop);
                        }
                    }
                }
            }

            // Finally, remove the original beveled vertices
            foreach (var vertex in verticesToBevel)
            {
                if (mesh.Vertices.Contains(vertex))
                    mesh.RemoveVertex(vertex);
            }
        }

        /// <summary>
        /// Order edges around a vertex based on face connectivity to maintain proper winding order.
        /// </summary>
        private static List<GeometryData.Edge> OrderEdgesAroundVertex(GeometryData.Vertex vertex,
            List<GeometryData.Edge> edges, List<GeometryData.Face> faces)
        {
            if (edges.Count <= 2)
                return edges;

            // Build a connectivity map: for each edge, find adjacent edges through faces
            var edgeNeighbors = new Dictionary<GeometryData.Edge, List<GeometryData.Edge>>();
            foreach (var edge in edges)
                edgeNeighbors[edge] = new List<GeometryData.Edge>();

            // For each face, find which edges share the vertex and mark them as neighbors
            foreach (var face in faces)
            {
                var faceEdges = edges.Where(e => face.NeighborEdges().Contains(e)).ToList();

                // In a face, edges sharing a vertex are consecutive
                for (int i = 0; i < faceEdges.Count; i++)
                {
                    for (int j = i + 1; j < faceEdges.Count; j++)
                    {
                        if (!edgeNeighbors[faceEdges[i]].Contains(faceEdges[j]))
                            edgeNeighbors[faceEdges[i]].Add(faceEdges[j]);
                        if (!edgeNeighbors[faceEdges[j]].Contains(faceEdges[i]))
                            edgeNeighbors[faceEdges[j]].Add(faceEdges[i]);
                    }
                }
            }

            // Build ordered list by following neighbors
            var ordered = new List<GeometryData.Edge>();
            var visited = new HashSet<GeometryData.Edge>();

            var currentEdge = edges[0];
            ordered.Add(currentEdge);
            visited.Add(currentEdge);

            while (ordered.Count < edges.Count)
            {
                // Find an unvisited neighbor
                GeometryData.Edge? nextEdge = null;
                if (edgeNeighbors.ContainsKey(currentEdge))
                {
                    foreach (var neighbor in edgeNeighbors[currentEdge])
                    {
                        if (!visited.Contains(neighbor))
                        {
                            nextEdge = neighbor;
                            break;
                        }
                    }
                }

                if (nextEdge == null)
                {
                    // No connected unvisited edge, find any unvisited edge
                    nextEdge = edges.FirstOrDefault(e => !visited.Contains(e));
                    if (nextEdge == null)
                        break;
                }

                ordered.Add(nextEdge);
                visited.Add(nextEdge);
                currentEdge = nextEdge;
            }

            return ordered;
        }
    }
}
