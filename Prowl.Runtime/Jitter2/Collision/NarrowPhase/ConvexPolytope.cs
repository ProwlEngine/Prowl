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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jitter2.LinearMath;
using Jitter2.UnmanagedMemory;

namespace Jitter2.Collision;

/// <summary>
/// Represents a convex polytope builder used in collision detection during the narrow phase.
/// Note: Ensure to call <see cref="ConvexPolytope.InitHeap"/> at least once before utilizing this structure
/// to allocate necessary memory for vertices and triangles.
/// </summary>
public unsafe struct ConvexPolytope
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Triangle
    {
        public short A, B, C;
        public bool FacingOrigin;

        public short this[int i] => ((short*)Unsafe.AsPointer(ref this))[i];

        public JVector Normal;
        public JVector ClosestToOrigin;

        public float NormalSq;
        public float ClosestToOriginSq;
    }

    /// <summary>
    /// Represents a vertex utilized in algorithms that operate on the Minkowski sum of two shapes.
    /// </summary>
    public struct Vertex
    {
        public JVector V;
        public JVector A;
        public JVector B;

        public Vertex(JVector v)
        {
#if NET6_0
            A = new JVector();
            B = new JVector();
#endif
            V = v;
        }
    }

    private struct Edge
    {
        public readonly short A;
        public readonly short B;

        public Edge(short a, short b)
        {
            A = a;
            B = b;
        }

        public static bool Equals(in Edge a, in Edge b)
        {
            return (a.A == b.A && a.B == b.B) || (a.A == b.B && a.B == b.A);
        }
    }

    private const float NumericEpsilon = 1e-16f;

    // (*) Euler-characteristic: V (vertices) - E (edges) + F (faces) = 2
    // We have triangles T instead of faces: F = T
    // and every edge shares two triangles -> T = 2*V - 4
    private const int MaxVertices = 128;
    private const int MaxTriangles = 2 * MaxVertices;

    private Triangle* triangles;
    private Vertex* vertices;

    private short tPointer;
    private short vPointer;

    private bool originEnclosed;

    private JVector center;

    public Span<Triangle> HullTriangles => new Span<Triangle>(triangles, tPointer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ConvexPolytope.Vertex GetVertex(int index)
    {
        System.Diagnostics.Debug.Assert(index < MaxVertices, "Out of bounds.");
        return ref vertices[index];
    }

    /// <summary>
    /// Indicates whether the origin is enclosed within the polyhedron.
    /// Important: This property returns correct values after invoking <see cref="GetClosestTriangle"/>.
    /// </summary>
    public readonly bool OriginEnclosed => originEnclosed;

    /// <summary>
    /// Computes the barycentric coordinates of the origin projected onto a given triangle.
    /// These coordinates are used to retrieve points in A- and B-space.
    /// </summary>
    public void CalculatePoints(in Triangle ctri, out JVector pA, out JVector pB)
    {
        CalcBarycentric(ctri, out JVector bc, !originEnclosed);
        pA = bc.X * vertices[ctri.A].A + bc.Y * vertices[ctri.B].A + bc.Z * vertices[ctri.C].A;
        pB = bc.X * vertices[ctri.A].B + bc.Y * vertices[ctri.B].B + bc.Z * vertices[ctri.C].B;
    }

    private bool CalcBarycentric(in Triangle tri, out JVector result, bool clamp = false)
    {
        bool clamped = false;

        JVector a = vertices[tri.A].V;
        JVector b = vertices[tri.B].V;
        JVector c = vertices[tri.C].V;

        // Calculate the barycentric coordinates of the origin (0,0,0) projected
        // onto the plane of the triangle.
        //
        // [W. Heidrich, Journal of Graphics, GPU, and Game Tools,Volume 10, Issue 3, 2005.]
#pragma warning disable IDE0018
        JVector u, v, w, tmp;
#pragma warning restore IDE0018

        JVector.Subtract(a, b, out u);
        JVector.Subtract(a, c, out v);

        float t = tri.NormalSq;

        JVector.Cross(u, a, out tmp);
        float gamma = JVector.Dot(tmp, tri.Normal) / t;
        JVector.Cross(a, v, out tmp);
        float beta = JVector.Dot(tmp, tri.Normal) / t;
        float alpha = 1.0f - gamma - beta;

        if (clamp)
        {
            // Clamp the projected barycentric coordinates to lie within the triangle,
            // such that the clamped coordinates are closest (euclidean) to the original point.
            //
            // [https://math.stackexchange.com/questions/1092912/find-closest-point-in-triangle-given-barycentric-coordinates-outside]
            if (alpha >= 0.0f && beta < 0.0f)
            {
                t = JVector.Dot(a, u);
                if (gamma < 0.0f && t > 0.0f)
                {
                    beta = MathF.Min(1.0f, t / u.LengthSquared());
                    alpha = 1.0f - beta;
                    gamma = 0.0f;
                }
                else
                {
                    gamma = MathF.Min(1.0f, MathF.Max(0.0f, JVector.Dot(a, v) / v.LengthSquared()));
                    alpha = 1.0f - gamma;
                    beta = 0.0f;
                }

                clamped = true;
            }
            else if (beta >= 0.0f && gamma < 0.0f)
            {
                JVector.Subtract(b, c, out w);
                t = JVector.Dot(b, w);
                if (alpha < 0.0f && t > 0.0f)
                {
                    gamma = MathF.Min(1.0f, t / w.LengthSquared());
                    beta = 1.0f - gamma;
                    alpha = 0.0f;
                }
                else
                {
                    alpha = MathF.Min(1.0f, MathF.Max(0.0f, -JVector.Dot(b, u) / u.LengthSquared()));
                    beta = 1.0f - alpha;
                    gamma = 0.0f;
                }

                clamped = true;
            }
            else if (gamma >= 0.0f && alpha < 0.0f)
            {
                JVector.Subtract(b, c, out w);
                t = -JVector.Dot(c, v);
                if (beta < 0.0f && t > 0.0f)
                {
                    alpha = MathF.Min(1.0f, t / v.LengthSquared());
                    gamma = 1.0f - alpha;
                    beta = 0.0f;
                }
                else
                {
                    beta = MathF.Min(1.0f, MathF.Max(0.0f, -JVector.Dot(c, w) / w.LengthSquared()));
                    gamma = 1.0f - beta;
                    alpha = 0.0f;
                }

                clamped = true;
            }
        }

        result.X = alpha;
        result.Y = beta;
        result.Z = gamma;
        return clamped;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly bool IsLit(int candidate, int w)
    {
        // Checks if the triangle would be lit, if there would
        // be a light at the origin.
        ref Triangle tr = ref triangles[candidate];
        JVector deltaA = vertices[w].V - vertices[tr.A].V;
        return JVector.Dot(deltaA, tr.Normal) > 0;
    }

    private bool CreateTriangle(short a, short b, short c)
    {
        ref Triangle triangle = ref triangles[tPointer];
        triangle.A = a;
        triangle.B = b;
        triangle.C = c;

        JVector.Subtract(vertices[a].V, vertices[b].V, out JVector u);
        JVector.Subtract(vertices[a].V, vertices[c].V, out JVector v);
        JVector.Cross(u, v, out triangle.Normal);
        triangle.NormalSq = triangle.Normal.LengthSquared();

        // return on degenerate triangles
        if (triangle.NormalSq < NumericEpsilon)
        {
            return false;
        }

        // do we need to flip the triangle? (the origin of the md has to be enclosed)
        float delta = JVector.Dot(triangle.Normal, vertices[a].V - center);

        if (delta < 0)
        {
            (triangle.A, triangle.B) = (triangle.B, triangle.A);
            triangle.Normal.Negate();
        }

        delta = JVector.Dot(triangle.Normal, vertices[a].V);
        triangle.FacingOrigin = delta >= 0.0f;

        if (!originEnclosed && CalcBarycentric(triangle, out JVector bc, true))
        {
            triangle.ClosestToOrigin = bc.X * vertices[triangle.A].V + bc.Y * vertices[triangle.B].V +
                                       bc.Z * vertices[triangle.C].V;
            triangle.ClosestToOriginSq = triangle.ClosestToOrigin.LengthSquared();
        }
        else
        {
            JVector.Multiply(triangle.Normal, delta / triangle.NormalSq, out triangle.ClosestToOrigin);
            triangle.ClosestToOriginSq = triangle.ClosestToOrigin.LengthSquared();
        }

        tPointer++;
        return true;
    }

    /// <summary>
    /// Iterates through all triangles of the convex polytope and returns the one closest
    /// to the origin (0, 0, 0), based on the minimum distance.
    /// </summary>
    public ref Triangle GetClosestTriangle()
    {
        int closestIndex = -1;
        float currentMin = float.MaxValue;

        originEnclosed = true;

        for (int i = 0; i < tPointer; i++)
        {
            if (triangles[i].ClosestToOriginSq < currentMin)
            {
                currentMin = triangles[i].ClosestToOriginSq;
                closestIndex = i;
            }

            if (!triangles[i].FacingOrigin) originEnclosed = false;
        }

        return ref triangles[closestIndex];
    }

    /// <summary>
    /// Initializes the structure with a tetrahedron formed using the first four vertices in the <see cref="vertices"/> array.
    /// </summary>
    public void InitTetrahedron()
    {
        originEnclosed = false;
        vPointer = 4;
        tPointer = 0;

        center = 0.25f * (vertices[0].V + vertices[1].V + vertices[2].V + vertices[3].V);

        CreateTriangle(0, 2, 1);
        CreateTriangle(0, 1, 3);
        CreateTriangle(0, 3, 2);
        CreateTriangle(1, 2, 3);
    }

    /// <summary>
    /// Creates a small tetrahedron that encapsulates the specified point.
    /// </summary>
    public void InitTetrahedron(in JVector point)
    {
        originEnclosed = false;
        vPointer = 4;
        tPointer = 0;
        center = point;

        const float scale = 1e-2f; // minkowski sums not allowed to be thinner
        vertices[0] = new Vertex(center + scale * new JVector(MathF.Sqrt(8.0f / 9.0f), 0.0f, -1.0f / 3.0f));
        vertices[1] = new Vertex(center + scale * new JVector(-MathF.Sqrt(2.0f / 9.0f), MathF.Sqrt(2.0f / 3.0f), -1.0f / 3.0f));
        vertices[2] = new Vertex(center + scale * new JVector(-MathF.Sqrt(2.0f / 9.0f), -MathF.Sqrt(2.0f / 3.0f), -1.0f / 3.0f));
        vertices[3] = new Vertex(center + scale * new JVector(0.0f, 0.0f, 1.0f));

        CreateTriangle(2, 0, 1);
        CreateTriangle(1, 0, 3);
        CreateTriangle(3, 0, 2);
        CreateTriangle(2, 1, 3);
    }

    /// <summary>
    /// Initializes the memory for <see cref="vertices"/> and <see cref="triangles"/>.
    /// Must be invoked prior to calling any other method in this struct.
    /// Note: Can be called multiple times; however, initialization occurs only once.
    /// </summary>
    public void InitHeap()
    {
        if (vertices == (void*)0)
        {
            vertices = MemoryHelper.AllocateHeap<Vertex>(MaxVertices);
            triangles = MemoryHelper.AllocateHeap<Triangle>(MaxTriangles);
        }
    }

    /// <summary>
    /// Incorporates a single point into the polyhedron, disregarding A- and B-space.
    /// This operation contrasts with <see cref="AddVertex"/>.
    /// </summary>
    /// <returns>Indicates whether the polyhedron successfully incorporated the new point.</returns>
    public bool AddPoint(in JVector point)
    {
        Unsafe.SkipInit(out Vertex v);
        v.V = point;
        return AddVertex(v);
    }

    /// <summary>
    /// Adds a vertex to the polyhedron. Note: This operation invalidates the reference
    /// returned by previous calls to <see cref="GetClosestTriangle"/>, regardless of
    /// the return value of this method.
    /// </summary>
    /// <returns>Indicates whether the polyhedron successfully incorporated the new vertex.</returns>
    public bool AddVertex(in Vertex vertex)
    {
        System.Diagnostics.Debug.Assert(vPointer < MaxVertices, "Maximum number of vertices exceeded.");

        // see (*) above
        Edge* edges = stackalloc Edge[MaxVertices * 3 / 2];

        vertices[vPointer] = vertex;

        int ePointer = 0;
        for (int index = tPointer; index-- > 0;)
        {
            if (!IsLit(index, vPointer)) continue;

            for (int k = 0; k < 3; k++)
            {
                Edge edge = new Edge(triangles[index][(k + 0) % 3], triangles[index][(k + 1) % 3]);
                bool added = true;
                for (int e = ePointer; e-- > 0;)
                {
                    if (Edge.Equals(edges[e], edge))
                    {
                        edges[e] = edges[--ePointer];
                        added = false;
                    }
                }

                if (added) edges[ePointer++] = edge;
            }

            triangles[index] = triangles[--tPointer];
        }

        if (ePointer == 0) return false;

        for (int i = 0; i < ePointer; i++)
        {
            if (!CreateTriangle(edges[i].A, edges[i].B, vPointer))
                return false;
        }

        vPointer++;
        return true;
    }
}