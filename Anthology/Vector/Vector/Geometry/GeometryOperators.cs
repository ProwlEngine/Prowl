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
