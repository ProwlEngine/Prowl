// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Static operators for manipulating GeometryData (BMesh-like) structures.
    /// All operations modify the mesh in-place. Inspired by Blender's BMesh operators.
    /// </summary>
    public static class GeometryOperators
    {
        #region Attribute Interpolation

        /// <summary>
        /// Set all attributes in destination vertex to attr[v1] * (1 - t) + attr[v2] * t.
        /// Linearly interpolates all vertex attributes between two vertices.
        /// </summary>
        /// <param name="mesh">The mesh containing the vertices.</param>
        /// <param name="destination">The vertex to write interpolated attributes to.</param>
        /// <param name="v1">First source vertex.</param>
        /// <param name="v2">Second source vertex.</param>
        /// <param name="t">Interpolation factor (0 = v1, 1 = v2).</param>
        public static void AttributeLerp(GeometryData mesh, GeometryData.Vertex destination,
            GeometryData.Vertex v1, GeometryData.Vertex v2, double t)
        {
            foreach (var attr in mesh.VertexAttributes)
            {
                if (!v1.Attributes.ContainsKey(attr.Name) || !v2.Attributes.ContainsKey(attr.Name))
                    continue;

                switch (attr.Type.BaseType)
                {
                    case GeometryData.AttributeBaseType.Float:
                        {
                            var val1 = v1.Attributes[attr.Name] as GeometryData.FloatAttributeValue;
                            var val2 = v2.Attributes[attr.Name] as GeometryData.FloatAttributeValue;
                            if (val1 == null || val2 == null) break;

                            int n = val1.Data.Length;
                            Debug.Assert(val2.Data.Length == n);
                            var val = new GeometryData.FloatAttributeValue { Data = new double[n] };
                            for (int i = 0; i < n; ++i)
                            {
                                val.Data[i] = Maths.Lerp(val1.Data[i], val2.Data[i], t);
                            }
                            destination.Attributes[attr.Name] = val;
                            break;
                        }
                    case GeometryData.AttributeBaseType.Int:
                        {
                            var val1 = v1.Attributes[attr.Name] as GeometryData.IntAttributeValue;
                            var val2 = v2.Attributes[attr.Name] as GeometryData.IntAttributeValue;
                            if (val1 == null || val2 == null) break;

                            int n = val1.Data.Length;
                            Debug.Assert(val2.Data.Length == n);
                            var val = new GeometryData.IntAttributeValue { Data = new int[n] };
                            for (int i = 0; i < n; ++i)
                            {
                                val.Data[i] = (int)Math.Round(Maths.Lerp(val1.Data[i], val2.Data[i], t));
                            }
                            destination.Attributes[attr.Name] = val;
                            break;
                        }
                }
            }
        }

        #endregion

        #region Subdivision

        /// <summary>
        /// Subdivide all faces in the mesh without smoothing.
        /// After subdivision, all faces are quads. Attempts to interpolate all attributes.
        /// Note: Modifies edge IDs during operation.
        /// </summary>
        /// <param name="mesh">The mesh to subdivide.</param>
        public static void Subdivide(GeometryData mesh)
        {
            int i = 0;
            var edgeCenters = new GeometryData.Vertex[mesh.Edges.Count];
            var originalEdges = new GeometryData.Edge[mesh.Edges.Count];

            // Create vertex at each edge center
            foreach (var e in mesh.Edges)
            {
                edgeCenters[i] = mesh.AddVertex(e.Center());
                AttributeLerp(mesh, edgeCenters[i], e.Vert1, e.Vert2, 0.5);
                originalEdges[i] = e;
                e.Id = i++;
            }

            var originalFaces = new List<GeometryData.Face>(mesh.Faces);
            foreach (var f in originalFaces)
            {
                var faceCenter = mesh.AddVertex(f.Center());
                double w = 0;

                // Create one quad per loop in the original face
                var it = f.Loop;
                if (it == null) continue;

                do
                {
                    w += 1;
                    AttributeLerp(mesh, faceCenter, faceCenter, it.Vert, 1.0 / w);

                    var quad = new GeometryData.Vertex[]
                    {
                        it.Vert,
                        edgeCenters[it.Edge.Id],
                        faceCenter,
                        edgeCenters[it.Prev!.Edge.Id]
                    };
                    mesh.AddFace(quad);
                    it = it.Next;
                } while (it != f.Loop);

                // Remove the original face
                mesh.RemoveFace(f);
            }

            // Remove old edges
            foreach (var e in originalEdges)
            {
                mesh.RemoveEdge(e);
            }
        }

        #endregion

        #region Quad Squarification

        /// <summary>
        /// Compute a local coordinate system for a quad face.
        /// </summary>
        private static Double4x4 ComputeLocalAxis(Double3 r0, Double3 r1, Double3 r2, Double3 r3)
        {
            Double3 Z = Double3.Normalize(
                Double3.Normalize(Double3.Cross(r0, r1)) +
                Double3.Normalize(Double3.Cross(r1, r2)) +
                Double3.Normalize(Double3.Cross(r2, r3)) +
                Double3.Normalize(Double3.Cross(r3, r0))
            );
            Double3 X = Double3.Normalize(r0);
            Double3 Y = Double3.Cross(Z, X);

            // Build transformation matrix
            return new Double4x4(
                new Double4(X.X, X.Y, X.Z, 0),
                new Double4(Y.X, Y.Y, Y.Z, 0),
                new Double4(Z.X, Z.Y, Z.Z, 0),
                new Double4(0, 0, 0, 1)
            );
        }

        /// <summary>
        /// Calculate average radius length of all quads in the mesh.
        /// </summary>
        private static double AverageRadiusLength(GeometryData mesh)
        {
            double lengthSum = 0;
            double weightSum = 0;

            foreach (var f in mesh.Faces)
            {
                var verts = f.NeighborVertices();
                if (verts.Count != 4) continue;

                Double3 c = f.Center();
                Double3 r0 = verts[0].Point - c;
                Double3 r1 = verts[1].Point - c;
                Double3 r2 = verts[2].Point - c;
                Double3 r3 = verts[3].Point - c;

                var localToGlobal = ComputeLocalAxis(r0, r1, r2, r3);
                var globalToLocal = Double4x4.Transpose(localToGlobal);

                // Transform to local coordinates
                Double3 l0 = Double4x4.TransformPoint(r0, globalToLocal);
                Double3 l1 = Double4x4.TransformPoint(r1, globalToLocal);
                Double3 l2 = Double4x4.TransformPoint(r2, globalToLocal);
                Double3 l3 = Double4x4.TransformPoint(r3, globalToLocal);

                // Rotate vectors to align
                Double3 rl0 = l0;
                Double3 rl1 = new Double3(l1.Y, -l1.X, l1.Z);
                Double3 rl2 = new Double3(-l2.X, -l2.Y, l2.Z);
                Double3 rl3 = new Double3(-l3.Y, l3.X, l3.Z);

                Double3 average = (rl0 + rl1 + rl2 + rl3) * 0.25;

                lengthSum += Double3.Length(average);
                weightSum += 1;
            }

            return weightSum > 0 ? lengthSum / weightSum : 1.0;
        }

        /// <summary>
        /// Try to make quad faces as square as possible. This assumes the mesh is only made of quads.
        /// Can be called iteratively for better results.
        /// </summary>
        /// <param name="mesh">The mesh to squarify (must contain only quad faces).</param>
        /// <param name="rate">Speed at which faces are squarified. Higher is faster but may overshoot.</param>
        /// <param name="uniformLength">Whether to uniformize the size of all quads.</param>
        public static void SquarifyQuads(GeometryData mesh, double rate = 1.0, bool uniformLength = false)
        {
            double avg = uniformLength ? AverageRadiusLength(mesh) : 0;

            var pointUpdates = new Double3[mesh.Vertices.Count];
            var weights = new double[mesh.Vertices.Count];

            // Initialize with rest positions if available
            int i = 0;
            foreach (var v in mesh.Vertices)
            {
                if (mesh.HasVertexAttribute("restpos"))
                {
                    weights[i] = mesh.HasVertexAttribute("weight")
                        ? (v.Attributes["weight"] as GeometryData.FloatAttributeValue)?.Data[0] ?? 1.0
                        : 1.0;

                    var restpos = (v.Attributes["restpos"] as GeometryData.FloatAttributeValue)?.AsVector3() ?? v.Point;
                    pointUpdates[i] = (restpos - v.Point) * weights[i];
                }
                else
                {
                    pointUpdates[i] = Double3.Zero;
                    weights[i] = 0;
                }
                v.Id = i++;
            }

            // Accumulate squarification updates
            foreach (var f in mesh.Faces)
            {
                var verts = f.NeighborVertices();
                if (verts.Count != 4) continue;

                Double3 c = f.Center();
                Double3 r0 = verts[0].Point - c;
                Double3 r1 = verts[1].Point - c;
                Double3 r2 = verts[2].Point - c;
                Double3 r3 = verts[3].Point - c;

                var localToGlobal = ComputeLocalAxis(r0, r1, r2, r3);
                var globalToLocal = Double4x4.Transpose(localToGlobal);

                // Transform to local coordinates
                Double3 l0 = Double4x4.TransformPoint(r0, globalToLocal);
                Double3 l1 = Double4x4.TransformPoint(r1, globalToLocal);
                Double3 l2 = Double4x4.TransformPoint(r2, globalToLocal);
                Double3 l3 = Double4x4.TransformPoint(r3, globalToLocal);

                // Ensure proper winding order
                bool switch03 = false;
                if (Double3.Normalize(l1).Y < Double3.Normalize(l3).Y)
                {
                    switch03 = true;
                    var tmp = l3;
                    l3 = l1;
                    l1 = tmp;
                }

                // Rotate vectors to align
                Double3 rl0 = l0;
                Double3 rl1 = new Double3(l1.Y, -l1.X, l1.Z);
                Double3 rl2 = new Double3(-l2.X, -l2.Y, l2.Z);
                Double3 rl3 = new Double3(-l3.Y, l3.X, l3.Z);

                Double3 average = (rl0 + rl1 + rl2 + rl3) * 0.25;
                if (uniformLength)
                {
                    average = Double3.Normalize(average) * avg;
                }

                // Rotate back to get target positions
                Double3 lt0 = average;
                Double3 lt1 = new Double3(-average.Y, average.X, average.Z);
                Double3 lt2 = new Double3(-average.X, -average.Y, average.Z);
                Double3 lt3 = new Double3(average.Y, -average.X, average.Z);

                if (switch03)
                {
                    var tmp = lt3;
                    lt3 = lt1;
                    lt1 = tmp;
                }

                // Transform back to global coordinates
                Double3 t0 = Double4x4.TransformPoint(lt0, localToGlobal);
                Double3 t1 = Double4x4.TransformPoint(lt1, localToGlobal);
                Double3 t2 = Double4x4.TransformPoint(lt2, localToGlobal);
                Double3 t3 = Double4x4.TransformPoint(lt3, localToGlobal);

                // Accumulate updates
                pointUpdates[verts[0].Id] += t0 - r0;
                pointUpdates[verts[1].Id] += t1 - r1;
                pointUpdates[verts[2].Id] += t2 - r2;
                pointUpdates[verts[3].Id] += t3 - r3;
                weights[verts[0].Id] += 1;
                weights[verts[1].Id] += 1;
                weights[verts[2].Id] += 1;
                weights[verts[3].Id] += 1;
            }

            // Apply accumulated updates
            i = 0;
            foreach (var v in mesh.Vertices)
            {
                if (weights[i] > 0)
                {
                    v.Point += pointUpdates[i] * (rate / weights[i]);
                }
                ++i;
            }
        }

        #endregion

        #region Mesh Merging

        /// <summary>
        /// Merge another mesh into this one. All vertices, edges, and faces from the other mesh
        /// are added to this mesh. Attributes are copied.
        /// Note: Modifies vertex IDs of the source mesh during operation.
        /// </summary>
        /// <param name="mesh">The destination mesh.</param>
        /// <param name="other">The source mesh to merge in.</param>
        public static void Merge(GeometryData mesh, GeometryData other)
        {
            var newVerts = new GeometryData.Vertex[other.Vertices.Count];
            int i = 0;

            // Copy all vertices and their attributes
            foreach (var v in other.Vertices)
            {
                newVerts[i] = mesh.AddVertex(v.Point);
                AttributeLerp(mesh, newVerts[i], v, v, 1); // Copy all attributes
                v.Id = i;
                ++i;
            }

            // Copy all edges
            foreach (var e in other.Edges)
            {
                mesh.AddEdge(newVerts[e.Vert1.Id], newVerts[e.Vert2.Id]);
            }

            // Copy all faces
            foreach (var f in other.Faces)
            {
                var neighbors = f.NeighborVertices();
                var newNeighbors = new GeometryData.Vertex[neighbors.Count];
                int j = 0;
                foreach (var v in neighbors)
                {
                    newNeighbors[j] = newVerts[v.Id];
                    ++j;
                }
                mesh.AddFace(newNeighbors);
            }
        }

        #endregion

        #region Split Operations

        /// <summary>
        /// Split an edge at a given interpolation factor, creating a new vertex.
        /// </summary>
        /// <param name="mesh">The mesh containing the edge.</param>
        /// <param name="edge">The edge to split.</param>
        /// <param name="fromVertex">One of the edge's vertices (defines the "from" end).</param>
        /// <param name="factor">Interpolation factor (0.0 = at fromVertex, 1.0 = at the other vertex).</param>
        /// <param name="newEdge">Output: The newly created edge (from the new vertex to the non-fromVertex end).</param>
        /// <returns>The newly created vertex at the split point.</returns>
        public static GeometryData.Vertex SplitEdge(GeometryData mesh, GeometryData.Edge edge,
            GeometryData.Vertex fromVertex, double factor, out GeometryData.Edge newEdge)
        {
            Debug.Assert(edge.ContainsVertex(fromVertex));
            Debug.Assert(factor >= 0.0 && factor <= 1.0);

            GeometryData.Vertex otherVertex = edge.OtherVertex(fromVertex);

            // Create new vertex at interpolated position
            Double3 newPos = Maths.Lerp(fromVertex.Point, otherVertex.Point, factor);
            GeometryData.Vertex newVert = mesh.AddVertex(newPos);

            // Interpolate all vertex attributes
            AttributeLerp(mesh, newVert, fromVertex, otherVertex, factor);

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
                                                var val = new GeometryData.FloatAttributeValue { Data = new double[n] };
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

        /// <summary>
        /// Split a face along two vertices, creating a new face and edge.
        /// </summary>
        /// <param name="mesh">The mesh containing the face.</param>
        /// <param name="face">The face to split.</param>
        /// <param name="vert1">First vertex of the split.</param>
        /// <param name="vert2">Second vertex of the split (must be different from vert1 and not adjacent).</param>
        /// <param name="newEdge">Output: The newly created edge connecting the two vertices.</param>
        /// <returns>The newly created face, or null if the split fails.</returns>
        public static GeometryData.Face? SplitFace(GeometryData mesh, GeometryData.Face face,
            GeometryData.Vertex vert1, GeometryData.Vertex vert2, out GeometryData.Edge? newEdge)
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

        #endregion

        #region Plane Operations

        /// <summary>
        /// Bisect a mesh with a plane, splitting edges and faces that cross the plane.
        /// </summary>
        /// <param name="mesh">The mesh to bisect.</param>
        /// <param name="plane">The plane to bisect with (Normal.X, Normal.Y, Normal.Z, Distance).</param>
        /// <param name="epsilon">Tolerance for considering a vertex on the plane.</param>
        /// <param name="snapToPlane">If true, snap vertices very close to the plane onto it.</param>
        public static void BisectPlane(GeometryData mesh, Plane plane, double epsilon = 0.0001, bool snapToPlane = true)
        {
            // Store which side of the plane each vertex is on
            var vertexSide = new Dictionary<GeometryData.Vertex, int>(); // -1 = negative side, 0 = on plane, 1 = positive side
            var vertexDistance = new Dictionary<GeometryData.Vertex, double>();

            // Classify all vertices
            foreach (var v in mesh.Vertices.ToArray())
            {
                double distance = plane.GetSignedDistanceToPoint(v.Point);
                vertexDistance[v] = distance;

                if (distance <= -epsilon)
                {
                    vertexSide[v] = -1;
                }
                else if (distance >= epsilon)
                {
                    vertexSide[v] = 1;
                }
                else
                {
                    vertexSide[v] = 0;
                    if (snapToPlane)
                    {
                        // Snap vertex to plane
                        v.Point = plane.ClosestPointOnPlane(v.Point);
                    }
                }
            }

            // Track vertices that are on the plane (from splitting or already there)
            var verticesOnPlane = new HashSet<GeometryData.Vertex>();
            foreach (var v in mesh.Vertices)
            {
                if (vertexSide.ContainsKey(v) && vertexSide[v] == 0)
                    verticesOnPlane.Add(v);
            }

            // Split edges that cross the plane
            var edgesToSplit = new List<(GeometryData.Edge edge, double factor)>();
            foreach (var edge in mesh.Edges.ToArray())
            {
                if (!vertexSide.ContainsKey(edge.Vert1) || !vertexSide.ContainsKey(edge.Vert2))
                    continue;

                int side1 = vertexSide[edge.Vert1];
                int side2 = vertexSide[edge.Vert2];

                // Edge crosses the plane if vertices are on opposite sides
                if (side1 != 0 && side2 != 0 && side1 != side2)
                {
                    double dist1 = vertexDistance[edge.Vert1];
                    double dist2 = vertexDistance[edge.Vert2];

                    // Calculate interpolation factor for the intersection point
                    double factor = dist1 / (dist1 - dist2);
                    edgesToSplit.Add((edge, factor));
                }
            }

            // Split all crossing edges
            foreach (var (edge, factor) in edgesToSplit)
            {
                var newVertex = SplitEdge(mesh, edge, edge.Vert1, factor, out var newEdge);
                verticesOnPlane.Add(newVertex);
                vertexSide[newVertex] = 0;
                vertexDistance[newVertex] = 0.0;
            }

            // Now split faces that have vertices on both sides of the plane
            var facesToProcess = new List<GeometryData.Face>(mesh.Faces);
            foreach (var face in facesToProcess)
            {
                // Skip if face was already removed by a previous split
                if (!mesh.Faces.Contains(face))
                    continue;

                var faceVerts = face.NeighborVertices();

                // Check if this face needs splitting
                bool hasPositive = false;
                bool hasNegative = false;
                var vertsOnPlaneInFace = new List<GeometryData.Vertex>();
                int totalVerts = 0;

                foreach (var v in faceVerts)
                {
                    if (!vertexSide.ContainsKey(v))
                        continue;

                    totalVerts++;
                    int side = vertexSide[v];
                    if (side == -1) hasNegative = true;
                    else if (side == 1) hasPositive = true;
                    else if (side == 0) vertsOnPlaneInFace.Add(v);
                }

                // Only split if the face spans both sides and has exactly 2 non-adjacent vertices on the plane
                // If all vertices are on the plane, skip (face lies on plane)
                // If only has positive or negative vertices, skip (face is entirely on one side)
                if (hasPositive && hasNegative && vertsOnPlaneInFace.Count >= 2 && vertsOnPlaneInFace.Count < totalVerts)
                {
                    // Sort vertices on plane by their order in the face to find split pairs
                    SplitFaceAlongPlane(mesh, face, vertsOnPlaneInFace, vertexSide);
                }
            }
        }

        /// <summary>
        /// Helper method to split a face along vertices that lie on the bisecting plane.
        /// </summary>
        private static void SplitFaceAlongPlane(GeometryData mesh, GeometryData.Face face,
            List<GeometryData.Vertex> vertsOnPlane, Dictionary<GeometryData.Vertex, int> vertexSide)
        {
            if (vertsOnPlane.Count < 2)
                return;

            // Get all vertices in the face in order
            var allVerts = face.NeighborVertices();

            // Find indices of vertices on the plane
            var planeVertIndices = new List<int>();
            for (int i = 0; i < allVerts.Count; i++)
            {
                if (vertsOnPlane.Contains(allVerts[i]))
                    planeVertIndices.Add(i);
            }

            if (planeVertIndices.Count < 2)
                return;

            // For simple cases with exactly 2 vertices on the plane, split directly
            if (planeVertIndices.Count == 2)
            {
                var vert1 = allVerts[planeVertIndices[0]];
                var vert2 = allVerts[planeVertIndices[1]];

                // Make sure they're not adjacent (would just be an edge on the plane)
                int dist = Math.Abs(planeVertIndices[1] - planeVertIndices[0]);
                if (dist == 1 || dist == allVerts.Count - 1)
                    return;

                // Verify the split would separate positive and negative vertices
                if (!WouldSplitSeparateSides(allVerts, planeVertIndices[0], planeVertIndices[1], vertexSide))
                    return;

                // Split the face
                try
                {
                    SplitFace(mesh, face, vert1, vert2, out var newEdge);
                }
                catch
                {
                    // Face might already be removed or invalid, skip
                }
            }
            else if (planeVertIndices.Count > 2)
            {
                // More complex case: multiple vertices on the plane
                // We need to find pairs that properly separate positive from negative vertices

                // Try pairs in order of separation distance
                var pairs = new List<(int idx1, int idx2, int separation)>();
                for (int i = 0; i < planeVertIndices.Count - 1; i++)
                {
                    for (int j = i + 1; j < planeVertIndices.Count; j++)
                    {
                        int idx1 = planeVertIndices[i];
                        int idx2 = planeVertIndices[j];

                        int dist = Math.Abs(idx2 - idx1);
                        int wrapDist = allVerts.Count - dist;
                        int minDist = Math.Min(dist, wrapDist);

                        if (minDist > 1) // Not adjacent
                        {
                            pairs.Add((idx1, idx2, minDist));
                        }
                    }
                }

                // Sort by separation distance (larger first)
                pairs.Sort((a, b) => b.separation.CompareTo(a.separation));

                foreach (var (idx1, idx2, _) in pairs)
                {
                    var vert1 = allVerts[idx1];
                    var vert2 = allVerts[idx2];

                    // Verify the face still exists
                    if (!mesh.Faces.Contains(face))
                        return;

                    // Check that both vertices still belong to this face
                    var currentFaceVerts = face.NeighborVertices();
                    if (!currentFaceVerts.Contains(vert1) || !currentFaceVerts.Contains(vert2))
                        continue;

                    // Verify this split would separate positive and negative vertices
                    if (!WouldSplitSeparateSides(allVerts, idx1, idx2, vertexSide))
                        continue;

                    try
                    {
                        SplitFace(mesh, face, vert1, vert2, out var newEdge);
                        return; // Successfully split, done with this face
                    }
                    catch
                    {
                        // Try next pair
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Check if splitting a face between two vertex indices would separate positive and negative sides.
        /// </summary>
        private static bool WouldSplitSeparateSides(List<GeometryData.Vertex> allVerts, int idx1, int idx2,
            Dictionary<GeometryData.Vertex, int> vertexSide)
        {
            // Walk from idx1 to idx2 in one direction
            bool hasPositive1 = false, hasNegative1 = false;
            int current = (idx1 + 1) % allVerts.Count;
            while (current != idx2)
            {
                if (vertexSide.ContainsKey(allVerts[current]))
                {
                    int side = vertexSide[allVerts[current]];
                    if (side > 0) hasPositive1 = true;
                    if (side < 0) hasNegative1 = true;
                }
                current = (current + 1) % allVerts.Count;
            }

            // Walk from idx2 to idx1 in the other direction
            bool hasPositive2 = false, hasNegative2 = false;
            current = (idx2 + 1) % allVerts.Count;
            while (current != idx1)
            {
                if (vertexSide.ContainsKey(allVerts[current]))
                {
                    int side = vertexSide[allVerts[current]];
                    if (side > 0) hasPositive2 = true;
                    if (side < 0) hasNegative2 = true;
                }
                current = (current + 1) % allVerts.Count;
            }

            // Good split if one side has only positive/plane and other has only negative/plane
            bool side1Clean = (hasPositive1 && !hasNegative1) || (!hasPositive1 && hasNegative1);
            bool side2Clean = (hasPositive2 && !hasNegative2) || (!hasPositive2 && hasNegative2);
            bool sidesOpposite = (hasPositive1 && hasNegative2) || (hasNegative1 && hasPositive2);

            return side1Clean && side2Clean && sidesOpposite;
        }

        /// <summary>
        /// Remove all geometry on the positive side of a plane (where the normal points).
        /// Vertices exactly on the plane are kept.
        /// </summary>
        /// <param name="mesh">The mesh to modify.</param>
        /// <param name="plane">The plane to use for culling.</param>
        /// <param name="epsilon">Tolerance for considering a vertex on the plane.</param>
        public static void ClearPositive(GeometryData mesh, Plane plane, double epsilon = 0.0001)
        {
            // Classify vertices
            var verticesToRemove = new List<GeometryData.Vertex>();

            foreach (var v in mesh.Vertices.ToArray())
            {
                double distance = plane.GetSignedDistanceToPoint(v.Point);
                if (distance > epsilon) // Positive side
                {
                    verticesToRemove.Add(v);
                }
            }

            // Remove vertices (this will also remove connected edges and faces)
            foreach (var v in verticesToRemove)
            {
                if (mesh.Vertices.Contains(v))
                    mesh.RemoveVertex(v);
            }
        }

        /// <summary>
        /// Remove all geometry on the negative side of a plane (opposite to where the normal points).
        /// Vertices exactly on the plane are kept.
        /// </summary>
        /// <param name="mesh">The mesh to modify.</param>
        /// <param name="plane">The plane to use for culling.</param>
        /// <param name="epsilon">Tolerance for considering a vertex on the plane.</param>
        public static void ClearNegative(GeometryData mesh, Plane plane, double epsilon = 0.0001)
        {
            // Classify vertices
            var verticesToRemove = new List<GeometryData.Vertex>();

            foreach (var v in mesh.Vertices.ToArray())
            {
                double distance = plane.GetSignedDistanceToPoint(v.Point);
                if (distance < -epsilon) // Negative side
                {
                    verticesToRemove.Add(v);
                }
            }

            // Remove vertices (this will also remove connected edges and faces)
            foreach (var v in verticesToRemove)
            {
                if (mesh.Vertices.Contains(v))
                    mesh.RemoveVertex(v);
            }
        }

        #endregion

        #region Spatial Queries

        /// <summary>
        /// Find the vertex whose attribute value is closest to the given value.
        /// Uses Euclidean distance for comparison.
        /// </summary>
        /// <param name="mesh">The mesh to search.</param>
        /// <param name="value">The target attribute value.</param>
        /// <param name="attrName">The name of the attribute to compare.</param>
        /// <returns>The vertex with the closest attribute value, or null if attribute doesn't exist.</returns>
        public static GeometryData.Vertex? Nearpoint(GeometryData mesh, GeometryData.AttributeValue value, string attrName)
        {
            if (!mesh.HasVertexAttribute(attrName))
                return null;

            GeometryData.Vertex? argmin = null;
            double min = 0;

            foreach (var v in mesh.Vertices)
            {
                if (!v.Attributes.ContainsKey(attrName))
                    continue;

                double d = Distance(v.Attributes[attrName], value);
                if (argmin == null || d < min)
                {
                    argmin = v;
                    min = d;
                }
            }

            return argmin;
        }

        /// <summary>
        /// Calculate Euclidean distance between two attribute values.
        /// </summary>
        private static double Distance(GeometryData.AttributeValue value1, GeometryData.AttributeValue value2)
        {
            if (value1 is GeometryData.IntAttributeValue ival1 && value2 is GeometryData.IntAttributeValue ival2)
            {
                int n = ival1.Data.Length;
                if (n != ival2.Data.Length) return double.PositiveInfinity;

                double s = 0;
                for (int i = 0; i < n; ++i)
                {
                    double diff = ival1.Data[i] - ival2.Data[i];
                    s += diff * diff;
                }
                return Math.Sqrt(s);
            }

            if (value1 is GeometryData.FloatAttributeValue fval1 && value2 is GeometryData.FloatAttributeValue fval2)
            {
                int n = fval1.Data.Length;
                if (n != fval2.Data.Length) return double.PositiveInfinity;

                double s = 0;
                for (int i = 0; i < n; ++i)
                {
                    double diff = fval1.Data[i] - fval2.Data[i];
                    s += diff * diff;
                }
                return Math.Sqrt(s);
            }

            return double.PositiveInfinity;
        }

        #endregion

        #region Additional Utility Operators

        /// <summary>
        /// Recalculate normals for all vertices based on adjacent face normals.
        /// Creates or updates a "normal" Float attribute (3 dimensions) on vertices.
        /// </summary>
        /// <param name="mesh">The mesh to calculate normals for.</param>
        public static void RecalculateNormals(GeometryData mesh)
        {
            // Ensure normal attribute exists
            if (!mesh.HasVertexAttribute("normal"))
            {
                mesh.AddVertexAttribute("normal", GeometryData.AttributeBaseType.Float, 3);
            }

            // Initialize all normals to zero
            foreach (var v in mesh.Vertices)
            {
                v.Attributes["normal"] = new GeometryData.FloatAttributeValue(0, 0, 0);
            }

            // Accumulate face normals
            foreach (var face in mesh.Faces)
            {
                var verts = face.NeighborVertices();
                if (verts.Count < 3) continue;

                // Calculate face normal using first three vertices
                Double3 v0 = verts[0].Point;
                Double3 v1 = verts[1].Point;
                Double3 v2 = verts[2].Point;

                Double3 edge1 = v1 - v0;
                Double3 edge2 = v2 - v0;
                Double3 faceNormal = Double3.Normalize(Double3.Cross(edge1, edge2));

                // Add to all vertices of this face
                foreach (var vert in verts)
                {
                    var normal = vert.Attributes["normal"] as GeometryData.FloatAttributeValue;
                    if (normal != null)
                    {
                        Double3 current = normal.AsVector3();
                        current += faceNormal;
                        normal.FromVector3(current);
                    }
                }
            }

            // Normalize all vertex normals
            foreach (var v in mesh.Vertices)
            {
                var normal = v.Attributes["normal"] as GeometryData.FloatAttributeValue;
                if (normal != null)
                {
                    Double3 n = normal.AsVector3();
                    if (Double3.LengthSquared(n) > 0)
                    {
                        normal.FromVector3(Double3.Normalize(n));
                    }
                }
            }
        }

        /// <summary>
        /// Scale the entire mesh uniformly from the origin.
        /// </summary>
        /// <param name="mesh">The mesh to scale.</param>
        /// <param name="scale">The scale factor.</param>
        public static void Scale(GeometryData mesh, double scale)
        {
            foreach (var v in mesh.Vertices)
            {
                v.Point *= scale;
            }
        }

        /// <summary>
        /// Translate the entire mesh by an offset.
        /// </summary>
        /// <param name="mesh">The mesh to translate.</param>
        /// <param name="offset">The translation offset.</param>
        public static void Translate(GeometryData mesh, Double3 offset)
        {
            foreach (var v in mesh.Vertices)
            {
                v.Point += offset;
            }
        }

        /// <summary>
        /// Transform all vertices by a matrix.
        /// </summary>
        /// <param name="mesh">The mesh to transform.</param>
        /// <param name="transform">The transformation matrix.</param>
        public static void Transform(GeometryData mesh, Double4x4 transform)
        {
            foreach (var v in mesh.Vertices)
            {
                v.Point = Double4x4.TransformPoint(v.Point, transform);
            }
        }

        #endregion
    }
}
