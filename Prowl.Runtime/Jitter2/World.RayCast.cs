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
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace Jitter2;

public partial class World
{
    /// <summary>
    /// Preliminary result of the ray cast.
    /// </summary>
    public struct RayCastResult
    {
        public Shape Entity;
        public float Fraction;
        public JVector Normal;
        public bool Hit;
    }

    /// <summary>
    /// Post-filter delegate.
    /// </summary>
    /// <returns>False if the hit should be filtered out.</returns>
    public delegate bool RayCastFilterPost(RayCastResult result);

    /// <summary>
    /// Pre-filter delegate.
    /// </summary>
    /// <returns>False if the hit should be filtered out.</returns>
    public delegate bool RayCastFilterPre(Shape result);

    private struct Ray
    {
        public readonly JVector Origin;
        public readonly JVector Direction;

        public RayCastFilterPost? FilterPost;
        public RayCastFilterPre? FilterPre;

        public readonly float Lambda;

        public Ray(in JVector origin, in JVector direction)
        {
            Origin = origin;
            Direction = direction;
            FilterPost = null;
            FilterPre = null;
            Lambda = float.MaxValue;
        }
    }

    [ThreadStatic] private static Stack<int>? stack;

    /// <summary>
    /// Ray cast against a single shape. See <see cref="World.RayCast(JVector, JVector, RayCastFilterPre?, RayCastFilterPost?, out Shape, out JVector, out float)"/>
    /// for a ray cast against the world.
    /// </summary>
    /// <param name="shape">Shape to test.</param>
    /// <param name="origin">Origin of the ray.</param>
    /// <param name="direction">Direction of the ray. Does not have to be normalized.</param>
    /// <param name="normal">The normal of the surface where the ray hits. Zero if ray does not hit.</param>
    /// <param name="fraction">Distance from the origin to the ray hit point in units of the ray's direction.</param>
    /// <returns>True if the ray hits, false otherwise.</returns>
    public bool RayCast(Shape shape, JVector origin, JVector direction, out JVector normal, out float fraction)
    {
        if (shape.RigidBody == null)
        {
            return NarrowPhase.RayCast(shape, origin, direction, out fraction, out normal);
        }

        ref RigidBodyData body = ref shape.RigidBody.Data;

        if (shape is TriangleShape tms)
        {
            // special case: ray-triangle, use linear algebra.
            tms.GetWorldVertices(out JVector a, out JVector b, out JVector c);
            bool result = CollisionHelper.RayTriangle(a, b, c, origin, direction, out fraction, out normal);
            return result;
        }

        return NarrowPhase.RayCast(shape, body.Orientation, body.Position,
            origin, direction, out fraction, out normal);
    }

    /// <summary>
    /// Ray cast against the world.
    /// </summary>
    /// <param name="origin">Origin of the ray.</param>
    /// <param name="direction">Direction of the ray. Does not have to be normalized.</param>
    /// <param name="pre">Optional pre-filter which allows to skip shapes in the detection.</param>
    /// <param name="post">Optional post-filter which allows to skip detections.</param>
    /// <param name="shape">The shape which was hit.</param>
    /// <param name="normal">The normal of the surface where the ray hits. Zero if ray does not hit.</param>
    /// <param name="fraction">Distance from the origin to the ray hit point in units of the ray's directin.</param>
    /// <returns>True if the ray hits, false otherwise.</returns>
    public bool RayCast(JVector origin, JVector direction, RayCastFilterPre? pre, RayCastFilterPost? post,
        out Shape? shape, out JVector normal, out float fraction)
    {
        Ray ray = new(origin, direction)
        {
            FilterPre = pre,
            FilterPost = post
        };
        var result = QueryRay(ray);
        shape = result.Entity;
        normal = result.Normal;
        fraction = result.Fraction;
        return result.Hit;
    }

    private RayCastResult QueryRay(in Ray ray)
    {
        if (DynamicTree.Root == -1) return new RayCastResult();

        stack ??= new Stack<int>(256);

        stack.Push(DynamicTree.Root);

        RayCastResult result = new();
        result.Fraction = ray.Lambda;

        while (stack.Count > 0)
        {
            int pop = stack.Pop();

            ref DynamicTree<Shape>.Node node = ref DynamicTree.Nodes[pop];

            if (node.IsLeaf)
            {
                if (ray.FilterPre != null && !ray.FilterPre(node.Proxy)) continue;

                RayCastResult res = RayCast(node.Proxy, ray);
                if (res.Hit && res.Fraction < result.Fraction)
                {
                    if (ray.FilterPost != null && !ray.FilterPost(res)) continue;
                    result = res;
                }

                continue;
            }

            ref DynamicTree<Shape>.Node lnode = ref DynamicTree.Nodes[node.Left];
            ref DynamicTree<Shape>.Node rnode = ref DynamicTree.Nodes[node.Right];

            bool lres = lnode.ExpandedBox.RayIntersect(ray.Origin, ray.Direction, out float enterl);
            bool rres = rnode.ExpandedBox.RayIntersect(ray.Origin, ray.Direction, out float enterr);

            if (enterl > result.Fraction) lres = false;
            if (enterr > result.Fraction) rres = false;

            if (lres && rres)
            {
                if (enterl < enterr)
                {
                    stack.Push(node.Right);
                    stack.Push(node.Left);
                }
                else
                {
                    stack.Push(node.Left);
                    stack.Push(node.Right);
                }
            }
            else
            {
                if (lres) stack.Push(node.Left);
                if (rres) stack.Push(node.Right);
            }
        }

        return result;
    }

    private RayCastResult RayCast(Shape entity, in Ray ray)
    {
        RayCastResult result;
        result.Hit = RayCast(entity, ray.Origin, ray.Direction, out result.Normal, out result.Fraction);
        result.Entity = entity;

        return result;
    }
}