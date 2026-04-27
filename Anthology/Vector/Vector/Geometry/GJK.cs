// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Gilbert-Johnson-Keerthi (GJK) collision detection algorithm.
    /// Provides collision detection between any shapes that implement IBoundingShape.
    /// </summary>
    public static class GJK
    {
        private const int MaxIterations = 64;
        private const float Epsilon = 1e-10f;

        /// <summary>
        /// Tests if two bounding shapes intersect using the GJK algorithm.
        /// </summary>
        /// <param name="shapeA">The first shape.</param>
        /// <param name="shapeB">The second shape.</param>
        /// <returns>True if the shapes intersect.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Intersects(IBoundingShape shapeA, IBoundingShape shapeB)
        {
            // Initial search direction (arbitrary)
            Float3 direction = new Float3(1, 0, 0);

            // Get initial support point
            Float3 support = GetSupport(shapeA, shapeB, direction);

            // Initialize simplex with first point
            Simplex simplex = new Simplex();
            simplex.Add(support);

            // New direction is towards the origin
            direction = -support;

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                // Get next support point
                support = GetSupport(shapeA, shapeB, direction);

                // If the support point is not past the origin in the direction,
                // then the Minkowski difference cannot contain the origin
                if (Float3.Dot(support, direction) < 0)
                {
                    return false; // No collision
                }

                // Add the support point to the simplex
                simplex.Add(support);

                // Check if the simplex contains the origin and update direction
                if (HandleSimplex(ref simplex, ref direction))
                {
                    return true; // Collision detected
                }
            }

            return false; // Max iterations reached, assume no collision
        }

        /// <summary>
        /// Gets the support point in the Minkowski difference (A - B).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Float3 GetSupport(IBoundingShape shapeA, IBoundingShape shapeB, Float3 direction)
        {
            return shapeA.SupportMap(direction) - shapeB.SupportMap(-direction);
        }

        /// <summary>
        /// Handles the simplex to determine if it contains the origin.
        /// Updates the simplex and search direction accordingly.
        /// </summary>
        /// <returns>True if the origin is contained in the simplex.</returns>
        private static bool HandleSimplex(ref Simplex simplex, ref Float3 direction)
        {
            switch (simplex.Count)
            {
                case 2:
                    return HandleLine(ref simplex, ref direction);
                case 3:
                    return HandleTriangle(ref simplex, ref direction);
                case 4:
                    return HandleTetrahedron(ref simplex, ref direction);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Handles a line simplex (2 points).
        /// </summary>
        private static bool HandleLine(ref Simplex simplex, ref Float3 direction)
        {
            Float3 a = simplex[0]; // Most recently added point
            Float3 b = simplex[1];

            Float3 ab = b - a;
            Float3 ao = -a; // Direction to origin from a

            // Check if origin is in the direction of b from a
            if (Float3.Dot(ab, ao) > 0)
            {
                // Origin is between a and b, search perpendicular to ab towards origin
                direction = Float3.Cross(Float3.Cross(ab, ao), ab);

                // Handle degenerate case where direction becomes zero
                if (Float3.LengthSquared(direction) < Epsilon)
                {
                    direction = GetPerpendicularDirection(ab);
                }
            }
            else
            {
                // Origin is in the opposite direction, keep only point a
                simplex.Set(a);
                direction = ao;
            }

            return false;
        }

        /// <summary>
        /// Handles a triangle simplex (3 points).
        /// </summary>
        private static bool HandleTriangle(ref Simplex simplex, ref Float3 direction)
        {
            Float3 a = simplex[0]; // Most recently added point
            Float3 b = simplex[1];
            Float3 c = simplex[2];

            Float3 ab = b - a;
            Float3 ac = c - a;
            Float3 ao = -a;

            Float3 abc = Float3.Cross(ab, ac); // Triangle normal

            // Check if origin is outside edge AC
            Float3 acPerp = Float3.Cross(abc, ac);
            if (Float3.Dot(acPerp, ao) > 0)
            {
                // Origin is on the AC side
                if (Float3.Dot(ac, ao) > 0)
                {
                    // Origin is in AC region
                    simplex.Set(a, c);
                    direction = Float3.Cross(Float3.Cross(ac, ao), ac);

                    if (Float3.LengthSquared(direction) < Epsilon)
                    {
                        direction = GetPerpendicularDirection(ac);
                    }
                }
                else
                {
                    // Origin is in A region
                    simplex.Set(a);
                    direction = ao;
                }
                return false;
            }

            // Check if origin is outside edge AB
            Float3 abPerp = Float3.Cross(ab, abc);
            if (Float3.Dot(abPerp, ao) > 0)
            {
                // Origin is on the AB side
                if (Float3.Dot(ab, ao) > 0)
                {
                    // Origin is in AB region
                    simplex.Set(a, b);
                    direction = Float3.Cross(Float3.Cross(ab, ao), ab);

                    if (Float3.LengthSquared(direction) < Epsilon)
                    {
                        direction = GetPerpendicularDirection(ab);
                    }
                }
                else
                {
                    // Origin is in A region
                    simplex.Set(a);
                    direction = ao;
                }
                return false;
            }

            // Origin is on the triangle
            // Check if origin is above or below the triangle
            if (Float3.Dot(abc, ao) > 0)
            {
                // Origin is above the triangle
                direction = abc;
            }
            else
            {
                // Origin is below the triangle, flip the simplex
                simplex.Set(a, c, b);
                direction = -abc;
            }

            return false;
        }

        /// <summary>
        /// Handles a tetrahedron simplex (4 points).
        /// </summary>
        private static bool HandleTetrahedron(ref Simplex simplex, ref Float3 direction)
        {
            Float3 a = simplex[0]; // Most recently added point
            Float3 b = simplex[1];
            Float3 c = simplex[2];
            Float3 d = simplex[3];

            Float3 ab = b - a;
            Float3 ac = c - a;
            Float3 ad = d - a;
            Float3 ao = -a;

            // Compute normals for each face (pointing outward)
            Float3 abc = Float3.Cross(ab, ac);
            Float3 acd = Float3.Cross(ac, ad);
            Float3 adb = Float3.Cross(ad, ab);

            // Check face ABC
            if (Float3.Dot(abc, ao) > 0)
            {
                simplex.Set(a, b, c);
                return HandleTriangle(ref simplex, ref direction);
            }

            // Check face ACD
            if (Float3.Dot(acd, ao) > 0)
            {
                simplex.Set(a, c, d);
                return HandleTriangle(ref simplex, ref direction);
            }

            // Check face ADB
            if (Float3.Dot(adb, ao) > 0)
            {
                simplex.Set(a, d, b);
                return HandleTriangle(ref simplex, ref direction);
            }

            // Origin is inside the tetrahedron
            return true;
        }

        /// <summary>
        /// Gets a perpendicular direction to the given vector.
        /// Used as a fallback for degenerate cases.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Float3 GetPerpendicularDirection(Float3 v)
        {
            // Choose an axis that's not parallel to v
            Float3 axis = Maths.Abs(v.X) < 0.9 ? new Float3(1, 0, 0) : new Float3(0, 1, 0);
            return Float3.Cross(v, axis);
        }

        /// <summary>
        /// Internal simplex structure for GJK algorithm.
        /// Stores up to 4 points.
        /// </summary>
        private struct Simplex
        {
            private Float3 _p0, _p1, _p2, _p3;
            private int _count;

            public int Count => _count;

            public Float3 this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0: return _p0;
                        case 1: return _p1;
                        case 2: return _p2;
                        case 3: return _p3;
                        default: throw new IndexOutOfRangeException();
                    }
                }
            }

            public void Add(Float3 point)
            {
                // Add point at the front (most recent)
                switch (_count)
                {
                    case 0:
                        _p0 = point;
                        break;
                    case 1:
                        _p1 = _p0;
                        _p0 = point;
                        break;
                    case 2:
                        _p2 = _p1;
                        _p1 = _p0;
                        _p0 = point;
                        break;
                    case 3:
                        _p3 = _p2;
                        _p2 = _p1;
                        _p1 = _p0;
                        _p0 = point;
                        break;
                }
                _count = Maths.Min(_count + 1, 4);
            }

            public void Set(Float3 a)
            {
                _p0 = a;
                _count = 1;
            }

            public void Set(Float3 a, Float3 b)
            {
                _p0 = a;
                _p1 = b;
                _count = 2;
            }

            public void Set(Float3 a, Float3 b, Float3 c)
            {
                _p0 = a;
                _p1 = b;
                _p2 = c;
                _count = 3;
            }
        }
    }
}
