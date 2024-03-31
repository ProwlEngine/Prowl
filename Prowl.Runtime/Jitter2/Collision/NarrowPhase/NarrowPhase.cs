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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Jitter2.LinearMath;

namespace Jitter2.Collision;

/// <summary>
/// Provides efficient and accurate collision detection algorithms for general convex objects
/// implicitly defined by a support function, see <see cref="ISupportMap"/>.
/// </summary>
public static class NarrowPhase
{
    private const float NumericEpsilon = 1e-16f;

    private unsafe struct Solver
    {
        private ConvexPolytope convexPolytope;

        public bool PointTest(ISupportMap supportA, in JVector origin)
        {
            const float CollideEpsilon = 1e-4f;
            const int MaxIter = 34;

            JVector x = origin;

            var center = supportA.GeometricCenter;
            JVector v = x - center;

            convexPolytope.InitHeap();
            convexPolytope.InitTetrahedron(v);

            int maxIter = MaxIter;

            float distSq = v.LengthSquared();

            while (distSq > CollideEpsilon * CollideEpsilon && maxIter-- != 0)
            {
                supportA.SupportMap(v, out JVector p);
                JVector.Subtract(x, p, out JVector w);

                float vw = JVector.Dot(v, w);

                if (vw >= 0.0f)
                {
                    return false;
                }

                if (!convexPolytope.AddPoint(w))
                {
                    goto converged;
                }

                v = convexPolytope.GetClosestTriangle().ClosestToOrigin;

                if (convexPolytope.OriginEnclosed) return true;

                distSq = v.LengthSquared();
            }

            converged:

            return true;
        }

        public bool RayCast(ISupportMap supportA, in JVector origin, in JVector direction, out float fraction, out JVector normal)
        {
            const float CollideEpsilon = 1e-4f;
            const int MaxIter = 34;

            normal = JVector.Zero;
            fraction = float.PositiveInfinity;

            float lambda = 0.0f;

            JVector r = direction;
            JVector x = origin;

            var center = supportA.GeometricCenter;
            JVector v = x - center;

            convexPolytope.InitHeap();
            convexPolytope.InitTetrahedron(v);

            int maxIter = MaxIter;

            float distSq = v.LengthSquared();

            while (distSq > CollideEpsilon * CollideEpsilon && maxIter-- != 0)
            {
                supportA.SupportMap(v, out JVector p);

                JVector.Subtract(x, p, out JVector w);

                float VdotW = JVector.Dot(v, w);

                if (VdotW > 0.0f)
                {
                    float VdotR = JVector.Dot(v, r);

                    if (VdotR >= -NumericEpsilon)
                    {
                        return false;
                    }

                    lambda -= VdotW / VdotR;

                    JVector.Multiply(r, lambda, out x);
                    JVector.Add(origin, x, out x);
                    JVector.Subtract(x, p, out w);
                    normal = v;
                }

                if (!convexPolytope.AddPoint(w))
                {
                    goto converged;
                }

                v = convexPolytope.GetClosestTriangle().ClosestToOrigin;

                distSq = v.LengthSquared();
            }

            converged:

            fraction = lambda;

            float nlen2 = normal.LengthSquared();

            if (nlen2 > NumericEpsilon)
            {
                normal *= 1.0f / MathF.Sqrt(nlen2);
            }

            return true;
        }

