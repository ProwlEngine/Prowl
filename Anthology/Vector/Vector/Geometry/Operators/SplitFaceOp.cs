// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Diagnostics;

namespace Prowl.Vector.Geometry.Operators
{
    internal static class SplitFaceOp
    {
        internal static GeometryData.Face? SplitFace(GeometryData mesh, GeometryData.Face face, GeometryData.Vertex vert1, GeometryData.Vertex vert2, out GeometryData.Edge? newEdge)
        {
            newEdge = null;

            // Validate inputs
            if (vert1 == vert2)
            {
                Debug.WriteLine("SplitFace: vertices must be different");
                return null;
            }

            // Get the loops for these vertices in the face
            var loop1 = face.GetLoop(vert1);
            var loop2 = face.GetLoop(vert2);

            if (loop1 == null || loop2 == null)
            {
                Debug.WriteLine("SplitFace: one or both vertices not found in face");
                return null;
            }

            // Check if vertices are adjacent (can't split along an existing edge)
            if (loop1.Next == loop2 || loop1.Prev == loop2)
            {
                Debug.WriteLine("SplitFace: vertices are adjacent in the face");
                return null;
            }

            // Collect vertices for the two new faces
            var face1Verts = new List<GeometryData.Vertex>();
            var face2Verts = new List<GeometryData.Vertex>();

            // Collect loop attributes for both faces
            var face1LoopAttrs = new List<Dictionary<string, GeometryData.AttributeValue>>();
            var face2LoopAttrs = new List<Dictionary<string, GeometryData.AttributeValue>>();

            // Walk from vert1 to vert2 for face1
            var it = loop1;
            do
            {
                face1Verts.Add(it.Vert);

                // Copy loop attributes
                var attrs = new Dictionary<string, GeometryData.AttributeValue>();
                foreach (var attr in mesh.LoopAttributes)
                {
                    if (it.Attributes.ContainsKey(attr.Name))
                        attrs[attr.Name] = GeometryData.AttributeValue.Copy(it.Attributes[attr.Name]);
                }
                face1LoopAttrs.Add(attrs);

                if (it == loop2) break;
                it = it.Next;
            } while (it != loop1);

            // Walk from vert2 to vert1 for face2
            it = loop2;
            do
            {
                face2Verts.Add(it.Vert);

                // Copy loop attributes
                var attrs = new Dictionary<string, GeometryData.AttributeValue>();
                foreach (var attr in mesh.LoopAttributes)
                {
                    if (it.Attributes.ContainsKey(attr.Name))
                        attrs[attr.Name] = GeometryData.AttributeValue.Copy(it.Attributes[attr.Name]);
                }
                face2LoopAttrs.Add(attrs);

                if (it == loop1) break;
                it = it.Next;
            } while (it != loop2);

            // Store original face attributes
            var faceAttrs = new Dictionary<string, GeometryData.AttributeValue>();
            foreach (var attr in mesh.FaceAttributes)
            {
                if (face.Attributes.ContainsKey(attr.Name))
                    faceAttrs[attr.Name] = GeometryData.AttributeValue.Copy(face.Attributes[attr.Name]);
            }

            // Remove the original face
            mesh.RemoveFace(face);

            // Create the splitting edge (or get existing edge if it already exists)
            var splitEdge = mesh.AddEdge(vert1, vert2);

            // Verify the edge was created
            if (splitEdge == null)
            {
                Debug.WriteLine("SplitFace: Failed to create splitting edge");
                return null;
            }

            // Create the two new faces
            var newFace1 = mesh.AddFace(face1Verts.ToArray());
            var newFace2 = mesh.AddFace(face2Verts.ToArray());

            // Verify both faces were created
            if (newFace1 == null || newFace2 == null)
            {
                Debug.WriteLine($"SplitFace: Failed to create faces (face1={newFace1 != null}, face2={newFace2 != null})");
                return null;
            }

            // Restore attributes on both faces
            if (newFace1 != null)
            {
                foreach (var kvp in faceAttrs)
                {
                    newFace1.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                }

                // Restore loop attributes for face1
                if (newFace1.Loop != null)
                {
                    var loopIt = newFace1.Loop;
                    int idx = 0;
                    do
                    {
                        if (idx < face1LoopAttrs.Count)
                        {
                            foreach (var kvp in face1LoopAttrs[idx])
                            {
                                loopIt.Attributes[kvp.Key] = kvp.Value;
                            }
                        }
                        loopIt = loopIt.Next;
                        idx++;
                    } while (loopIt != newFace1.Loop);
                }
            }

            if (newFace2 != null)
            {
                foreach (var kvp in faceAttrs)
                {
                    newFace2.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                }

                // Restore loop attributes for face2
                if (newFace2.Loop != null)
                {
                    var loopIt = newFace2.Loop;
                    int idx = 0;
                    do
                    {
                        if (idx < face2LoopAttrs.Count)
                        {
                            foreach (var kvp in face2LoopAttrs[idx])
                            {
                                loopIt.Attributes[kvp.Key] = kvp.Value;
                            }
                        }
                        loopIt = loopIt.Next;
                        idx++;
                    } while (loopIt != newFace2.Loop);
                }
            }

            newEdge = splitEdge;
            return newFace2;
        }
    }
}
