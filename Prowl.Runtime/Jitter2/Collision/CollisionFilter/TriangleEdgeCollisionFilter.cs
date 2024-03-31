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

// #define DEBUG_EDGEFILTER

using System;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

namespace Jitter2.Collision;

/// <summary>
/// Level geometry is often represented by multiple instances of <see cref="Collision.Shapes.TriangleShape"/>
/// added to a <see cref="Dynamics.RigidBody"/>. Other rigid bodies sliding over these triangles
/// might encounter "internal edges", resulting in jitter. The <see cref="TriangleEdgeCollisionFilter"/>
/// implements the <see cref="INarrowPhaseFilter"/> to help filter out these internal edges.
/// </summary>
public class TriangleEdgeCollisionFilter : INarrowPhaseFilter
{
    /// <summary>
    /// A tweakable parameter. Collision points that are closer than this value to a triangle edge 
    /// are considered as edge collisions and might be modified or discarded entirely.
    /// </summary>
    public float EdgeThreshold { get; set; } = 0.05f;

    private float cosAT = 0.99f;

    /// <summary>
    /// A tweakable parameter that defines the threshold to determine when two normals 
    /// are considered identical.
    /// </summary>
    public JAngle AngleThreshold
    {
        get => JAngle.FromRadiant(MathF.Acos(cosAT));
        set => cosAT = MathF.Cos(value.Radiant);
    }

    /// <inheritdoc />
    public bool Filter(Shape shapeA, Shape shapeB, ref JVector pAA, ref JVector pBB, ref JVector normal,
        ref float penetration)
    {
        TriangleShape? ts1 = shapeA as TriangleShape;
        TriangleShape? ts2 = shapeB as TriangleShape;

        bool c1 = ts1 != null;
        bool c2 = ts2 != null;

        // both shapes are triangles or both of them are not -> return
        if (c1 == c2) return true;

        TriangleShape tshape;
        JVector collP;

        if (c1)
        {
            tshape = ts1!;
            collP = pAA;
        }
        else
        {
            tshape = ts2!;
            collP = pBB;
        }

        if (shapeA.RigidBody == null || shapeB.RigidBody == null)
        {
            return true;
        }

        ref var triangle = ref tshape.Mesh.Indices[tshape.Index];

        JVector tnormal = triangle.Normal;
        tnormal = JVector.Transform(tnormal, tshape.RigidBody!.Data.Orientation);

        if (c2) tnormal.Negate();

        if (JVector.Dot(normal, tnormal) < -cosAT) normal.Negate();

        tshape.GetWorldVertices(out JVector a, out JVector b, out JVector c);

        JVector n, pma;
        float d0, d1, d2;

        // TODO: this can be optimized
        n = b - a;
        pma = collP - a;
        d0 = (pma - JVector.Dot(pma, n) * n * (1.0f / n.LengthSquared())).LengthSquared();

        n = c - a;
        pma = collP - a;
        d1 = (pma - JVector.Dot(pma, n) * n * (1.0f / n.LengthSquared())).LengthSquared();

        n = c - b;
        pma = collP - b;
        d2 = (pma - JVector.Dot(pma, n) * n * (1.0f / n.LengthSquared())).LengthSquared();

        if (MathF.Min(MathF.Min(d0, d1), d2) > EdgeThreshold) return true;

        JVector nnormal;

        if (d0 < d1 && d0 < d2)
        {
            if (triangle.NeighborC == -1) return true;
            nnormal = tshape.Mesh.Indices[triangle.NeighborC].Normal;
        }
        else if (d1 <= d0 && d1 < d2)
        {
            if (triangle.NeighborB == -1) return true;
            nnormal = tshape.Mesh.Indices[triangle.NeighborB].Normal;
        }
        else
        {
            if (triangle.NeighborA == -1) return true;
            nnormal = tshape.Mesh.Indices[triangle.NeighborA].Normal;
        }

        nnormal = JVector.Transform(nnormal, tshape.RigidBody.Data.Orientation);

        if (c2)
        {
            nnormal.Negate();
        }

        // now the fun part
        //
        // we have a collision close to an edge, with
        //
        // tnormal -> the triangle normal where collision occurred
        // nnormal -> the normal of neighbouring triangle
        // normal  -> the collision normal
        if (JVector.Dot(tnormal, nnormal) > cosAT)
        {
            // tnormal and nnormal are the same
            // --------------------------------
            float f5 = JVector.Dot(normal, nnormal);
            float f6 = JVector.Dot(normal, tnormal);

            if (f5 > f6)
            {
#if DEBUG_EDGEFILTER
                    if(f5 < cosAT) Console.WriteLine($"case #1.1: dropping; normal {normal} -> {nnormal}");
                    else Console.WriteLine($"case #1.2: adjusting; normal {normal} -> {nnormal}");
#endif
                if (f5 < cosAT)
                {
                    return false;
                }

                penetration *= f5;
                normal = nnormal;
            }
            else
            {
#if DEBUG_EDGEFILTER
                if(f6 < cosAT) Console.WriteLine($"case #1.1: dropping; normal {normal} -> {tnormal}");
                else Console.WriteLine($"case #1.2: adjusting; normal {normal} -> {tnormal}");
#endif
                if (f6 < cosAT)
                {
                    return false;
                }

                penetration *= f6;
                normal = tnormal;
            }

            return true;
        }
        // nnormal and tnormal are different
        // ----------------------------------

        // 1st step, project the normal onto the plane given by tnormal and nnormal
        JVector cross = nnormal % tnormal;
        JVector proj = normal - cross * normal * cross;

        // 2nd step, determine if "proj" is between nnormal and tnormal
        //
        //    /    nnormal
        //   /
        //  /
        //  -----  proj
        // \
        //  \
        //   \     tnormal
        float f1 = proj % nnormal * cross;
        float f2 = proj % tnormal * cross;

        bool between = f1 * f2 <= 0.0f;

        if (!between)
        {
            // not in-between, clamp normal
            float f3 = JVector.Dot(normal, nnormal);
            float f4 = JVector.Dot(normal, tnormal);

            if (f3 > f4)
            {
#if DEBUG_EDGEFILTER
                    if(f3 < cosAT) Console.WriteLine($"case #2.1: adjusting; normal {normal} -> {nnormal}");
                    else Console.WriteLine($"case #2.2: adjusting; normal {normal} -> {nnormal}");
#endif
                if (f3 < cosAT)
                {
                    return false;
                }

                penetration *= f3;
                normal = nnormal;
            }
            else
            {
#if DEBUG_EDGEFILTER
                    if (f4 < cosAT) Console.WriteLine($"case #2.1: dropping; normal {normal} -> {tnormal}");
                    else Console.WriteLine($"case #2.2: adjusting; normal {normal} -> {tnormal}");
#endif
                if (f4 < cosAT)
                {
                    return false;
                }

                penetration *= f4;
                normal = tnormal;
            }
        }

        return true;
    }
}