        public bool SweepTest(ref MinkowskiDifference mkd, in JVector sweep,
            out JVector p1, out JVector p2, out JVector normal, out float fraction)
        {
            const float CollideEpsilon = 1e-4f;
            const int MaxIter = 34;

            convexPolytope.InitHeap();

            mkd.GeometricCenter(out var center);
            convexPolytope.InitTetrahedron(center.V);

            JVector posB = mkd.PositionB;

            fraction = 0.0f;

            p1 = p2 = JVector.Zero;

            JVector r = sweep;

            ConvexPolytope.Triangle ctri = convexPolytope.GetClosestTriangle();

            JVector v = -ctri.ClosestToOrigin;

            normal = JVector.Zero;

            int iter = MaxIter;

            float distSq = v.LengthSquared();

            while ((distSq > CollideEpsilon * CollideEpsilon) && (iter-- != 0))
            {
                mkd.Support(v, out ConvexPolytope.Vertex vertex);
                var w = vertex.V;

                float VdotW = -JVector.Dot(v, w);

                if (VdotW > 0.0f)
                {
                    float VdotR = JVector.Dot(v, r);

                    if (VdotR >= -1e-12f)
                    {
                        fraction = float.PositiveInfinity;
                        return false;
                    }

                    fraction -= VdotW / VdotR;

                    mkd.PositionB = posB + fraction * r;
                    normal = v;
                }

                if (!convexPolytope.AddVertex(vertex))
                {
                    goto converged;
                }

                ctri = convexPolytope.GetClosestTriangle();

                v = -ctri.ClosestToOrigin;

                distSq = v.LengthSquared();
            }

            converged:

            convexPolytope.CalculatePoints(ctri, out p1, out p2);

            float nlen2 = normal.LengthSquared();

            if (nlen2 > NumericEpsilon)
            {
                normal *= 1.0f / MathF.Sqrt(nlen2);
            }

            return true;
        }

        private bool SolveMPREPA(in MinkowskiDifference mkd, ref JVector point1, ref JVector point2, ref JVector normal, ref float penetration)
        {
            const float CollideEpsilon = 1e-4f;
            const int MaxIter = 85;

            convexPolytope.InitTetrahedron();

            int iter = 0;

            Unsafe.SkipInit(out ConvexPolytope.Triangle ctri);

            while (++iter < MaxIter)
            {
                ctri = convexPolytope.GetClosestTriangle();

                JVector searchDir = ctri.ClosestToOrigin;
                float searchDirSq = ctri.ClosestToOriginSq;

                if (ctri.ClosestToOriginSq < NumericEpsilon)
                {
                    searchDir = ctri.Normal;
                    searchDirSq = ctri.NormalSq;
                }

                mkd.Support(searchDir, out ConvexPolytope.Vertex vertex);

                // compare with the corresponding code in SolveGJKEPA.
                float deltaDist = JVector.Dot(ctri.ClosestToOrigin - vertex.V, searchDir);

                if (deltaDist * deltaDist <= CollideEpsilon * CollideEpsilon * searchDirSq)
                {
                    goto converged;
                }

                if (!convexPolytope.AddVertex(vertex))
                {
                    goto converged;
                }
            }

            Trace.WriteLine($"EPA: Could not converge within {MaxIter} iterations.");

            return false;

            converged:

            convexPolytope.CalculatePoints(ctri, out point1, out point2);

            normal = ctri.Normal * (1.0f / MathF.Sqrt(ctri.NormalSq));
            penetration = MathF.Sqrt(ctri.ClosestToOriginSq);

            return true;
        }

