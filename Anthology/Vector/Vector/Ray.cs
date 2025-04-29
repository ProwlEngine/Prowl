// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

#region License
/*
MIT License
Copyright © 2006 The Mono.Xna Team

All rights reserved.

Authors:
Olivier Dufour (Duff)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
#endregion License

using System;

namespace Prowl.Vector
{
    public struct Ray : IEquatable<Ray>
    {
        #region Public Fields

        public Vector3 direction;

        public Vector3 origin;

        #endregion


        #region Public Constructors

        public Ray(Vector3 position, Vector3 direction)
        {
            origin = position;
            this.direction = direction;
        }

        #endregion


        #region Public Methods

        public override bool Equals(object? obj)
        {
            return (obj is Ray ray) && Equals(ray);
        }


        public bool Equals(Ray other)
        {
            return origin.Equals(other.origin) && direction.Equals(other.direction);
        }


        public override int GetHashCode()
        {
            return origin.GetHashCode() ^ direction.GetHashCode();
        }

        // adapted from http://www.scratchapixel.com/lessons/3d-basic-lessons/lesson-7-intersecting-simple-shapes/ray-box-intersection/
        public readonly double? Intersects(Bounds box)
        {
            double? tMin = null, tMax = null;

            if (Math.Abs(direction.x) < double.Epsilon)
            {
                if (origin.x < box.min.x || origin.x > box.max.x)
                    return null;
            }
            else
            {
                tMin = (box.min.x - origin.x) / direction.x;
                tMax = (box.max.x - origin.x) / direction.x;

                if (tMin > tMax)
                {
                    (tMax, tMin) = (tMin, tMax);
                }
            }

            if (Math.Abs(direction.y) < double.Epsilon)
            {
                if (origin.y < box.min.y || origin.y > box.max.y)
                    return null;
            }
            else
            {
                double tMinY = (box.min.y - origin.y) / direction.y;
                double tMaxY = (box.max.y - origin.y) / direction.y;

                if (tMinY > tMaxY)
                {
                    (tMaxY, tMinY) = (tMinY, tMaxY);
                }

                if ((tMin.HasValue && tMin > tMaxY) || (tMax.HasValue && tMinY > tMax))
                    return null;

                if (!tMin.HasValue || tMinY > tMin) tMin = tMinY;
                if (!tMax.HasValue || tMaxY < tMax) tMax = tMaxY;
            }

            if (Math.Abs(direction.z) < double.Epsilon)
            {
                if (origin.z < box.min.z || origin.z > box.max.z)
                    return null;
            }
            else
            {
                double tMinZ = (box.min.z - origin.z) / direction.z;
                double tMaxZ = (box.max.z - origin.z) / direction.z;

                if (tMinZ > tMaxZ)
                {
                    (tMaxZ, tMinZ) = (tMinZ, tMaxZ);
                }

                if ((tMin.HasValue && tMin > tMaxZ) || (tMax.HasValue && tMinZ > tMax))
                    return null;

                if (!tMin.HasValue || tMinZ > tMin) tMin = tMinZ;
                if (!tMax.HasValue || tMaxZ < tMax) tMax = tMaxZ;
            }

            // having a positive tMin and a negative tMax means the ray is inside the box
            // we expect the intesection distance to be 0 in that case
            if ((tMin.HasValue && tMin < 0) && tMax > 0) return 0;

            // a negative tMin means that the intersection point is behind the ray's origin
            // we discard these as not hitting the AABB
            if (tMin < 0) return null;

            return tMin;
        }


        public readonly void Intersects(ref Bounds box, out double? result)
        {
            result = Intersects(box);
        }

        public readonly double? Intersects(Plane plane)
        {
            Intersects(ref plane, out double? result);
            return result;
        }

        public readonly void Intersects(ref Plane plane, out double? result)
        {
            double den = Vector3.Dot(direction, plane.normal);
            if (Math.Abs(den) < 0.00001)
            {
                result = null;
                return;
            }

            result = (-plane.distance - Vector3.Dot(plane.normal, origin)) / den;

            if (result < 0.0)
            {
                if (result < -0.00001)
                {
                    result = null;
                    return;
                }

                result = 0.0;
            }
        }

        public readonly double? Intersects(Vector3 vertex1, Vector3 vertex2, Vector3 vertex3, bool cullBackface = false)
        {
            // Edge vectors
            Vector3 edge1 = vertex2 - vertex1;
            Vector3 edge2 = vertex3 - vertex1;

            // Calculate determinant
            Vector3 h = Vector3.Cross(direction, edge2);
            double a = Vector3.Dot(edge1, h);

            // If determinant is near zero, ray lies in plane of triangle or ray is parallel to plane of triangle
            // Backface culling: if a < 0, ray hits triangle from behind
            if (cullBackface)
            {
                if (a < float.Epsilon) return null;
            }
            else
            {
                if (Math.Abs(a) < float.Epsilon) return null;
            }

            double f = 1.0 / a;
            Vector3 s = origin - vertex1;
            double u = f * Vector3.Dot(s, h);

            // Ray lies outside the triangle
            if (u < 0.0 || u > 1.0) return null;

            Vector3 q = Vector3.Cross(s, edge1);
            double v = f * Vector3.Dot(direction, q);

            // Ray lies outside the triangle
            if (v < 0.0 || u + v > 1.0) return null;

            double t = f * Vector3.Dot(edge2, q);

            // Line intersection but not ray intersection
            if (t < float.Epsilon) return null;

            return t;
        }

        public readonly Vector3 Position(double distance)
        {
            return origin + direction * distance;
        }

        public static bool operator !=(Ray a, Ray b)
        {
            return !a.Equals(b);
        }


        public static bool operator ==(Ray a, Ray b)
        {
            return a.Equals(b);
        }


        public override string ToString()
        {
            return string.Format("{{Position:{0} Direction:{1}}}", origin.ToString(), direction.ToString());
        }

        #endregion
    }
}
