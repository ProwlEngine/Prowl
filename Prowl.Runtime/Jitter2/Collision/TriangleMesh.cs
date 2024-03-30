/*
 * Copyright (c) Thorben Linneweber and others
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Jitter2.LinearMath;

namespace Jitter2.Collision;

/// <summary>
/// Represents an exception thrown when a degenerate triangle is detected.
/// </summary>
public class DegenerateTriangleException : Exception
{
    public DegenerateTriangleException()
    {
    }

    public DegenerateTriangleException(string message) : base(message)
    {
    }

    public DegenerateTriangleException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Encapsulates the data of a triangle mesh. An instance of this can be supplied to the <see cref="Jitter2.Collision.Shapes.TriangleShape"/>.
/// The triangles within this class contain indices pointing to neighboring triangles.
/// </summary>
public class TriangleMesh
{
    private struct Edge : IEquatable<Edge>
    {
        public int IndexA;
        public int IndexB;

        public Edge(int indexA, int indexB)
        {
            IndexA = indexA;
            IndexB = indexB;
        }

        public override bool Equals(object? obj)
        {
            return obj is Edge other && Equals(other);
        }

        public override int GetHashCode()
        {
            return IndexA + 228771 * IndexB;
        }

        public bool Equals(Edge other)
        {
            return IndexA == other.IndexA && IndexB == other.IndexB;
        }
    }

    /// <summary>
    /// This structure encapsulates vertex indices along with indices pointing to
    /// neighboring triangles.
    /// </summary>
    public struct Triangle
    {
        public int IndexA;
        public int IndexB;
        public int IndexC;

        public int NeighborA;
        public int NeighborB;
        public int NeighborC;

        public JVector Normal;

        public Triangle(int a, int b, int c)
        {
#if NET6_0
            Normal = new JVector();
#endif

            IndexA = a;
            IndexB = b;
            IndexC = c;
            NeighborA = -1;
            NeighborB = -1;
            NeighborC = -1;
        }
    }

    /// <summary>
    /// An array containing the vertices that comprise the triangle mesh.
    /// </summary>
    public readonly JVector[] Vertices;

    /// <summary>
    /// The triangles constituting the triangle mesh.
    /// </summary>
    public readonly Triangle[] Indices;

    /// <summary>
    /// Initializes a new instance of the triangle mesh.
    /// </summary>
    /// <param name="triangles">The triangles to be added. The reference to the list can be
    /// modified/deleted after invoking this constructor.</param>
    /// <exception cref="DegenerateTriangleException">This is thrown if the triangle mesh contains one or
    /// more degenerate triangles.</exception>
    public TriangleMesh(List<JTriangle> triangles)
    {
        Dictionary<JVector, int> tmpIndices = new();
        List<JVector> tmpVertices = new();

        // 1. step: build indices and vertices for triangles (JTriangle contains raw x, y, z coordinates).

        Indices = new Triangle[triangles.Count];

        int PushVector(JVector v)
        {
            if (!tmpIndices.TryGetValue(v, out int result))
            {
                result = tmpVertices.Count;
                tmpIndices.Add(v, result);
                tmpVertices.Add(v);
            }

            return result;
        }

        for (int i = 0; i < triangles.Count; i++)
        {
            JTriangle tti = triangles[i];

            int a = PushVector(tti.V0);
            int b = PushVector(tti.V1);
            int c = PushVector(tti.V2);

            Indices[i] = new Triangle(a, b, c);
        }

        Vertices = tmpVertices.ToArray();

        // 2. step: Identify the neighbors.

        Dictionary<Edge, int> tmpEdges = new();

        int GetEdge(Edge e)
        {
            if (!tmpEdges.TryGetValue(e, out int res))
            {
                return -1;
            }

            return res;
        }

        for (int i = 0; i < Indices.Length; i++)
        {
            tmpEdges.TryAdd(new Edge(Indices[i].IndexA, Indices[i].IndexB), i);
            tmpEdges.TryAdd(new Edge(Indices[i].IndexB, Indices[i].IndexC), i);
            tmpEdges.TryAdd(new Edge(Indices[i].IndexC, Indices[i].IndexA), i);
        }

        for (int i = 0; i < Indices.Length; i++)
        {
            Indices[i].NeighborA = GetEdge(new Edge(Indices[i].IndexC, Indices[i].IndexB));
            Indices[i].NeighborB = GetEdge(new Edge(Indices[i].IndexA, Indices[i].IndexC));
            Indices[i].NeighborC = GetEdge(new Edge(Indices[i].IndexB, Indices[i].IndexA));
        }

        for (int i = 0; i < Indices.Length; i++)
        {
            JVector A = Vertices[Indices[i].IndexA];
            JVector B = Vertices[Indices[i].IndexB];
            JVector C = Vertices[Indices[i].IndexC];

            JVector normal = (C - A) % (B - A);

            if (MathHelper.CloseToZero(normal, 1e-12f))
            {
                throw new DegenerateTriangleException("Degenerate triangle found in mesh. Try to clean the " +
                                                      "mesh in the editor of your choice first.");
            }

            JVector.Normalize(normal, out Indices[i].Normal);
        }
    }
}