        public bool SolveMPR(in MinkowskiDifference mkd,
            out JVector pointA, out JVector pointB, out JVector normal, out float penetration)
        {
            /*
            XenoCollide is available under the zlib license:

            XenoCollide Collision Detection and Physics Library
            Copyright (c) 2007-2014 Gary Snethen http://xenocollide.com

            This software is provided 'as-is', without any express or implied warranty.
            In no event will the authors be held liable for any damages arising
            from the use of this software.
            Permission is granted to anyone to use this software for any purpose,
            including commercial applications, and to alter it and redistribute it freely,
            subject to the following restrictions:

            1. The origin of this software must not be misrepresented; you must
            not claim that you wrote the original software. If you use this
            software in a product, an acknowledgment in the product documentation
            would be appreciated but is not required.
            2. Altered source versions must be plainly marked as such, and must
            not be misrepresented as being the original software.
            3. This notice may not be removed or altered from any source distribution.
            */
            const float CollideEpsilon = 1e-4f;
            const int MaxIter = 34;

            // If MPR reports a penetration deeper than this value we do not trust
            // MPR to have found the global minimum and perform an EPA run.
            const float EPAPenetrationThreshold = 0.02f;

            convexPolytope.InitHeap();

            ref ConvexPolytope.Vertex v0 = ref convexPolytope.GetVertex(0);
            ref ConvexPolytope.Vertex v1 = ref convexPolytope.GetVertex(1);
            ref ConvexPolytope.Vertex v2 = ref convexPolytope.GetVertex(2);
            ref ConvexPolytope.Vertex v3 = ref convexPolytope.GetVertex(3);
            ref ConvexPolytope.Vertex v4 = ref convexPolytope.GetVertex(4);

            Unsafe.SkipInit(out JVector temp1);
            Unsafe.SkipInit(out JVector temp2);
            Unsafe.SkipInit(out JVector temp3);

            penetration = 0.0f;

            mkd.GeometricCenter(out v0);

            if (Math.Abs(v0.V.X) < NumericEpsilon &&
                Math.Abs(v0.V.Y) < NumericEpsilon &&
                Math.Abs(v0.V.Y) < NumericEpsilon)
            {
                // any direction is fine
                v0.V.X = 1e-05f;
            }

            JVector.Negate(v0.V, out normal);

            mkd.Support(normal, out v1);

            pointA = v1.A;
            pointB = v1.B;

            if (JVector.Dot(v1.V, normal) <= 0.0f) return false;
            JVector.Cross(v1.V, v0.V, out normal);

            if (normal.LengthSquared() < NumericEpsilon)
            {
                JVector.Subtract(v1.V, v0.V, out normal);

                normal.Normalize();

                JVector.Subtract(v1.A, v1.B, out temp1);
                penetration = JVector.Dot(temp1, normal);

                return true;
            }

            mkd.Support(normal, out v2);

            if (JVector.Dot(v2.V, normal) <= 0.0f) return false;

            // Determine whether origin is on + or - side of plane (v1.V,v0.V,v2.V)
            JVector.Subtract(v1.V, v0.V, out temp1);
            JVector.Subtract(v2.V, v0.V, out temp2);
            JVector.Cross(temp1, temp2, out normal);

            float dist = JVector.Dot(normal, v0.V);

            // If the origin is on the - side of the plane, reverse the direction of the plane
            if (dist > 0.0f)
            {
                JVector.Swap(ref v1.V, ref v2.V);
                JVector.Swap(ref v1.A, ref v2.A);
                JVector.Swap(ref v1.B, ref v2.B);
                JVector.Negate(normal, out normal);
            }

            int phase2 = 0;
            int phase1 = 0;
            bool hit = false;

            // Phase One: Identify a portal
            while (true)
            {
                if (phase1 > MaxIter) return false;

                phase1++;

                mkd.Support(normal, out v3);

                if (JVector.Dot(v3.V, normal) <= 0.0f)
                {
                    return false;
                }

                // If origin is outside (v1.V,v0.V,v3.V), then eliminate v2.V and loop
                JVector.Cross(v1.V, v3.V, out temp1);
                if (JVector.Dot(temp1, v0.V) < 0.0f)
                {
                    v2 = v3;
                    JVector.Subtract(v1.V, v0.V, out temp1);
                    JVector.Subtract(v3.V, v0.V, out temp2);
                    JVector.Cross(temp1, temp2, out normal);
                    continue;
                }

                // If origin is outside (v3.V,v0.V,v2.V), then eliminate v1.V and loop
                JVector.Cross(v3.V, v2.V, out temp1);
                if (JVector.Dot(temp1, v0.V) < 0.0f)
                {
                    v1 = v3;
                    JVector.Subtract(v3.V, v0.V, out temp1);
                    JVector.Subtract(v2.V, v0.V, out temp2);
                    JVector.Cross(temp1, temp2, out normal);
                    continue;
                }

                break;
            }

            // Phase Two: Refine the portal
            // We are now inside of a wedge...
            while (true)
            {
                phase2++;

                // Compute normal of the wedge face
                JVector.Subtract(v2.V, v1.V, out temp1);
                JVector.Subtract(v3.V, v1.V, out temp2);
                JVector.Cross(temp1, temp2, out normal);

                // normal.Normalize();
                float normalSq = normal.LengthSquared();

                // Can this happen???  Can it be handled more cleanly?
                if (normalSq < NumericEpsilon)
                {
                    // was: return true;
                    // better not return a collision
                    Trace.WriteLine("MPR: This should not happen.");
                    return false;
                }

                if (!hit)
                {
                    // Compute distance from origin to wedge face
                    float d = JVector.Dot(normal, v1.V);
                    // If the origin is inside the wedge, we have a hit
                    hit = d >= 0;
                }

                mkd.Support(normal, out v4);

                JVector.Subtract(v4.V, v3.V, out temp3);
                float delta = JVector.Dot(temp3, normal);
                penetration = JVector.Dot(v4.V, normal);

                // If the boundary is thin enough or the origin is outside the support plane for the newly discovered vertex, then we can terminate
                if (delta * delta <= CollideEpsilon * CollideEpsilon * normalSq || penetration <= 0.0f ||
                    phase2 > MaxIter)
                {
                    if (hit)
                    {
                        float invnormal = 1.0f / (float)Math.Sqrt(normalSq);

                        penetration *= invnormal;

                        if (penetration > EPAPenetrationThreshold)
                        {
                            // If epa fails it does not set any result data. We continue with the mpr data.
                            if (SolveMPREPA(mkd, ref pointA, ref pointB, ref normal, ref penetration)) return true;
                        }

                        normal *= invnormal;

                        // Compute the barycentric coordinates of the origin
                        JVector.Cross(v1.V, temp1, out temp3);
                        float gamma = JVector.Dot(temp3, normal) * invnormal;
                        JVector.Cross(temp2, v1.V, out temp3);
                        float beta = JVector.Dot(temp3, normal) * invnormal;
                        float alpha = 1.0f - gamma - beta;

                        pointA = alpha * v1.A + beta * v2.A + gamma * v3.A;
                        pointB = alpha * v1.B + beta * v2.B + gamma * v3.B;
                    }

                    return hit;
                }

                // Compute the tetrahedron dividing face (v4.V,v0.V,v3.V)
                JVector.Cross(v4.V, v0.V, out temp1);
                float dot = JVector.Dot(temp1, v1.V);

                if (dot >= 0.0f)
                {
                    dot = JVector.Dot(temp1, v2.V);

                    if (dot >= 0.0f)
                    {
                        v1 = v4; // Inside d1 & inside d2 -> eliminate v1.V
                    }
                    else
                    {
                        v3 = v4; // Inside d1 & outside d2 -> eliminate v3.V
                    }
                }
                else
                {
                    dot = JVector.Dot(temp1, v3.V);

                    if (dot >= 0.0f)
                    {
                        v2 = v4; // Outside d1 & inside d3 -> eliminate v2.V
                    }
                    else
                    {
                        v1 = v4; // Outside d1 & outside d3 -> eliminate v1.V
                    }
                }
            }
        }

