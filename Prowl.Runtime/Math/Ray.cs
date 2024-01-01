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

namespace Prowl.Runtime
{
    public struct Ray : IEquatable<Ray>
    {
        #region Public Fields

        public Vector3 Direction;

        public Vector3 Position;

        #endregion


        #region Public Constructors

        public Ray(Vector3 position, Vector3 direction)
        {
            this.Position = position;
            this.Direction = direction;
        }

        #endregion


        #region Public Methods

        public override bool Equals(object obj)
        {
            return (obj is Ray) ? this.Equals((Ray)obj) : false;
        }


        public bool Equals(Ray other)
        {
            return this.Position.Equals(other.Position) && this.Direction.Equals(other.Direction);
        }


        public override int GetHashCode()
        {
            return Position.GetHashCode() ^ Direction.GetHashCode();
        }

        // adapted from http://www.scratchapixel.com/lessons/3d-basic-lessons/lesson-7-intersecting-simple-shapes/ray-box-intersection/
        public double? Intersects(Bounds box)
        {
            const double Epsilon = 1e-6;

            double? tMin = null, tMax = null;

            if (Math.Abs(Direction.x) < Epsilon)
            {
                if (Position.x < box.Min.x || Position.x > box.Max.x)
                    return null;
            }
            else
            {
                tMin = (box.Min.x - Position.x) / Direction.x;
                tMax = (box.Max.x - Position.x) / Direction.x;

                if (tMin > tMax)
                {
                    var temp = tMin;
                    tMin = tMax;
                    tMax = temp;
                }
            }

            if (Math.Abs(Direction.y) < Epsilon)
            {
                if (Position.y < box.Min.y || Position.y > box.Max.y)
                    return null;
            }
            else
            {
                var tMinY = (box.Min.y - Position.y) / Direction.y;
                var tMaxY = (box.Max.y - Position.y) / Direction.y;

                if (tMinY > tMaxY)
                {
                    var temp = tMinY;
                    tMinY = tMaxY;
                    tMaxY = temp;
                }

                if ((tMin.HasValue && tMin > tMaxY) || (tMax.HasValue && tMinY > tMax))
                    return null;

                if (!tMin.HasValue || tMinY > tMin) tMin = tMinY;
                if (!tMax.HasValue || tMaxY < tMax) tMax = tMaxY;
            }

            if (Math.Abs(Direction.z) < Epsilon)
            {
                if (Position.z < box.Min.z || Position.z > box.Max.z)
                    return null;
            }
            else
            {
                var tMinZ = (box.Min.z - Position.z) / Direction.z;
                var tMaxZ = (box.Max.z - Position.z) / Direction.z;

                if (tMinZ > tMaxZ)
                {
                    var temp = tMinZ;
                    tMinZ = tMaxZ;
                    tMaxZ = temp;
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


        public void Intersects(ref Bounds box, out double? result)
        {
            result = Intersects(box);
        }

        public double? Intersects(Plane plane)
        {
            double? result;
            Intersects(ref plane, out result);
            return result;
        }

        public void Intersects(ref Plane plane, out double? result)
        {
            var den = Vector3.Dot(Direction, plane.Normal);
            if (Math.Abs(den) < 0.00001)
            {
                result = null;
                return;
            }

            result = (-plane.D - Vector3.Dot(plane.Normal, Position)) / den;

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
            return string.Format("{{Position:{0} Direction:{1}}}", Position.ToString(), Direction.ToString());
        }

        #endregion
    }
}
