// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Defines the extrusion mode for face extrusion operations.
    /// </summary>
    public enum ExtrudeMode
    {
        /// <summary>
        /// Each vertex moves along its own normal, averaged across all selected faces sharing that vertex.
        /// Vertices are shared between extruded faces.
        /// </summary>
        AlongNormals,

        /// <summary>
        /// All vertices move along the average normal of all selected faces.
        /// Vertices are shared between extruded faces.
        /// </summary>
        AverageNormal,

        /// <summary>
        /// Each face gets its own copy of vertices - no sharing.
        /// Each vertex moves along that face's normal.
        /// </summary>
        PerFace
    }

    /// <summary>
    /// Defines the inset mode for face inset operations.
    /// </summary>
    public enum InsetMode
    {
        /// <summary>
        /// Vertices are shared between inset faces.
        /// Non-shared vertices move towards their face centers.
        /// Shared vertices (on edges between selected faces) move along their edge lines towards edge midpoints,
        /// keeping them on the surface while insetting.
        /// </summary>
        Shared,

        /// <summary>
        /// Each face gets its own copy of vertices - no sharing.
        /// Each face shrinks independently towards its own center.
        /// </summary>
        PerFace
    }

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

        /// <summary>
        /// Triangulates all faces in the mesh by converting n-gons (faces with more than 3 vertices) into triangles.
        /// Uses fan triangulation from the first vertex of each face.
        /// All loop attributes (including UVs) are preserved on the resulting triangles.
        /// Faces that are already triangles are left unchanged.
        /// Modifies the mesh in-place.
        /// </summary>
        /// <param name="mesh">The mesh to triangulate.</param>
        public static void Triangulate(GeometryData mesh)
        {
            // Collect faces that need triangulation
            var facesToTriangulate = new List<GeometryData.Face>();

            foreach (var face in mesh.Faces)
            {
                if (face.VertCount > 3)
                {
                    facesToTriangulate.Add(face);
                }
            }

            // Process each face that needs triangulation
            foreach (var face in facesToTriangulate)
            {
                var verts = face.NeighborVertices();
                if (verts.Count < 3) continue;

                // Store loop attributes for each vertex in the face
                var loopAttributes = new Dictionary<string, GeometryData.AttributeValue>[verts.Count];
                for (int i = 0; i < verts.Count; i++)
                {
                    var loop = face.GetLoop(verts[i]);
                    loopAttributes[i] = new Dictionary<string, GeometryData.AttributeValue>();
                    if (loop != null)
                    {
                        foreach (var kvp in loop.Attributes)
                        {
                            loopAttributes[i][kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                        }
                    }
                }

                // Store face attributes
                var faceAttributes = new Dictionary<string, GeometryData.AttributeValue>();
                foreach (var kvp in face.Attributes)
                {
                    faceAttributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                }

                // Create triangles using fan triangulation from first vertex
                var triangles = new List<(GeometryData.Vertex, GeometryData.Vertex, GeometryData.Vertex, int, int, int)>();
                for (int i = 1; i < verts.Count - 1; i++)
                {
                    triangles.Add((verts[0], verts[i], verts[i + 1], 0, i, i + 1));
                }

                // Remove the original face
                mesh.RemoveFace(face);

                // Add triangular faces
                foreach (var (v0, v1, v2, idx0, idx1, idx2) in triangles)
                {
                    var newFace = mesh.AddFace(v0, v1, v2);
                    if (newFace != null)
                    {
                        // Copy face attributes
                        foreach (var kvp in faceAttributes)
                        {
                            newFace.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                        }

                        // Copy loop attributes
                        var loop = newFace.Loop;
                        if (loop != null)
                        {
                            // First vertex (v0)
                            foreach (var kvp in loopAttributes[idx0])
                            {
                                loop.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                            }
                            loop = loop.Next;

                            // Second vertex (v1)
                            if (loop != null)
                            {
                                foreach (var kvp in loopAttributes[idx1])
                                {
                                    loop.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                                }
                                loop = loop.Next;
                            }

                            // Third vertex (v2)
                            if (loop != null)
                            {
                                foreach (var kvp in loopAttributes[idx2])
                                {
                                    loop.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Face Extrusion

        /// <summary>
        /// Extrude a collection of faces outward (or inward) along their normals.
        /// Creates new geometry by duplicating the faces and connecting them with wall faces.
        /// </summary>
        /// <param name="mesh">The mesh containing the faces.</param>
        /// <param name="facesToExtrude">The faces to extrude.</param>
        /// <param name="distance">The extrusion distance (positive = outward along normal, negative = inward).</param>
        /// <param name="mode">The extrusion mode:
        /// AlongNormals - vertices move along averaged normals (shared vertices),
        /// AverageNormal - all vertices move along average of all face normals (shared vertices),
        /// PerFace - each face gets its own vertices (no sharing).</param>
        public static void ExtrudeFaces(GeometryData mesh, IEnumerable<GeometryData.Face> facesToExtrude,
            double distance, ExtrudeMode mode = ExtrudeMode.AlongNormals)
        {
            var facesToExtrudeList = facesToExtrude.ToList();
            if (facesToExtrudeList.Count == 0) return;

            // Calculate face normals for all faces
            var faceNormals = new Dictionary<GeometryData.Face, Double3>();
            foreach (var face in facesToExtrudeList)
            {
                var verts = face.NeighborVertices();
                if (verts.Count >= 3)
                {
                    Double3 v0 = verts[0].Point;
                    Double3 v1 = verts[1].Point;
                    Double3 v2 = verts[2].Point;
                    faceNormals[face] = Double3.Normalize(Double3.Cross(v1 - v0, v2 - v0));
                }
                else
                {
                    faceNormals[face] = Double3.Zero;
                }
            }

            // Calculate average normal for AverageNormal mode
            Double3 averageNormal = Double3.Zero;
            if (mode == ExtrudeMode.AverageNormal)
            {
                foreach (var normal in faceNormals.Values)
                {
                    averageNormal += normal;
                }
                if (facesToExtrudeList.Count > 0)
                {
                    averageNormal = Double3.Normalize(averageNormal);
                }
            }

            // For AlongNormals mode, calculate averaged vertex normals
            var vertexNormals = new Dictionary<GeometryData.Vertex, Double3>();
            if (mode == ExtrudeMode.AlongNormals)
            {
                // For each vertex, average the normals of all selected faces using it
                foreach (var face in facesToExtrudeList)
                {
                    var verts = face.NeighborVertices();
                    var faceNormal = faceNormals[face];

                    foreach (var vert in verts)
                    {
                        if (!vertexNormals.ContainsKey(vert))
                        {
                            vertexNormals[vert] = Double3.Zero;
                        }
                        vertexNormals[vert] += faceNormal;
                    }
                }

                // Normalize the accumulated normals
                foreach (var vert in vertexNormals.Keys.ToList())
                {
                    vertexNormals[vert] = Double3.Normalize(vertexNormals[vert]);
                }
            }

            // Track which vertices we've created for the extruded surface
            // For PerFace mode, we'll have separate mappings per face
            var vertexMapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

            // Track all edges that are part of the extruded faces
            var edgeUsageCount = new Dictionary<(GeometryData.Vertex, GeometryData.Vertex), int>();

            // First pass: count edge usage to identify boundary edges (only for shared vertex modes)
            if (mode != ExtrudeMode.PerFace)
            {
                foreach (var face in facesToExtrudeList)
                {
                    var verts = face.NeighborVertices();
                    for (int i = 0; i < verts.Count; i++)
                    {
                        var v1 = verts[i];
                        var v2 = verts[(i + 1) % verts.Count];

                        // Create a canonical edge key (smaller vertex first for consistency)
                        var edgeKey = v1.GetHashCode() < v2.GetHashCode() ? (v1, v2) : (v2, v1);

                        if (!edgeUsageCount.ContainsKey(edgeKey))
                            edgeUsageCount[edgeKey] = 0;
                        edgeUsageCount[edgeKey]++;
                    }
                }
            }

            // Store face data for recreation
            var faceData = new List<(
                GeometryData.Face originalFace,
                List<GeometryData.Vertex> verts,
                Dictionary<string, GeometryData.AttributeValue> faceAttrs,
                List<Dictionary<string, GeometryData.AttributeValue>> loopAttrs,
                Dictionary<GeometryData.Vertex, GeometryData.Vertex> perFaceMapping)>();

            foreach (var face in facesToExtrudeList)
            {
                var verts = face.NeighborVertices();
                var perFaceMapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

                // Create extruded vertices based on mode
                foreach (var vert in verts)
                {
                    GeometryData.Vertex newVert;

                    if (mode == ExtrudeMode.PerFace)
                    {
                        // PerFace: Always create new vertices for this face
                        Double3 normal = faceNormals[face];
                        Double3 newPos = vert.Point + normal * distance;
                        newVert = mesh.AddVertex(newPos);
                        AttributeLerp(mesh, newVert, vert, vert, 1.0);
                        perFaceMapping[vert] = newVert;
                    }
                    else if (!vertexMapping.ContainsKey(vert))
                    {
                        // AlongNormals or AverageNormal: Create shared vertices
                        Double3 normal;
                        if (mode == ExtrudeMode.AlongNormals)
                        {
                            normal = vertexNormals[vert];
                        }
                        else // AverageNormal
                        {
                            normal = averageNormal;
                        }

                        Double3 newPos = vert.Point + normal * distance;
                        newVert = mesh.AddVertex(newPos);
                        AttributeLerp(mesh, newVert, vert, vert, 1.0);
                        vertexMapping[vert] = newVert;
                    }
                }

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

                faceData.Add((face, verts, faceAttrs, loopAttrs, perFaceMapping));
            }

            // Remove original faces
            foreach (var face in facesToExtrudeList)
            {
                mesh.RemoveFace(face);
            }

            // Create extruded top faces
            for (int faceIdx = 0; faceIdx < faceData.Count; faceIdx++)
            {
                var (originalFace, verts, faceAttrs, loopAttrs, perFaceMapping) = faceData[faceIdx];

                // Map to new vertices based on mode
                GeometryData.Vertex[] newVerts;
                if (mode == ExtrudeMode.PerFace)
                {
                    newVerts = verts.Select(v => perFaceMapping[v]).ToArray();
                }
                else
                {
                    newVerts = verts.Select(v => vertexMapping[v]).ToArray();
                }

                var newFace = mesh.AddFace(newVerts);
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
                        int idx = 0;

                        do
                        {
                            if (idx < loopAttrs.Count)
                            {
                                foreach (var kvp in loopAttrs[idx])
                                {
                                    it.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                                }
                            }
                            it = it.Next;
                            idx++;
                        } while (it != newFace.Loop);
                    }
                }
            }

            // Create wall faces for boundary edges
            if (mode == ExtrudeMode.PerFace)
            {
                // PerFace: Each face creates its own walls
                foreach (var (originalFace, verts, _, _, perFaceMapping) in faceData)
                {
                    for (int i = 0; i < verts.Count; i++)
                    {
                        var v1 = verts[i];
                        var v2 = verts[(i + 1) % verts.Count];

                        var extV1 = perFaceMapping[v1];
                        var extV2 = perFaceMapping[v2];

                        // Create quad wall face
                        mesh.AddFace(v1, v2, extV2, extV1);
                    }
                }
            }
            else
            {
                // AlongNormals or AverageNormal: Only create walls for boundary edges
                foreach (var (originalFace, verts, _, _, _) in faceData)
                {
                    for (int i = 0; i < verts.Count; i++)
                    {
                        var v1 = verts[i];
                        var v2 = verts[(i + 1) % verts.Count];

                        // Create canonical edge key
                        var edgeKey = v1.GetHashCode() < v2.GetHashCode() ? (v1, v2) : (v2, v1);

                        // Only create a wall if this edge was on the boundary (used by only one face)
                        if (edgeUsageCount[edgeKey] == 1)
                        {
                            var extV1 = vertexMapping[v1];
                            var extV2 = vertexMapping[v2];

                            // Create quad wall face
                            mesh.AddFace(v1, v2, extV2, extV1);
                        }
                    }
                }
            }
        }


        #endregion

        #region Inset

        private class EdgeInfo
        {
            public GeometryData.Vertex V1 { get; set; }
            public GeometryData.Vertex V2 { get; set; }
            public int UsageCount { get; set; }
            public bool IsBoundary => UsageCount == 1;

            public override int GetHashCode()
            {
                // Canonical edge representation
                var h1 = V1.GetHashCode();
                var h2 = V2.GetHashCode();
                return h1 < h2 ? (h1, h2).GetHashCode() : (h2, h1).GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is EdgeInfo other)
                {
                    return (V1 == other.V1 && V2 == other.V2) ||
                           (V1 == other.V2 && V2 == other.V1);
                }
                return false;
            }
        }

        private class VertexInfo
        {
            public GeometryData.Vertex Vertex { get; set; }
            public List<GeometryData.Face> Faces { get; set; } = new List<GeometryData.Face>();
            public HashSet<GeometryData.Vertex> SharedEdgeNeighbors { get; set; } = new HashSet<GeometryData.Vertex>();
            public Double3 TargetPosition { get; set; }
            public bool IsShared => Faces.Count > 1;
        }

        /// <summary>
        /// Inset a collection of faces by shrinking them towards their centers.
        /// Creates new smaller faces and connects them with wall faces to the original edges.
        /// </summary>
        /// <param name="mesh">The mesh containing the faces.</param>
        /// <param name="facesToInset">The faces to inset.</param>
        /// <param name="amount">The inset amount (0 = no change, 1 = full inset to face center).</param>
        /// <param name="mode">The inset mode (Shared or PerFace).</param>
        public static void InsetFaces(GeometryData mesh, IEnumerable<GeometryData.Face> facesToInset,
            double amount, InsetMode mode = InsetMode.Shared)
        {
            var facesToInsetList = facesToInset.ToList();
            if (facesToInsetList.Count == 0) return;

            // Clamp amount to valid range
            amount = Math.Max(0.0, Math.Min(1.0, amount));

            // Early exit for zero inset
            if (Math.Abs(amount) < 1e-10) return;

            // Calculate face centers and normals
            var faceCenters = new Dictionary<GeometryData.Face, Double3>();
            foreach (var face in facesToInsetList)
            {
                faceCenters[face] = face.Center();
            }

            // Build comprehensive edge information
            var edgeInfoMap = BuildEdgeInfo(facesToInsetList);

            // Build vertex information for Shared mode
            var vertexInfoMap = BuildVertexInfo(facesToInsetList, edgeInfoMap, mode);

            // Calculate target positions
            if (mode == InsetMode.Shared)
            {
                CalculateSharedTargetPositions(vertexInfoMap, faceCenters, amount);
            }

            // Store face data for recreation
            var faceDataList = new List<FaceData>();
            foreach (var face in facesToInsetList)
            {
                var faceData = new FaceData
                {
                    OriginalFace = face,
                    Vertices = face.NeighborVertices(),
                    FaceCenter = faceCenters[face]
                };

                // Store face attributes
                faceData.FaceAttributes = new Dictionary<string, GeometryData.AttributeValue>();
                foreach (var attr in mesh.FaceAttributes)
                {
                    if (face.Attributes.ContainsKey(attr.Name))
                        faceData.FaceAttributes[attr.Name] = GeometryData.AttributeValue.Copy(face.Attributes[attr.Name]);
                }

                // Store loop attributes
                faceData.LoopAttributes = new List<Dictionary<string, GeometryData.AttributeValue>>();
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
                        faceData.LoopAttributes.Add(attrs);
                        it = it.Next;
                    } while (it != face.Loop);
                }

                faceDataList.Add(faceData);
            }

            // Create new vertices for inset
            var vertexMapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

            foreach (var faceData in faceDataList)
            {
                faceData.PerFaceMapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

                foreach (var vert in faceData.Vertices)
                {
                    GeometryData.Vertex newVert;

                    if (mode == InsetMode.PerFace)
                    {
                        // PerFace: Always create new vertices
                        Double3 newPos = Maths.Lerp(vert.Point, faceData.FaceCenter, amount);
                        newVert = mesh.AddVertex(newPos);
                        AttributeLerp(mesh, newVert, vert, vert, 1.0);
                        faceData.PerFaceMapping[vert] = newVert;
                    }
                    else if (!vertexMapping.ContainsKey(vert))
                    {
                        // Shared: Use pre-calculated target position
                        if (vertexInfoMap.TryGetValue(vert, out var vertInfo))
                        {
                            newVert = mesh.AddVertex(vertInfo.TargetPosition);
                            AttributeLerp(mesh, newVert, vert, vert, 1.0);
                            vertexMapping[vert] = newVert;
                        }
                    }
                }
            }

            // Remove original faces
            foreach (var face in facesToInsetList)
            {
                mesh.RemoveFace(face);
            }

            // Create inset faces
            foreach (var faceData in faceDataList)
            {
                GeometryData.Vertex[] newVerts;

                if (mode == InsetMode.PerFace)
                {
                    newVerts = faceData.Vertices.Select(v => faceData.PerFaceMapping[v]).ToArray();
                }
                else
                {
                    newVerts = faceData.Vertices.Select(v => vertexMapping[v]).ToArray();
                }

                var newFace = mesh.AddFace(newVerts);
                if (newFace != null)
                {
                    // Restore face attributes
                    foreach (var kvp in faceData.FaceAttributes)
                    {
                        newFace.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                    }

                    // Restore loop attributes
                    if (newFace.Loop != null && faceData.LoopAttributes.Count > 0)
                    {
                        var it = newFace.Loop;
                        int idx = 0;
                        do
                        {
                            if (idx < faceData.LoopAttributes.Count)
                            {
                                foreach (var kvp in faceData.LoopAttributes[idx])
                                {
                                    it.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                                }
                            }
                            it = it.Next;
                            idx++;
                        } while (it != newFace.Loop);
                    }
                }
            }

            // Create wall faces
            CreateWallFaces(mesh, faceDataList, edgeInfoMap, vertexMapping, mode);
        }

        private static Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo> BuildEdgeInfo(
            List<GeometryData.Face> facesToInsetList)
        {
            var edgeInfoMap = new Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo>();

            foreach (var face in facesToInsetList)
            {
                var verts = face.NeighborVertices();
                for (int i = 0; i < verts.Count; i++)
                {
                    var v1 = verts[i];
                    var v2 = verts[(i + 1) % verts.Count];

                    // Create canonical edge key
                    var edgeKey = GetCanonicalEdge(v1, v2);

                    if (!edgeInfoMap.ContainsKey(edgeKey))
                    {
                        edgeInfoMap[edgeKey] = new EdgeInfo { V1 = edgeKey.Item1, V2 = edgeKey.Item2, UsageCount = 0 };
                    }
                    edgeInfoMap[edgeKey].UsageCount++;
                }
            }

            return edgeInfoMap;
        }

        private static Dictionary<GeometryData.Vertex, VertexInfo> BuildVertexInfo(
            List<GeometryData.Face> facesToInsetList,
            Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo> edgeInfoMap,
            InsetMode mode)
        {
            var vertexInfoMap = new Dictionary<GeometryData.Vertex, VertexInfo>();

            // Build vertex-to-faces mapping
            foreach (var face in facesToInsetList)
            {
                var verts = face.NeighborVertices();
                foreach (var vert in verts)
                {
                    if (!vertexInfoMap.ContainsKey(vert))
                    {
                        vertexInfoMap[vert] = new VertexInfo { Vertex = vert };
                    }
                    vertexInfoMap[vert].Faces.Add(face);
                }
            }

            if (mode == InsetMode.Shared)
            {
                // Identify shared edges (edges used by more than one face)
                var sharedEdges = new HashSet<(GeometryData.Vertex, GeometryData.Vertex)>();
                foreach (var kvp in edgeInfoMap)
                {
                    if (kvp.Value.UsageCount > 1)
                    {
                        sharedEdges.Add(kvp.Key);
                    }
                }

                // For each vertex, find its shared edge neighbors
                foreach (var face in facesToInsetList)
                {
                    var verts = face.NeighborVertices();
                    for (int i = 0; i < verts.Count; i++)
                    {
                        var v1 = verts[i];
                        var v2 = verts[(i + 1) % verts.Count];

                        var edgeKey = GetCanonicalEdge(v1, v2);

                        if (sharedEdges.Contains(edgeKey) && vertexInfoMap[v1].IsShared && vertexInfoMap[v2].IsShared)
                        {
                            vertexInfoMap[v1].SharedEdgeNeighbors.Add(v2);
                            vertexInfoMap[v2].SharedEdgeNeighbors.Add(v1);
                        }
                    }
                }
            }

            return vertexInfoMap;
        }

        private static void CalculateSharedTargetPositions(
            Dictionary<GeometryData.Vertex, VertexInfo> vertexInfoMap,
            Dictionary<GeometryData.Face, Double3> faceCenters,
            double amount)
        {
            foreach (var kvp in vertexInfoMap)
            {
                var vert = kvp.Key;
                var vertInfo = kvp.Value;

                if (!vertInfo.IsShared)
                {
                    // Non-shared vertex: move towards face center
                    var faceCenter = faceCenters[vertInfo.Faces[0]];
                    vertInfo.TargetPosition = Maths.Lerp(vert.Point, faceCenter, amount);
                }
                else
                {
                    // Shared vertex: handle based on connectivity
                    var connectedSharedVerts = vertInfo.SharedEdgeNeighbors.ToList();

                    if (connectedSharedVerts.Count == 0)
                    {
                        // Isolated - shouldn't happen but fallback to stay in place
                        vertInfo.TargetPosition = vert.Point;
                    }
                    else if (connectedSharedVerts.Count == 1)
                    {
                        // End of chain: move along the single shared edge
                        var edgePartner = connectedSharedVerts[0];
                        var edgeMidpoint = (vert.Point + edgePartner.Point) * 0.5;
                        vertInfo.TargetPosition = Maths.Lerp(vert.Point, edgeMidpoint, amount);
                    }
                    else if (connectedSharedVerts.Count == 2)
                    {
                        // Middle of chain: move towards average of neighbors
                        // This preserves the edge flow
                        var avgPos = Double3.Zero;
                        foreach (var neighbor in connectedSharedVerts)
                        {
                            avgPos += neighbor.Point;
                        }
                        avgPos /= connectedSharedVerts.Count;
                        vertInfo.TargetPosition = Maths.Lerp(vert.Point, avgPos, amount);
                    }
                    else
                    {
                        // Junction vertex (3+ shared edges): don't move to preserve topology
                        // This prevents mesh collapse at complex junctions
                        vertInfo.TargetPosition = vert.Point;
                    }
                }
            }
        }

        private static void CreateWallFaces(
            GeometryData mesh,
            List<FaceData> faceDataList,
            Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo> edgeInfoMap,
            Dictionary<GeometryData.Vertex, GeometryData.Vertex> vertexMapping,
            InsetMode mode)
        {
            if (mode == InsetMode.PerFace)
            {
                // PerFace: Each face creates its own walls
                foreach (var faceData in faceDataList)
                {
                    for (int i = 0; i < faceData.Vertices.Count; i++)
                    {
                        var v1 = faceData.Vertices[i];
                        var v2 = faceData.Vertices[(i + 1) % faceData.Vertices.Count];

                        var insetV1 = faceData.PerFaceMapping[v1];
                        var insetV2 = faceData.PerFaceMapping[v2];

                        // Create quad wall face (winding order matters!)
                        mesh.AddFace(insetV1, v1, v2, insetV2);
                    }
                }
            }
            else
            {
                // Shared: Only create walls for boundary edges
                foreach (var faceData in faceDataList)
                {
                    for (int i = 0; i < faceData.Vertices.Count; i++)
                    {
                        var v1 = faceData.Vertices[i];
                        var v2 = faceData.Vertices[(i + 1) % faceData.Vertices.Count];

                        var edgeKey = GetCanonicalEdge(v1, v2);

                        // Only create wall if this edge is on the boundary
                        if (edgeInfoMap.TryGetValue(edgeKey, out var edgeInfo) && edgeInfo.IsBoundary)
                        {
                            if (vertexMapping.TryGetValue(v1, out var insetV1) &&
                                vertexMapping.TryGetValue(v2, out var insetV2))
                            {
                                // Create quad wall face
                                mesh.AddFace(insetV1, v1, v2, insetV2);
                            }
                        }
                    }
                }
            }
        }

        private static (GeometryData.Vertex, GeometryData.Vertex) GetCanonicalEdge(
            GeometryData.Vertex v1, GeometryData.Vertex v2)
        {
            return v1.GetHashCode() < v2.GetHashCode() ? (v1, v2) : (v2, v1);
        }

        private class FaceData
        {
            public GeometryData.Face OriginalFace { get; set; }
            public List<GeometryData.Vertex> Vertices { get; set; }
            public Double3 FaceCenter { get; set; }
            public Dictionary<string, GeometryData.AttributeValue> FaceAttributes { get; set; }
            public List<Dictionary<string, GeometryData.AttributeValue>> LoopAttributes { get; set; }
            public Dictionary<GeometryData.Vertex, GeometryData.Vertex> PerFaceMapping { get; set; }
        }

        #endregion

        #region Vertex Welding

        /// <summary>
        /// Weld vertices that are within a distance threshold, merging them into single vertices.
        /// This is useful for cleaning up geometry with duplicate or nearly-duplicate vertices.
        /// Attributes are averaged based on the merged vertices.
        /// </summary>
        /// <param name="mesh">The mesh to weld vertices in.</param>
        /// <param name="threshold">Maximum distance between vertices to be welded together.</param>
        /// <returns>The number of vertices that were welded (removed).</returns>
        public static int WeldVertices(GeometryData mesh, double threshold = 0.0001)
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

        /// <summary>
        /// Weld vertices at specific positions by merging all vertices within threshold distance of each target.
        /// This is useful for welding specific problem areas without affecting the entire mesh.
        /// </summary>
        /// <param name="mesh">The mesh to weld vertices in.</param>
        /// <param name="targetPositions">Positions around which to weld vertices.</param>
        /// <param name="threshold">Maximum distance from target positions to weld vertices.</param>
        /// <returns>The number of vertices that were welded (removed).</returns>
        public static int WeldVerticesAtPositions(GeometryData mesh, IEnumerable<Double3> targetPositions, double threshold)
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

        #endregion

        #region Vertex Beveling

        /// <summary>
        /// Bevel one or more vertices by replacing each with a small face.
        /// Creates new vertices along edges connected to each beveled vertex and connects them with a bevel face.
        /// Vertices are beveled in the order they are passed in.
        /// </summary>
        /// <param name="mesh">The mesh containing the vertices.</param>
        /// <param name="verticesToBevel">The vertices to bevel, in order.</param>
        /// <param name="offset">Distance along edges from the vertex to place new vertices (0.0 to 1.0, where 0.5 is midpoint).</param>
        public static void BevelVertices(GeometryData mesh, IEnumerable<GeometryData.Vertex> verticesToBevel, double offset = 0.3)
        {
            if (offset <= 0.0 || offset >= 1.0)
                throw new ArgumentException("Offset must be between 0.0 and 1.0", nameof(offset));

            var vertexList = verticesToBevel.Where(v => mesh.Vertices.Contains(v)).ToList();
            if (vertexList.Count == 0)
                return;

            // Process all vertices at once to handle shared edges
            BevelVerticesInternal(mesh, vertexList, offset);
        }

        /// <summary>
        /// Internal method to bevel multiple vertices, handling shared edges correctly.
        /// </summary>
        private static void BevelVerticesInternal(GeometryData mesh, List<GeometryData.Vertex> verticesToBevel, double offset)
        {
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
                    AttributeLerp(mesh, newVert, vertex, otherVertex, offset);

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

        #endregion
    }
}
