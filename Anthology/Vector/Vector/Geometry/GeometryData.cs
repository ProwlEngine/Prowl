// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Prowl.Vector.Geometry
{

    /// <summary>
    /// Non-manifold boundary representation of a 3D mesh with arbitrary attributes.
    /// Similar to Blender's BMesh, this structure makes procedural mesh creation and
    /// arbitrary edits easy while remaining efficient. Supports vertices, edges, loops (face corners),
    /// and faces with arbitrary polygon counts and custom attributes on all topological elements.
    /// </summary>
    public class GeometryData
    {
        // Topological entities
        public List<Vertex> Vertices { get; } = new List<Vertex>();
        public List<Edge> Edges { get; } = new List<Edge>();
        public List<Loop> Loops { get; } = new List<Loop>();
        public List<Face> Faces { get; } = new List<Face>();

        // Attribute definitions - ensures consistency across all elements
        public List<AttributeDefinition> VertexAttributes { get; } = new List<AttributeDefinition>();
        public List<AttributeDefinition> EdgeAttributes { get; } = new List<AttributeDefinition>();
        public List<AttributeDefinition> LoopAttributes { get; } = new List<AttributeDefinition>();
        public List<AttributeDefinition> FaceAttributes { get; } = new List<AttributeDefinition>();

        #region Topology Types

        /// <summary>
        /// A vertex corresponds to a position in space. Many primitives (edges, faces) can share a vertex.
        /// </summary>
        public class Vertex
        {
            public int Id; // User-defined ID
            public Float3 Point; // Position in space
            public Dictionary<string, AttributeValue> Attributes = new Dictionary<string, AttributeValue>();
            public Edge? Edge; // One of the edges using this vertex, navigate others using edge.Next()

            public Vertex(Float3 point)
            {
                Point = point;
            }

            /// <summary>
            /// List all edges connected to this vertex.
            /// </summary>
            public List<Edge> NeighborEdges()
            {
                var edges = new List<Edge>();
                if (Edge != null)
                {
                    Edge it = Edge;
                    do
                    {
                        edges.Add(it);
                        it = it.Next(this);
                    } while (it != Edge);
                }
                return edges;
            }

            /// <summary>
            /// Return all faces that use this vertex as a corner.
            /// </summary>
            public List<Face> NeighborFaces()
            {
                var faces = new HashSet<Face>();
                if (Edge != null)
                {
                    Edge it = Edge;
                    do
                    {
                        foreach (var f in it.NeighborFaces())
                            faces.Add(f);
                        it = it.Next(this);
                    } while (it != Edge);
                }
                return faces.ToList();
            }
        }

        /// <summary>
        /// An edge links two vertices together and may be part of one or more faces.
        /// </summary>
        public class Edge
        {
            public int Id;
            public Dictionary<string, AttributeValue> Attributes = new Dictionary<string, AttributeValue>();
            public Vertex Vert1;
            public Vertex Vert2;
            public Edge? Next1; // Next edge around vert1
            public Edge? Next2; // Next edge around vert2
            public Edge? Prev1;
            public Edge? Prev2;
            public Loop? Loop; // First loop using this edge, navigate radially using loop.RadialNext

            public Edge(Vertex v1, Vertex v2)
            {
                Vert1 = v1;
                Vert2 = v2;
            }

            public bool ContainsVertex(Vertex v) => v == Vert1 || v == Vert2;

            public Vertex OtherVertex(Vertex v)
            {
                Debug.Assert(ContainsVertex(v));
                return v == Vert1 ? Vert2 : Vert1;
            }

            public Edge? Next(Vertex v)
            {
                Debug.Assert(ContainsVertex(v));
                return v == Vert1 ? Next1 : Next2;
            }

            public void SetNext(Vertex v, Edge? other)
            {
                Debug.Assert(ContainsVertex(v));
                if (v == Vert1) Next1 = other;
                else Next2 = other;
            }

            public Edge? Prev(Vertex v)
            {
                Debug.Assert(ContainsVertex(v));
                return v == Vert1 ? Prev1 : Prev2;
            }

            public void SetPrev(Vertex v, Edge? other)
            {
                Debug.Assert(ContainsVertex(v));
                if (v == Vert1) Prev1 = other;
                else Prev2 = other;
            }

            public List<Face> NeighborFaces()
            {
                var faces = new List<Face>();
                if (Loop != null)
                {
                    var it = Loop;
                    do
                    {
                        faces.Add(it.Face);
                        it = it.RadialNext;
                    } while (it != Loop);
                }
                return faces;
            }

            public Float3 Center() => (Vert1.Point + Vert2.Point) * 0.5f;
        }

        /// <summary>
        /// A loop represents a face corner and is a node in both the face's edge loop and the edge's radial loop.
        /// This is where per-corner attributes like UVs are typically stored.
        /// </summary>
        public class Loop
        {
            public Dictionary<string, AttributeValue> Attributes = new Dictionary<string, AttributeValue>();
            public Vertex Vert;
            public Edge Edge;
            public Face Face;
            public Loop? RadialPrev; // Around edge
            public Loop? RadialNext;
            public Loop? Prev; // Around face
            public Loop? Next;

            public Loop(Vertex v, Edge e, Face f)
            {
                Vert = v;
                Edge = e;
                Face = f;
            }
        }

        /// <summary>
        /// A face is a polygon with any number of vertices, represented as a loop of edges.
        /// </summary>
        public class Face
        {
            public int Id;
            public Dictionary<string, AttributeValue> Attributes = new Dictionary<string, AttributeValue>();
            public int VertCount; // Cached for convenience
            public Loop? Loop; // Navigate using loop.Next

            public List<Vertex> NeighborVertices()
            {
                var verts = new List<Vertex>();
                if (Loop != null)
                {
                    var it = Loop;
                    do
                    {
                        verts.Add(it.Vert);
                        it = it.Next;
                    } while (it != Loop);
                }
                return verts;
            }

            public IEnumerable<Vertex> NeighborVerticesEnumerable()
            {
                if (Loop != null)
                {
                    var it = Loop;
                    do
                    {
                        yield return it.Vert;
                        it = it.Next;
                    } while (it != Loop);
                }
            }

            public Loop? GetLoop(Vertex v)
            {
                if (Loop != null)
                {
                    var it = Loop;
                    do
                    {
                        if (it.Vert == v) return it;
                        it = it.Next;
                    } while (it != Loop);
                }
                return null;
            }

            public List<Edge> NeighborEdges()
            {
                var edges = new List<Edge>();
                if (Loop != null)
                {
                    var it = Loop;
                    do
                    {
                        edges.Add(it.Edge);
                        it = it.Next;
                    } while (it != Loop);
                }
                return edges;
            }

            public Float3 Center()
            {
                var p = Float3.Zero;
                float sum = 0;
                foreach (var v in NeighborVertices())
                {
                    p += v.Point;
                    sum += 1;
                }
                return sum > 0 ? p / sum : Float3.Zero;
            }

            public Float3 Normal()
            {
                var verts = NeighborVertices();
                if (verts.Count < 3)
                    return Float3.Zero;
                Float3 normal = Float3.Zero;
                for (int i = 0; i < verts.Count; i++)
                {
                    var current = verts[i].Point;
                    var next = verts[(i + 1) % verts.Count].Point;
                    normal.X += (current.Y - next.Y) * (current.Z + next.Z);
                    normal.Y += (current.Z - next.Z) * (current.X + next.X);
                    normal.Z += (current.X - next.X) * (current.Y + next.Y);
                }
                return Float3.Normalize(normal);
            }

            /// <summary>
            /// Gets the axis-aligned bounding box
            /// </summary>
            public AABB GetAABB()
            {
                var verts = NeighborVertices();
                if (verts.Count == 0)
                    return new AABB();

                return AABB.FromPoints(verts.Select(v => v.Point).ToArray());
            }
        }

        #endregion

        #region Topology Methods

        /// <summary>
        /// Add a new vertex to the mesh.
        /// </summary>
        public Vertex AddVertex(Vertex vert)
        {
            EnsureVertexAttributes(vert);
            Vertices.Add(vert);
            return vert;
        }

        public Vertex AddVertex(Float3 point) => AddVertex(new Vertex(point));
        public Vertex AddVertex(float x, float y, float z) => AddVertex(new Float3(x, y, z));

        /// <summary>
        /// Add a new edge between two vertices. If there is already such an edge, return it.
        /// </summary>
        public Edge AddEdge(Vertex vert1, Vertex vert2)
        {
            Debug.Assert(vert1 != vert2);

            var edge = FindEdge(vert1, vert2);
            if (edge != null) return edge;

            edge = new Edge(vert1, vert2);
            EnsureEdgeAttributes(edge);
            Edges.Add(edge);

            // Insert in vert1's edge list
            if (vert1.Edge == null)
            {
                vert1.Edge = edge;
                edge.Next1 = edge.Prev1 = edge;
            }
            else
            {
                edge.Next1 = vert1.Edge.Next(vert1);
                edge.Prev1 = vert1.Edge;
                edge.Next1?.SetPrev(vert1, edge);
                edge.Prev1?.SetNext(vert1, edge);
            }

            // Same for vert2
            if (vert2.Edge == null)
            {
                vert2.Edge = edge;
                edge.Next2 = edge.Prev2 = edge;
            }
            else
            {
                edge.Next2 = vert2.Edge.Next(vert2);
                edge.Prev2 = vert2.Edge;
                edge.Next2?.SetPrev(vert2, edge);
                edge.Prev2?.SetNext(vert2, edge);
            }

            return edge;
        }

        public Edge AddEdge(int v1, int v2) => AddEdge(Vertices[v1], Vertices[v2]);

        /// <summary>
        /// Add a new face that connects the array of vertices provided.
        /// </summary>
        public Face? AddFace(params Vertex[] fVerts)
        {
            if (fVerts.Length == 0) return null;

            var fEdges = new Edge[fVerts.Length];
            for (int i = 0, iPrev = fVerts.Length - 1; i < fVerts.Length; iPrev = i++)
            {
                fEdges[iPrev] = AddEdge(fVerts[iPrev], fVerts[i]);
            }

            var f = new Face();
            EnsureFaceAttributes(f);
            Faces.Add(f);

            for (int i = 0; i < fVerts.Length; i++)
            {
                var loop = new Loop(fVerts[i], fEdges[i], f);
                EnsureLoopAttributes(loop);
                Loops.Add(loop);

                // Insert loop into face's loop list
                if (f.Loop == null)
                {
                    f.Loop = loop;
                    loop.Next = loop.Prev = loop;
                }
                else
                {
                    loop.Prev = f.Loop;
                    loop.Next = f.Loop.Next;
                    if (f.Loop.Next != null) f.Loop.Next.Prev = loop;
                    f.Loop.Next = loop;
                    f.Loop = loop;
                }

                // Insert loop into edge's radial list
                var e = fEdges[i];
                if (e.Loop == null)
                {
                    e.Loop = loop;
                    loop.RadialNext = loop.RadialPrev = loop;
                }
                else
                {
                    loop.RadialPrev = e.Loop;
                    loop.RadialNext = e.Loop.RadialNext;
                    if (e.Loop.RadialNext != null) e.Loop.RadialNext.RadialPrev = loop;
                    e.Loop.RadialNext = loop;
                    e.Loop = loop;
                }
            }

            f.VertCount = fVerts.Length;
            return f;
        }

        public Face? AddFace(params int[] indices) => AddFace(indices.Select(i => Vertices[i]).ToArray());

        /// <summary>
        /// Find an edge that links vert1 to vert2.
        /// </summary>
        public Edge? FindEdge(Vertex vert1, Vertex vert2)
        {
            Debug.Assert(vert1 != vert2);
            if (vert1.Edge == null || vert2.Edge == null) return null;

            var e1 = vert1.Edge;
            var e2 = vert2.Edge;
            do
            {
                if (e1.ContainsVertex(vert2)) return e1;
                if (e2.ContainsVertex(vert1)) return e2;
                e1 = e1.Next(vert1);
                e2 = e2.Next(vert2);
            } while (e1 != vert1.Edge && e2 != vert2.Edge);
            return null;
        }

        /// <summary>
        /// Remove a vertex from the mesh (also removes all connected edges/loops/faces).
        /// </summary>
        public void RemoveVertex(Vertex v)
        {
            while (v.Edge != null)
                RemoveEdge(v.Edge);
            Vertices.Remove(v);
        }

        /// <summary>
        /// Remove an edge from the mesh (also removes all associated loops/faces).
        /// </summary>
        public void RemoveEdge(Edge e)
        {
            while (e.Loop != null)
                RemoveLoop(e.Loop);

            // Remove reference in vertices
            if (e == e.Vert1.Edge) e.Vert1.Edge = e.Next1 != e ? e.Next1 : null;
            if (e == e.Vert2.Edge) e.Vert2.Edge = e.Next2 != e ? e.Next2 : null;

            // Remove from linked lists
            if (e.Prev1 != null) e.Prev1.SetNext(e.Vert1, e.Next1);
            if (e.Next1 != null) e.Next1.SetPrev(e.Vert1, e.Prev1);
            if (e.Prev2 != null) e.Prev2.SetNext(e.Vert2, e.Next2);
            if (e.Next2 != null) e.Next2.SetPrev(e.Vert2, e.Prev2);

            Edges.Remove(e);
        }

        private void RemoveLoop(Loop l)
        {
            var face = l.Face;
            if (face != null)
            {
                RemoveFace(face);
                return;
            }

            // Remove from radial list
            if (l.RadialNext == l)
            {
                l.Edge.Loop = null;
            }
            else
            {
                if (l.RadialPrev != null) l.RadialPrev.RadialNext = l.RadialNext;
                if (l.RadialNext != null) l.RadialNext.RadialPrev = l.RadialPrev;
                if (l.Edge.Loop == l) l.Edge.Loop = l.RadialNext;
            }

            Loops.Remove(l);
        }

        /// <summary>
        /// Remove a face from the mesh.
        /// </summary>
        public void RemoveFace(Face f)
        {
            var l = f.Loop;
            if (l != null)
            {
                var nextL = l;
                do
                {
                    var currentL = nextL;
                    nextL = currentL.Next;
                    currentL.Face = null!; // Prevent recursion
                    RemoveLoop(currentL);
                } while (nextL != f.Loop && nextL != null);
            }
            Faces.Remove(f);
        }

        #endregion

        #region Attribute System

        public enum AttributeBaseType { Int, Float }

        public class AttributeType
        {
            public AttributeBaseType BaseType;
            public int Dimensions;

            public bool CheckValue(AttributeValue value)
            {
                Debug.Assert(Dimensions > 0);
                return BaseType switch
                {
                    AttributeBaseType.Int => value is IntAttributeValue ival && ival.Data.Length == Dimensions,
                    AttributeBaseType.Float => value is FloatAttributeValue fval && fval.Data.Length == Dimensions,
                    _ => false
                };
            }
        }

        public abstract class AttributeValue
        {
            public static AttributeValue Copy(AttributeValue value)
            {
                if (value is IntAttributeValue ival)
                {
                    var data = new int[ival.Data.Length];
                    ival.Data.CopyTo(data, 0);
                    return new IntAttributeValue { Data = data };
                }
                if (value is FloatAttributeValue fval)
                {
                    var data = new float[fval.Data.Length];
                    fval.Data.CopyTo(data, 0);
                    return new FloatAttributeValue { Data = data };
                }
                throw new InvalidOperationException("Unknown attribute value type");
            }

            public IntAttributeValue? AsInt() => this as IntAttributeValue;
            public FloatAttributeValue? AsFloat() => this as FloatAttributeValue;
        }

        public class IntAttributeValue : AttributeValue
        {
            public int[] Data = Array.Empty<int>();

            public IntAttributeValue() { }
            public IntAttributeValue(params int[] values) { Data = values; }
        }

        public class FloatAttributeValue : AttributeValue
        {
            public float[] Data = Array.Empty<float>();

            public FloatAttributeValue() { }
            public FloatAttributeValue(params float[] values) { Data = values; }

            public Float3 AsVector3() => new Float3(
                Data.Length > 0 ? Data[0] : 0,
                Data.Length > 1 ? Data[1] : 0,
                Data.Length > 2 ? Data[2] : 0
            );

            public void FromVector3(Float3 v)
            {
                if (Data.Length >= 3)
                {
                    Data[0] = v.X;
                    Data[1] = v.Y;
                    Data[2] = v.Z;
                }
            }
        }

        public class AttributeDefinition
        {
            public string Name;
            public AttributeType Type;
            public AttributeValue DefaultValue;

            public AttributeDefinition(string name, AttributeBaseType baseType, int dimensions)
            {
                Name = name;
                Type = new AttributeType { BaseType = baseType, Dimensions = dimensions };
                DefaultValue = NullValue();
            }

            public AttributeValue NullValue()
            {
                return Type.BaseType switch
                {
                    AttributeBaseType.Int => new IntAttributeValue { Data = new int[Type.Dimensions] },
                    AttributeBaseType.Float => new FloatAttributeValue { Data = new float[Type.Dimensions] },
                    _ => throw new InvalidOperationException()
                };
            }
        }

        #endregion

        #region Attribute Management

        public bool HasVertexAttribute(string name) => VertexAttributes.Any(a => a.Name == name);
        public AttributeDefinition AddVertexAttribute(string name, AttributeBaseType baseType, int dimensions)
        {
            var attrib = new AttributeDefinition(name, baseType, dimensions);
            if (HasVertexAttribute(name)) return attrib;
            VertexAttributes.Add(attrib);
            foreach (var v in Vertices)
                v.Attributes[attrib.Name] = AttributeValue.Copy(attrib.DefaultValue);
            return attrib;
        }

        private void EnsureVertexAttributes(Vertex v)
        {
            foreach (var attr in VertexAttributes)
            {
                if (!v.Attributes.ContainsKey(attr.Name) || !attr.Type.CheckValue(v.Attributes[attr.Name]))
                    v.Attributes[attr.Name] = AttributeValue.Copy(attr.DefaultValue);
            }
        }

        public bool HasEdgeAttribute(string name) => EdgeAttributes.Any(a => a.Name == name);
        public AttributeDefinition AddEdgeAttribute(string name, AttributeBaseType baseType, int dimensions)
        {
            var attrib = new AttributeDefinition(name, baseType, dimensions);
            if (HasEdgeAttribute(name)) return attrib;
            EdgeAttributes.Add(attrib);
            foreach (var e in Edges)
                e.Attributes[attrib.Name] = AttributeValue.Copy(attrib.DefaultValue);
            return attrib;
        }

        private void EnsureEdgeAttributes(Edge e)
        {
            foreach (var attr in EdgeAttributes)
            {
                if (!e.Attributes.ContainsKey(attr.Name) || !attr.Type.CheckValue(e.Attributes[attr.Name]))
                    e.Attributes[attr.Name] = AttributeValue.Copy(attr.DefaultValue);
            }
        }

        public bool HasLoopAttribute(string name) => LoopAttributes.Any(a => a.Name == name);
        public AttributeDefinition AddLoopAttribute(string name, AttributeBaseType baseType, int dimensions)
        {
            var attrib = new AttributeDefinition(name, baseType, dimensions);
            if (HasLoopAttribute(name)) return attrib;
            LoopAttributes.Add(attrib);
            foreach (var l in Loops)
                l.Attributes[attrib.Name] = AttributeValue.Copy(attrib.DefaultValue);
            return attrib;
        }

        private void EnsureLoopAttributes(Loop l)
        {
            foreach (var attr in LoopAttributes)
            {
                if (!l.Attributes.ContainsKey(attr.Name) || !attr.Type.CheckValue(l.Attributes[attr.Name]))
                    l.Attributes[attr.Name] = AttributeValue.Copy(attr.DefaultValue);
            }
        }

        public bool HasFaceAttribute(string name) => FaceAttributes.Any(a => a.Name == name);
        public AttributeDefinition AddFaceAttribute(string name, AttributeBaseType baseType, int dimensions)
        {
            var attrib = new AttributeDefinition(name, baseType, dimensions);
            if (HasFaceAttribute(name)) return attrib;
            FaceAttributes.Add(attrib);
            foreach (var f in Faces)
                f.Attributes[attrib.Name] = AttributeValue.Copy(attrib.DefaultValue);
            return attrib;
        }

        private void EnsureFaceAttributes(Face f)
        {
            foreach (var attr in FaceAttributes)
            {
                if (!f.Attributes.ContainsKey(attr.Name) || !attr.Type.CheckValue(f.Attributes[attr.Name]))
                    f.Attributes[attr.Name] = AttributeValue.Copy(attr.DefaultValue);
            }
        }

        #endregion

        #region Query

        /// <summary>
        /// Gets the axis-aligned bounding box of the entire geometry.
        /// </summary>
        public AABB GetAABB()
        {
            if (Vertices.Count == 0)
                return new AABB();

            var min = Vertices[0].Point;
            var max = Vertices[0].Point;

            foreach (var vertex in Vertices)
            {
                min = new Float3(
                    Maths.Min(min.X, vertex.Point.X),
                    Maths.Min(min.Y, vertex.Point.Y),
                    Maths.Min(min.Z, vertex.Point.Z)
                );
                max = new Float3(
                    Maths.Max(max.X, vertex.Point.X),
                    Maths.Max(max.Y, vertex.Point.Y),
                    Maths.Max(max.Z, vertex.Point.Z)
                );
            }

            return new AABB(min, max);
        }

        #endregion

        #region Conversion to Triangle Mesh

        /// <summary>
        /// Simple triangle mesh representation for rendering.
        /// </summary>
        public struct TriangleMesh
        {
            public Float3[] Vertices;
            public uint[] Indices;
        }

        /// <summary>
        /// Simple line mesh representation for rendering.
        /// </summary>
        public struct LineMesh
        {
            public Float3[] Vertices;
            public uint[] Indices;
        }

        /// <summary>
        /// Convert all faces to triangles for rendering.
        /// Triangulates n-gons using simple fan triangulation from the first vertex.
        /// </summary>
        public TriangleMesh ToTriangleMesh()
        {
            var vertices = new List<Float3>();
            var indices = new List<uint>();

            foreach (var face in Faces)
            {
                var faceVerts = face.NeighborVertices();
                if (faceVerts.Count < 3) continue;

                // Fan triangulation from first vertex
                for (int i = 1; i < faceVerts.Count - 1; i++)
                {
                    uint baseIndex = (uint)vertices.Count;
                    vertices.Add(faceVerts[0].Point);
                    vertices.Add(faceVerts[i].Point);
                    vertices.Add(faceVerts[i + 1].Point);

                    indices.Add(baseIndex);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 1);
                }
            }

            return new TriangleMesh { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
        }

        /// <summary>
        /// Convert all edges to lines for wireframe rendering.
        /// </summary>
        public LineMesh ToLineMesh()
        {
            var vertices = new List<Float3>();
            var indices = new List<uint>();

            foreach (var edge in Edges)
            {
                uint baseIndex = (uint)vertices.Count;
                vertices.Add(edge.Vert1.Point);
                vertices.Add(edge.Vert2.Point);
                indices.Add(baseIndex);
                indices.Add(baseIndex + 1);
            }

            return new LineMesh { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
        }

        #endregion

    }
}