        public bool SolveGJKEPA(in MinkowskiDifference mkd,
            out JVector point1, out JVector point2, out JVector normal, out float penetration)
        {
            const float CollideEpsilon = 1e-4f;
            const int MaxIter = 85;

            mkd.GeometricCenter(out ConvexPolytope.Vertex centerVertex);
            JVector center = centerVertex.V;

            convexPolytope.InitHeap();
            convexPolytope.InitTetrahedron(center);

            int iter = 0;

            Unsafe.SkipInit(out ConvexPolytope.Triangle ctri);

            while (++iter < MaxIter)
            {
                ctri = convexPolytope.GetClosestTriangle();

                JVector searchDir = ctri.ClosestToOrigin;
                float searchDirSq = ctri.ClosestToOriginSq;

                if (!convexPolytope.OriginEnclosed) searchDir.Negate();

                if (ctri.ClosestToOriginSq < NumericEpsilon)
                {
                    searchDir = ctri.Normal;
                    searchDirSq = ctri.NormalSq;
                }

                mkd.Support(searchDir, out ConvexPolytope.Vertex vertex);

                // Can we further "extend" the convex hull by adding the new vertex?
                //
                // v = Vertices[vPointer] (support point)
                // c = Triangles[Head].ClosestToOrigin
                // s = searchDir
                //
                // abs(dot(c - v, s)) / len(s) < e <=> [dot(c - v, s)]^2 = e*e*s^2
                float deltaDist = JVector.Dot(ctri.ClosestToOrigin - vertex.V, searchDir);

                if (deltaDist * deltaDist <= CollideEpsilon * CollideEpsilon * searchDirSq)
                {
                    goto converged;
                }

                if (!convexPolytope.AddVertex(vertex))
                {
                    goto converged;
                }
            }

            point1 = point2 = normal = JVector.Zero;
            penetration = 0.0f;

            Trace.WriteLine($"EPA: Could not converge within {MaxIter} iterations.");

            return false;

            converged:

            convexPolytope.CalculatePoints(ctri, out point1, out point2);
            normal = ctri.Normal * (1.0f / MathF.Sqrt(ctri.NormalSq));
            penetration = MathF.Sqrt(ctri.ClosestToOriginSq);

            // origin not enclosed: we basically did a pure GJK run
            // without ever enclosing the origin, i.e. the shapes do not overlap
            // and the penetration is negative.
            if (!convexPolytope.OriginEnclosed) penetration *= -1.0f;

            return true;
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    [ThreadStatic] private static Solver solver;

    /// <summary>
    /// Check if a point is inside a shape.
    /// </summary>
    /// <param name="support">Support map representing the shape.</param>
    /// <param name="point">Point to check.</param>
    /// <returns>Returns true if the point is contained within the shape, false otherwise.</returns>
    public static bool PointTest(ISupportMap support, in JVector point)
    {
        return solver.PointTest(support, point);
    }

    /// <summary>
    /// Check if a point is inside a shape.
    /// </summary>
    /// <param name="support">Support map representing the shape.</param>
    /// <param name="orientation">Orientation of the shape.</param>
    /// <param name="position">Position of the shape.</param>
    /// <param name="point">Point to check.</param>
    /// <returns>Returns true if the point is contained within the shape, false otherwise.</returns>
    public static bool PointTest(ISupportMap support, in JMatrix orientation,
        in JVector position, in JVector point)
    {
        JVector transformedOrigin = JVector.TransposedTransform(point - position, orientation);
        return solver.PointTest(support, transformedOrigin);
    }

    /// <summary>
    /// Performs a ray cast against a shape.
    /// </summary>
    /// <param name="support">The support function of the shape.</param>
    /// <param name="orientation">The orientation of the shape in world space.</param>
    /// <param name="position">The position of the shape in world space.</param>
    /// <param name="origin">The origin of the ray.</param>
    /// <param name="direction">The direction of the ray; normalization is not necessary.</param>
    /// <param name="fraction">Specifies the hit point of the ray, calculated as 'origin + fraction * direction'.</param>
    /// <param name="normal">
    /// The normalized normal vector perpendicular to the surface, pointing outwards. If the ray does not
    /// hit, this parameter will be zero.
    /// </param>
    /// <returns>Returns true if the ray intersects with the shape; otherwise, false.</returns>
    public static bool RayCast(ISupportMap support, in JMatrix orientation,
        in JVector position, in JVector origin, in JVector direction, out float fraction, out JVector normal)
    {
        // rotate the ray into the reference frame of bodyA..
        JVector tdirection = JVector.TransposedTransform(direction, orientation);
        JVector torigin = JVector.TransposedTransform(origin - position, orientation);

        bool result = solver.RayCast(support, torigin, tdirection, out fraction, out normal);

        // ..rotate back.
        JVector.Transform(normal, orientation, out normal);

        return result;
    }

    /// <summary>
    /// Performs a ray cast against a shape.
    /// </summary>
    /// <param name="support">The support function of the shape.</param>
    /// <param name="origin">The origin of the ray.</param>
    /// <param name="direction">The direction of the ray; normalization is not necessary.</param>
    /// <param name="fraction">Specifies the hit point of the ray, calculated as 'origin + fraction * direction'.</param>
    /// <param name="normal">
    /// The normalized normal vector perpendicular to the surface, pointing outwards. If the ray does not
    /// hit, this parameter will be zero.
    /// </param>
    /// <returns>Returns true if the ray intersects with the shape; otherwise, false.</returns>
    public static bool RayCast(ISupportMap support, in JVector origin, in JVector direction, out float fraction, out JVector normal)
    {
        return solver.RayCast(support, origin, direction, out fraction, out normal);
    }

    /// <summary>
    /// Determines whether two convex shapes overlap, providing detailed information for both overlapping and separated
    /// cases. Internally, the method employs the Expanding Polytope Algorithm (EPA) to gather collision information.
    /// </summary>
    /// <param name="supportA">The support function of shape A.</param>
    /// <param name="supportB">The support function of shape B.</param>
    /// <param name="orientationA">The orientation of shape A in world space.</param>
    /// <param name="orientationB">The orientation of shape B in world space.</param>
    /// <param name="positionA">The position of shape A in world space.</param>
    /// <param name="positionB">The position of shape B in world space.</param>
    /// <param name="pointA">
    /// For the overlapping case: the deepest point on shape A inside shape B; for the separated case: the
    /// closest point on shape A to shape B.
    /// </param>
    /// <param name="pointB">
    /// For the overlapping case: the deepest point on shape B inside shape A; for the separated case: the
    /// closest point on shape B to shape A.
    /// </param>
    /// <param name="normal">
    /// The normalized collision normal pointing from pointB to pointA. This normal remains defined even
    /// if pointA and pointB coincide. It denotes the direction in which the shapes should be moved by the minimum distance
    /// (defined by the penetration depth) to either separate them in the overlapping case or bring them into contact in
    /// the separated case.
    /// </param>
    /// <param name="penetration">The penetration depth.</param>
    /// <returns>
    /// Returns true if the algorithm completes successfully, false otherwise. In case of algorithm convergence
    /// failure, collision information reverts to the type's default values.
    /// </returns>
    public static bool GJKEPA(ISupportMap supportA, ISupportMap supportB,
        in JMatrix orientationA, in JMatrix orientationB,
        in JVector positionA, in JVector positionB,
        out JVector pointA, out JVector pointB, out JVector normal, out float penetration)
    {
        Unsafe.SkipInit(out MinkowskiDifference mkd);
        mkd.SupportA = supportA;
        mkd.SupportB = supportB;

        // rotate into the reference frame of bodyA..
        JMatrix.TransposedMultiply(orientationA, orientationB, out mkd.OrientationB);
        JVector.Subtract(positionB, positionA, out mkd.PositionB);
        JVector.TransposedTransform(mkd.PositionB, orientationA, out mkd.PositionB);

        // ..perform collision detection..
        bool success = solver.SolveGJKEPA(mkd, out pointA, out pointB, out normal, out penetration);

        // ..rotate back. this hopefully saves some matrix vector multiplication
        // when calling the support function multiple times.
        JVector.Transform(pointA, orientationA, out pointA);
        JVector.Add(pointA, positionA, out pointA);
        JVector.Transform(pointB, orientationA, out pointB);
        JVector.Add(pointB, positionA, out pointB);
        JVector.Transform(normal, orientationA, out normal);

        return success;
    }

    /// <summary>
    /// Detects whether two convex shapes overlap and provides detailed collision information for overlapping shapes.
    /// Internally, this method utilizes the Minkowski Portal Refinement (MPR) to obtain the collision information.
    /// Although MPR is not exact, it delivers a strict upper bound for the penetration depth. If the upper bound surpasses
    /// a predefined threshold, the results are further refined using the Expanding Polytope Algorithm (EPA).
    /// </summary>
    /// <param name="supportA">The support function of shape A.</param>
    /// <param name="supportB">The support function of shape B.</param>
    /// <param name="orientationA">The orientation of shape A in world space.</param>
    /// <param name="orientationB">The orientation of shape B in world space.</param>
    /// <param name="positionA">The position of shape A in world space.</param>
    /// <param name="positionB">The position of shape B in world space.</param>
    /// <param name="pointA">The deepest point on shape A that is inside shape B.</param>
    /// <param name="pointB">The deepest point on shape B that is inside shape A.</param>
    /// <param name="normal">
    /// The normalized collision normal pointing from pointB to pointA. This normal remains defined even
    /// if pointA and pointB coincide, representing the direction in which the shapes must be separated by the minimal
    /// distance (determined by the penetration depth) to avoid overlap.
    /// </param>
    /// <param name="penetration">The penetration depth.</param>
    /// <returns>Returns true if the shapes overlap (collide), and false otherwise.</returns>
    public static bool MPREPA(ISupportMap supportA, ISupportMap supportB,
        in JMatrix orientationA, in JMatrix orientationB,
        in JVector positionA, in JVector positionB,
        out JVector pointA, out JVector pointB, out JVector normal, out float penetration)
    {
        Unsafe.SkipInit(out MinkowskiDifference mkd);
        mkd.SupportA = supportA;
        mkd.SupportB = supportB;

        // rotate into the reference frame of bodyA..
        JMatrix.TransposedMultiply(orientationA, orientationB, out mkd.OrientationB);
        JVector.Subtract(positionB, positionA, out mkd.PositionB);
        JVector.TransposedTransform(mkd.PositionB, orientationA, out mkd.PositionB);

        // ..perform collision detection..
        bool res = solver.SolveMPR(mkd, out pointA, out pointB, out normal, out penetration);

        // ..rotate back. This approach potentially saves some matrix-vector multiplication when the support function is called multiple times.
        JVector.Transform(pointA, orientationA, out pointA);
        JVector.Add(pointA, positionA, out pointA);
        JVector.Transform(pointB, orientationA, out pointB);
        JVector.Add(pointB, positionA, out pointB);
        JVector.Transform(normal, orientationA, out normal);

        return res;
    }

    /// <summary>
    /// Detects whether two convex shapes overlap and provides detailed collision information.
    /// It assumes that support shape A is at position zero and not rotated.
    /// Internally, this method utilizes the Minkowski Portal Refinement (MPR) to obtain the
    /// Although MPR is not exact, it delivers a strict upper bound for the penetration depth
    /// a predefined threshold, the results are further refined using the Expanding Polytope
    /// </summary>
    /// <param name="supportA">The support function of shape A.</param>
    /// <param name="supportB">The support function of shape B.</param>
    /// <param name="orientationB">The orientation of shape B in world space.</param>
    /// <param name="positionB">The position of shape B in world space.</param>
    /// <param name="pointA">The deepest point on shape A that is inside shape B.</param>
    /// <param name="pointB">The deepest point on shape B that is inside shape A.</param>
    /// <param name="normal">
    /// The normalized collision normal pointing from pointB to pointA. This normal remains d
    /// if pointA and pointB coincide, representing the direction in which the shapes must be
    /// distance (determined by the penetration depth) to avoid overlap.
    /// </param>
    /// <param name="penetration">The penetration depth.</param>
    /// <returns>Returns true if the shapes overlap (collide), and false otherwise.</returns>
    public static bool MPREPA(ISupportMap supportA, ISupportMap supportB,
        in JMatrix orientationB, in JVector positionB,
        out JVector pointA, out JVector pointB, out JVector normal, out float penetration)
    {
        Unsafe.SkipInit(out MinkowskiDifference mkd);
        mkd.SupportA = supportA;
        mkd.SupportB = supportB;
        mkd.PositionB = positionB;
        mkd.OrientationB = orientationB;

        // ..perform collision detection..
        bool res = solver.SolveMPR(mkd, out pointA, out pointB, out normal, out penetration);

        return res;
    }

    /// <summary>
    /// Calculates the time of impact and the collision points in world space for two shapes with velocities
    /// sweepA and sweepB.
    /// </summary>
    /// <param name="pointA">Collision point on shapeA in world space. Zero if no hit is detected.</param>
    /// <param name="pointB">Collision point on shapeB in world space. Zero if no hit is detected.</param>
    /// <param name="fraction">Time of impact. Infinity if no hit is detected.</param>
    /// <returns>True if the shapes hit, false otherwise.</returns>
    public static bool SweepTest(ISupportMap supportA, ISupportMap supportB,
        in JMatrix orientationA, in JMatrix orientationB,
        in JVector positionA, in JVector positionB,
        in JVector sweepA, in JVector sweepB,
        out JVector pointA, out JVector pointB, out JVector normal, out float fraction)
    {
        Unsafe.SkipInit(out MinkowskiDifference mkd);

        mkd.SupportA = supportA;
        mkd.SupportB = supportB;

        // rotate into the reference frame of bodyA..
        JMatrix.TransposedMultiply(orientationA, orientationB, out mkd.OrientationB);
        JVector.Subtract(positionB, positionA, out mkd.PositionB);
        JVector.TransposedTransform(mkd.PositionB, orientationA, out mkd.PositionB);

        // we also transform the relative velocities
        JVector sweep = sweepB - sweepA;
        JVector.TransposedTransform(sweep, orientationA, out sweep);

        // ..perform toi calculation
        bool res = solver.SweepTest(ref mkd, sweep, out pointA, out pointB, out normal, out fraction);

        if (!res) return false;

        // ..rotate back. This approach potentially saves some matrix-vector multiplication when the support function is
        // called multiple times.
        JVector.Transform(pointA, orientationA, out pointA);
        JVector.Add(pointA, positionA, out pointA);
        JVector.Transform(pointB, orientationA, out pointB);
        JVector.Add(pointB, positionA, out pointB);
        JVector.Transform(normal, orientationA, out normal);

        // transform back from the relative velocities
        pointA += fraction * sweepA;
        pointB += fraction * sweepA; // sweepA is not a typo

        return true;
    }

    /// <summary>
    /// Perform a sweep test where support shape A is at position zero, not rotated and has no sweep
    /// direction.
    /// </summary>
    /// <param name="pointA">Collision point on shapeA in world space. Zero if no hit is detected.</param>
    /// <param name="pointB">Collision point on shapeB in world space. Zero if no hit is detected.</param>
    /// <param name="fraction">Time of impact. Infinity if no hit is detected.</param>
    /// <returns>True if the shapes hit, false otherwise.</returns>
    public static bool SweepTest(ISupportMap supportA, ISupportMap supportB,
        in JMatrix orientationB, in JVector positionB, in JVector sweepB,
        out JVector pointA, out JVector pointB, out JVector normal, out float fraction)
    {
        Unsafe.SkipInit(out MinkowskiDifference mkd);

        mkd.SupportA = supportA;
        mkd.SupportB = supportB;
        mkd.PositionB = positionB;
        mkd.OrientationB = orientationB;

        // ..perform toi calculation
        return solver.SweepTest(ref mkd, sweepB, out pointA, out pointB, out normal, out fraction);
    }
}