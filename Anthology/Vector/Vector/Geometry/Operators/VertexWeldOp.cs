// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry.Operators
{
    internal static class VertexWeldOp
    {
        internal static int WeldVertices(GeometryData mesh, double threshold = 0.0001)
        {
            if (threshold <= 0)
                return 0;

            var verticesToRemove = new HashSet<GeometryData.Vertex>();
            var vertexMapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();
            int weldedCount = 0;

            // Build a list of all vertices for spatial searching
            var allVertices = mesh.Vertices.ToList();

            // For each vertex, find nearby vertices and merge them
            for (int i = 0; i < allVertices.Count; i++)
            {
                var v1 = allVertices[i];

                // Skip if already marked for removal
                if (verticesToRemove.Contains(v1))
                    continue;

                // Find all vertices within threshold distance
                var cluster = new List<GeometryData.Vertex> { v1 };

                for (int j = i + 1; j < allVertices.Count; j++)
                {
                    var v2 = allVertices[j];

                    // Skip if already marked for removal
                    if (verticesToRemove.Contains(v2))
                        continue;

                    double distance = Double3.Distance(v1.Point, v2.Point);
                    if (distance <= threshold)
                    {
                        cluster.Add(v2);
                    }
                }

                // If we found vertices to weld
                if (cluster.Count > 1)
                {
                    // Calculate average position
                    Double3 avgPosition = Double3.Zero;
                    foreach (var v in cluster)
                    {
                        avgPosition += v.Point;
                    }
                    avgPosition /= cluster.Count;

                    // Update the first vertex to the average position
                    v1.Point = avgPosition;

                    // Average all attributes
                    foreach (var attr in mesh.VertexAttributes)
                    {
                        if (attr.Type.BaseType == GeometryData.AttributeBaseType.Float)
                        {
                            var floatAttrs = new List<GeometryData.FloatAttributeValue>();
                            foreach (var v in cluster)
                            {
                                if (v.Attributes.ContainsKey(attr.Name) &&
                                    v.Attributes[attr.Name] is GeometryData.FloatAttributeValue fval)
                                {
                                    floatAttrs.Add(fval);
                                }
                            }

                            if (floatAttrs.Count > 0)
                            {
                                int n = floatAttrs[0].Data.Length;
                                var avgData = new double[n];
                                for (int k = 0; k < n; k++)
                                {
                                    double sum = 0;
                                    foreach (var fval in floatAttrs)
                                    {
                                        if (k < fval.Data.Length)
                                            sum += fval.Data[k];
                                    }
                                    avgData[k] = sum / floatAttrs.Count;
                                }
                                v1.Attributes[attr.Name] = new GeometryData.FloatAttributeValue { Data = avgData };
                            }
                        }
                        else if (attr.Type.BaseType == GeometryData.AttributeBaseType.Int)
                        {
                            var intAttrs = new List<GeometryData.IntAttributeValue>();
                            foreach (var v in cluster)
                            {
                                if (v.Attributes.ContainsKey(attr.Name) &&
                                    v.Attributes[attr.Name] is GeometryData.IntAttributeValue ival)
                                {
                                    intAttrs.Add(ival);
                                }
                            }

                            if (intAttrs.Count > 0)
                            {
                                int n = intAttrs[0].Data.Length;
                                var avgData = new int[n];
                                for (int k = 0; k < n; k++)
                                {
                                    double sum = 0;
                                    foreach (var ival in intAttrs)
                                    {
                                        if (k < ival.Data.Length)
                                            sum += ival.Data[k];
                                    }
                                    avgData[k] = (int)Math.Round(sum / intAttrs.Count);
                                }
                                v1.Attributes[attr.Name] = new GeometryData.IntAttributeValue { Data = avgData };
                            }
                        }
                    }

                    // Map all other vertices in cluster to v1
                    for (int j = 1; j < cluster.Count; j++)
                    {
                        vertexMapping[cluster[j]] = v1;
                        verticesToRemove.Add(cluster[j]);
                        weldedCount++;
                    }
                }
            }

            if (weldedCount == 0)
                return 0;

            // Now we need to update all faces to use the welded vertices
            // Collect all face data before modification
            var faceData = new List<(List<GeometryData.Vertex> verts,
                Dictionary<string, GeometryData.AttributeValue> faceAttrs,
                List<Dictionary<string, GeometryData.AttributeValue>> loopAttrs)>();

            foreach (var face in mesh.Faces)
            {
                var verts = face.NeighborVertices();

                // Check if any vertices need to be remapped
                bool needsUpdate = false;
                foreach (var v in verts)
                {
                    if (vertexMapping.ContainsKey(v))
                    {
                        needsUpdate = true;
                        break;
                    }
                }

                if (!needsUpdate)
                    continue;

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

                faceData.Add((verts, faceAttrs, loopAttrs));
            }

            // Remove all faces that need updating
            var facesToRemove = new HashSet<GeometryData.Face>();
            foreach (var face in mesh.Faces)
            {
                var verts = face.NeighborVertices();
                foreach (var v in verts)
                {
                    if (vertexMapping.ContainsKey(v))
                    {
                        facesToRemove.Add(face);
                        break;
                    }
                }
            }

            foreach (var face in facesToRemove)
            {
                mesh.RemoveFace(face);
            }

            // Recreate faces with welded vertices
            foreach (var (verts, faceAttrs, loopAttrs) in faceData)
            {
                // Remap vertices
                var newVerts = new List<GeometryData.Vertex>();
                for (int i = 0; i < verts.Count; i++)
                {
                    var v = verts[i];
                    var mappedV = vertexMapping.ContainsKey(v) ? vertexMapping[v] : v;

                    // Skip duplicate consecutive vertices (can happen after welding)
                    if (newVerts.Count == 0 || newVerts[newVerts.Count - 1] != mappedV)
                    {
                        newVerts.Add(mappedV);
                    }
                }

                // Check for first/last duplicate
                if (newVerts.Count > 1 && newVerts[0] == newVerts[newVerts.Count - 1])
                {
                    newVerts.RemoveAt(newVerts.Count - 1);
                }

                // Only create face if it has at least 3 unique vertices
                if (newVerts.Count >= 3)
                {
                    var newFace = mesh.AddFace(newVerts.ToArray());
                    if (newFace != null)
                    {
                        // Restore face attributes
                        foreach (var kvp in faceAttrs)
                        {
                            newFace.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                        }

                        // Restore loop attributes (may need adjustment if vertices were removed)
                        if (newFace.Loop != null && loopAttrs.Count > 0)
                        {
                            var it = newFace.Loop;
                            int loopIdx = 0;
                            do
                            {
                                // Try to find matching loop attribute from original
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

            // Remove welded vertices
            foreach (var v in verticesToRemove)
            {
                if (mesh.Vertices.Contains(v))
                    mesh.RemoveVertex(v);
            }

            return weldedCount;
        }

        internal static int WeldVerticesAtPositions(GeometryData mesh, IEnumerable<Double3> targetPositions, double threshold)
        {
            if (threshold <= 0)
                return 0;

            var allVertices = mesh.Vertices.ToList();
            var verticesToWeld = new HashSet<GeometryData.Vertex>();

            // Find all vertices near target positions
            foreach (var targetPos in targetPositions)
            {
                foreach (var v in allVertices)
                {
                    if (Double3.Distance(v.Point, targetPos) <= threshold)
                    {
                        verticesToWeld.Add(v);
                    }
                }
            }

            if (verticesToWeld.Count == 0)
                return 0;

            // Create a temporary mesh with only the vertices to weld
            // and use the main WeldVertices method
            var originalCount = mesh.Vertices.Count;

            // We need to weld within the subset, so we'll do a targeted approach
            var vertexMapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();
            var verticesToRemove = new HashSet<GeometryData.Vertex>();
            var vertexList = verticesToWeld.ToList();
            int weldedCount = 0;

            for (int i = 0; i < vertexList.Count; i++)
            {
                var v1 = vertexList[i];
                if (verticesToRemove.Contains(v1))
                    continue;

                var cluster = new List<GeometryData.Vertex> { v1 };

                for (int j = i + 1; j < vertexList.Count; j++)
                {
                    var v2 = vertexList[j];
                    if (verticesToRemove.Contains(v2))
                        continue;

                    if (Double3.Distance(v1.Point, v2.Point) <= threshold)
                    {
                        cluster.Add(v2);
                    }
                }

                if (cluster.Count > 1)
                {
                    // Average position
                    Double3 avgPos = Double3.Zero;
                    foreach (var v in cluster)
                        avgPos += v.Point;
                    avgPos /= cluster.Count;
                    v1.Point = avgPos;

                    // Map others to v1
                    for (int j = 1; j < cluster.Count; j++)
                    {
                        vertexMapping[cluster[j]] = v1;
                        verticesToRemove.Add(cluster[j]);
                        weldedCount++;
                    }
                }
            }

            if (weldedCount == 0)
                return 0;

            // Update faces (same logic as WeldVertices)
            var faceData = new List<(List<GeometryData.Vertex> verts,
                Dictionary<string, GeometryData.AttributeValue> faceAttrs,
                List<Dictionary<string, GeometryData.AttributeValue>> loopAttrs)>();

            foreach (var face in mesh.Faces)
            {
                var verts = face.NeighborVertices();
                bool needsUpdate = verts.Any(v => vertexMapping.ContainsKey(v));

                if (!needsUpdate)
                    continue;

                var faceAttrs = new Dictionary<string, GeometryData.AttributeValue>();
                foreach (var attr in mesh.FaceAttributes)
                {
                    if (face.Attributes.ContainsKey(attr.Name))
                        faceAttrs[attr.Name] = GeometryData.AttributeValue.Copy(face.Attributes[attr.Name]);
                }

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

            var facesToRemove = new HashSet<GeometryData.Face>();
            foreach (var face in mesh.Faces)
            {
                var verts = face.NeighborVertices();
                if (verts.Any(v => vertexMapping.ContainsKey(v)))
                    facesToRemove.Add(face);
            }

            foreach (var face in facesToRemove)
                mesh.RemoveFace(face);

            foreach (var (verts, faceAttrs, loopAttrs) in faceData)
            {
                var newVerts = new List<GeometryData.Vertex>();
                for (int i = 0; i < verts.Count; i++)
                {
                    var v = verts[i];
                    var mappedV = vertexMapping.ContainsKey(v) ? vertexMapping[v] : v;

                    if (newVerts.Count == 0 || newVerts[newVerts.Count - 1] != mappedV)
                        newVerts.Add(mappedV);
                }

                if (newVerts.Count > 1 && newVerts[0] == newVerts[newVerts.Count - 1])
                    newVerts.RemoveAt(newVerts.Count - 1);

                if (newVerts.Count >= 3)
                {
                    var newFace = mesh.AddFace(newVerts.ToArray());
                    if (newFace != null)
                    {
                        foreach (var kvp in faceAttrs)
                            newFace.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);

                        if (newFace.Loop != null && loopAttrs.Count > 0)
                        {
                            var it = newFace.Loop;
                            int loopIdx = 0;
                            do
                            {
                                if (loopIdx < loopAttrs.Count)
                                {
                                    foreach (var kvp in loopAttrs[loopIdx])
                                        it.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                                }
                                it = it.Next;
                                loopIdx++;
                            } while (it != newFace.Loop);
                        }
                    }
                }
            }

            foreach (var v in verticesToRemove)
            {
                if (mesh.Vertices.Contains(v))
                    mesh.RemoveVertex(v);
            }

            return weldedCount;
        }
    }
}
