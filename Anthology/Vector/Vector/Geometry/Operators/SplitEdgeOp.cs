// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Prowl.Vector.Geometry.Operators
{
    internal static class SplitEdgeOp
    {
        internal static GeometryData.Vertex SplitEdge(GeometryData mesh, GeometryData.Edge edge,
            GeometryData.Vertex fromVertex, float factor, out GeometryData.Edge newEdge)
        {
            Debug.Assert(edge.ContainsVertex(fromVertex));
            Debug.Assert(factor >= 0.0f && factor <= 1.0f);

            GeometryData.Vertex otherVertex = edge.OtherVertex(fromVertex);

            // Create new vertex at interpolated position
            Float3 newPos = Maths.Lerp(fromVertex.Point, otherVertex.Point, factor);
            GeometryData.Vertex newVert = mesh.AddVertex(newPos);

            // Interpolate all vertex attributes
            GeometryOperators.AttributeLerp(mesh, newVert, fromVertex, otherVertex, factor);

            // Collect all faces that use this edge (we need to update their loops)
            var facesToUpdate = edge.NeighborFaces();

            // Create new edge from newVert to otherVertex
            // First, we need to remove the old edge and replace it with two new edges
            GeometryData.Edge edgeToOther = mesh.AddEdge(newVert, otherVertex);

            // The original edge now goes from fromVertex to newVert
            // We need to update the edge's second vertex
            // However, since Edge is immutable in terms of its vertices, we need to:
            // 1. Store information about all loops using this edge
            // 2. Remove all faces
            // 3. Recreate them with the new edge configuration

            // Store face vertex sequences and attributes
            var faceData = new List<(List<GeometryData.Vertex> verts, Dictionary<string, GeometryData.AttributeValue> faceAttrs, List<Dictionary<string, GeometryData.AttributeValue>> loopAttrs)>();

            foreach (var face in facesToUpdate)
            {
                var verts = face.NeighborVertices();

                // Store face attributes
                var faceAttrs = new Dictionary<string, GeometryData.AttributeValue>();
                foreach (var attr in mesh.FaceAttributes)
                {
                    if (face.Attributes.ContainsKey(attr.Name))
                        faceAttrs[attr.Name] = GeometryData.AttributeValue.Copy(face.Attributes[attr.Name]);
                }

                // Store loop attributes in order
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

                faceData.Add((verts, faceAttrs, loopAttrs));
            }

            // Remove old faces and edge
            foreach (var face in facesToUpdate.ToArray())
            {
                mesh.RemoveFace(face);
            }
            mesh.RemoveEdge(edge);

            // Create edge from fromVertex to newVert
            GeometryData.Edge edgeFromNew = mesh.AddEdge(fromVertex, newVert);

            // Recreate faces with the split edge
            foreach (var (verts, faceAttrs, loopAttrs) in faceData)
            {
                // Find where the old edge was and insert the new vertex
                var newVerts = new List<GeometryData.Vertex>();
                for (int i = 0; i < verts.Count; i++)
                {
                    newVerts.Add(verts[i]);

                    // Check if this edge was the one we split
                    int nextIdx = (i + 1) % verts.Count;
                    if ((verts[i] == fromVertex && verts[nextIdx] == otherVertex) ||
                        (verts[i] == otherVertex && verts[nextIdx] == fromVertex))
                    {
                        // Insert the new vertex between current and next
                        // Works correctly for both edge directions:
                        // fromVertex -> otherVertex: adds fromVertex, newVert, then otherVertex in next iteration
                        // otherVertex -> fromVertex: adds otherVertex, newVert, then fromVertex in next iteration
                        newVerts.Add(newVert);
                    }
                }

                // Create the new face
                var newFace = mesh.AddFace(newVerts.ToArray());
                if (newFace != null)
                {
                    // Restore face attributes
                    foreach (var kvp in faceAttrs)
                    {
                        newFace.Attributes[kvp.Key] = kvp.Value;
                    }

                    // Restore loop attributes (need to handle the new loop for newVert)
                    if (newFace.Loop != null && loopAttrs.Count > 0)
                    {
                        var it = newFace.Loop;
                        int loopIdx = 0;
                        int oldVertIdx = 0;

                        do
                        {
                            // Map back to original loop attributes
                            // Skip the new vertex's loop when indexing into old attributes
                            if (it.Vert != newVert)
                            {
                                if (oldVertIdx < loopAttrs.Count)
                                {
                                    foreach (var kvp in loopAttrs[oldVertIdx])
                                    {
                                        it.Attributes[kvp.Key] = kvp.Value;
                                    }
                                }
                                oldVertIdx++;
                            }
                            else
                            {
                                // Interpolate attributes for the new vertex's loop
                                // Find the loops before and after
                                var prevLoop = it.Prev;
                                var nextLoop = it.Next;

                                if (prevLoop != null && nextLoop != null)
                                {
                                    foreach (var attr in mesh.LoopAttributes)
                                    {
                                        if (prevLoop.Attributes.ContainsKey(attr.Name) &&
                                            nextLoop.Attributes.ContainsKey(attr.Name))
                                        {
                                            var val1 = prevLoop.Attributes[attr.Name];
                                            var val2 = nextLoop.Attributes[attr.Name];

                                            // Interpolate loop attributes
                                            if (val1 is GeometryData.FloatAttributeValue fval1 &&
                                                val2 is GeometryData.FloatAttributeValue fval2)
                                            {
                                                int n = fval1.Data.Length;
                                                var val = new GeometryData.FloatAttributeValue { Data = new float[n] };
                                                for (int i = 0; i < n; ++i)
                                                {
                                                    val.Data[i] = Maths.Lerp(fval1.Data[i], fval2.Data[i], factor);
                                                }
                                                it.Attributes[attr.Name] = val;
                                            }
                                            else if (val1 is GeometryData.IntAttributeValue ival1 &&
                                                     val2 is GeometryData.IntAttributeValue ival2)
                                            {
                                                int n = ival1.Data.Length;
                                                var val = new GeometryData.IntAttributeValue { Data = new int[n] };
                                                for (int i = 0; i < n; ++i)
                                                {
                                                    val.Data[i] = (int)Math.Round(Maths.Lerp(ival1.Data[i], ival2.Data[i], factor));
                                                }
                                                it.Attributes[attr.Name] = val;
                                            }
                                        }
                                    }
                                }
                            }

                            it = it.Next;
                            loopIdx++;
                        } while (it != newFace.Loop);
                    }
                }
            }

            newEdge = edgeToOther;
            return newVert;
        }
    }
}
