// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Prowl.Vector.Geometry.Operators;

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
        #region General Operators

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

        public static (GeometryData.Vertex, GeometryData.Vertex) GetCanonicalEdge(GeometryData.Vertex v1, GeometryData.Vertex v2)
        {
            return v1.GetHashCode() < v2.GetHashCode() ? (v1, v2) : (v2, v1);
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
        /// Scale specified faces uniformly from their centers.
        /// </summary>
        /// <param name="mesh">The mesh to scale.</param>
        /// <param name="faces">The faces to scale.</param>
        /// <param name="scale">The scale factor.</param>
        public static void ScaleFace(GeometryData mesh, GeometryData.Face face, double scale)
        {
            // Calculate face center
            Double3 center = Double3.Zero;
            var verts = face.NeighborVertices();
            int count = verts.Count;
            foreach (var v in verts)
            {
                center += v.Point;
            }
            center /= count;
            // Scale each vertex away from the center
            foreach (var v in verts)
            {
                Double3 dir = v.Point - center;
                v.Point = center + dir * scale;
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
        /// Translate specified faces by an offset.
        /// </summary>
        /// <param name="mesh">The mesh to translate.</param>
        /// <param name="faces">The faces to translate.</param>
        /// <param name="offset">The translation offset.</param>
        public static void TranslateFaces(GeometryData mesh, IEnumerable<GeometryData.Face> faces, Double3 offset)
        {
            var movedVertices = new HashSet<GeometryData.Vertex>();
            foreach (var face in faces)
            {
                foreach (var v in face.NeighborVerticesEnumerable())
                {
                    if (!movedVertices.Contains(v))
                    {
                        v.Point += offset;
                        movedVertices.Add(v);
                    }
                }
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
        /// Transform specified faces by a matrix.
        /// </summary>
        /// <param name="mesh">The mesh to transform.</param>
        /// <param name="faces">The faces to transform.</param>
        /// <param name="transform">The transformation matrix.</param>
        public static void TransformFaces(GeometryData mesh, IEnumerable<GeometryData.Face> faces, Double4x4 transform)
        {
            var transformedVertices = new HashSet<GeometryData.Vertex>();
            foreach (var face in faces)
            {
                foreach (var v in face.NeighborVerticesEnumerable())
                {
                    if (!transformedVertices.Contains(v))
                    {
                        v.Point = Double4x4.TransformPoint(v.Point, transform);
                        transformedVertices.Add(v);
                    }
                }
            }
        }

        /// <summary>
        /// Remove all geometry on the positive side of a plane (where the normal points).
        /// Vertices exactly on the plane are kept.
        /// </summary>
        /// <param name="mesh">The mesh to modify.</param>
        /// <param name="plane">The plane to use for culling.</param>
        /// <param name="epsilon">Tolerance for considering a vertex on the plane.</param>
        public static void RemoveVerticesOnPlanePositiveSide(GeometryData mesh, Plane plane, double epsilon = 0.0001)
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
        public static void RemoveVerticesOnPlaneNegativeSide(GeometryData mesh, Plane plane, double epsilon = 0.0001)
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

        /// <summary>
        /// Subdivide all faces in the mesh without smoothing.
        /// After subdivision, all faces are quads. Attempts to interpolate all attributes.
        /// Note: Modifies edge IDs during operation.
        /// </summary>
        /// <param name="mesh">The mesh to subdivide.</param>
        public static void Subdivide(GeometryData mesh)
        {
            SubdivideOp.Subdivide(mesh);
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
            SquarifyOp.SquarifyQuads(mesh, rate, uniformLength);
        }

        /// <summary>
        /// Merge another mesh into this one. All vertices, edges, and faces from the other mesh
        /// are added to this mesh. Attributes are copied.
        /// Note: Modifies vertex IDs of the source mesh during operation.
        /// </summary>
        /// <param name="mesh">The destination mesh.</param>
        /// <param name="other">The source mesh to merge in.</param>
        public static void Merge(GeometryData mesh, GeometryData other)
        {
            MergeOp.Merge(mesh, other);
        }

        /// <summary>
        /// Split an edge at a given interpolation factor, creating a new vertex.
        /// </summary>
        /// <param name="mesh">The mesh containing the edge.</param>
        /// <param name="edge">The edge to split.</param>
        /// <param name="fromVertex">One of the edge's vertices (defines the "from" end).</param>
        /// <param name="factor">Interpolation factor (0.0 = at fromVertex, 1.0 = at the other vertex).</param>
        /// <param name="newEdge">Output: The newly created edge (from the new vertex to the non-fromVertex end).</param>
        /// <returns>The newly created vertex at the split point.</returns>
        public static GeometryData.Vertex SplitEdge(GeometryData mesh, GeometryData.Edge edge, GeometryData.Vertex fromVertex, double factor, out GeometryData.Edge newEdge)
        {
            return SplitEdgeOp.SplitEdge(mesh, edge, fromVertex, factor, out newEdge);
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
        public static GeometryData.Face? SplitFace(GeometryData mesh, GeometryData.Face face, GeometryData.Vertex vert1, GeometryData.Vertex vert2, out GeometryData.Edge? newEdge)
        {
            return SplitFaceOp.SplitFace(mesh, face, vert1, vert2, out newEdge);
        }

        /// <summary>
        /// Bisect a mesh with a plane, splitting edges and faces that cross the plane.
        /// </summary>
        /// <param name="mesh">The mesh to bisect.</param>
        /// <param name="plane">The plane to bisect with (Normal.X, Normal.Y, Normal.Z, Distance).</param>
        /// <param name="epsilon">Tolerance for considering a vertex on the plane.</param>
        /// <param name="snapToPlane">If true, snap vertices very close to the plane onto it.</param>
        public static void BisectPlane(GeometryData mesh, Plane plane, double epsilon = 0.0001, bool snapToPlane = true)
        {
            BisectPlaneOp.BisectPlane(mesh, plane, epsilon, snapToPlane);
        }

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
        public static void ExtrudeFaces(GeometryData mesh, IEnumerable<GeometryData.Face> facesToExtrude, double distance, ExtrudeMode mode = ExtrudeMode.AlongNormals)
        {
            ExtrudeOp.ExtrudeFaces(mesh, facesToExtrude, distance, mode);
        }

        /// <summary>
        /// Inset a collection of faces by shrinking them towards their centers.
        /// Creates new smaller faces and connects them with wall faces to the original edges.
        /// </summary>
        /// <param name="mesh">The mesh containing the faces.</param>
        /// <param name="facesToInset">The faces to inset.</param>
        /// <param name="thickness">The inset amount (0 = no change, 1 = full inset to face center).</param>
        /// <param name="mode">The inset mode (Shared or PerFace).</param>
        public static void InsetFaces(GeometryData mesh, IEnumerable<GeometryData.Face> facesToInset, double thickness, InsetMode mode = InsetMode.Shared)
        {
            InsetOp.InsetFaces(mesh, facesToInset, thickness, mode);
        }

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
            return VertexWeldOp.WeldVertices(mesh, threshold);
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
            return VertexWeldOp.WeldVerticesAtPositions(mesh, targetPositions, threshold);
        }

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
            BevelVertexOp.BevelVertices(mesh, verticesToBevel, offset);
        }

    }
}
