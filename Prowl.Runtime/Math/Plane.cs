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
using System.Diagnostics;
using System.Runtime.Serialization;

namespace Prowl.Runtime
{
    internal class PlaneHelper
    {
        /// <summary>
        /// Returns a value indicating what side (positive/negative) of a plane a point is
        /// </summary>
        /// <param name="point">The point to check with</param>
        /// <param name="plane">The plane to check against</param>
        /// <returns>Greater than zero if on the positive side, less than zero if on the negative size, 0 otherwise</returns>
        public static double ClassifyPoint(ref Vector3 point, ref Plane plane)
        {
            return point.x * plane.Normal.x + point.y * plane.Normal.y + point.z * plane.Normal.z + plane.D;
        }

        /// <summary>
        /// Returns the perpendicular distance from a point to a plane
        /// </summary>
        /// <param name="point">The point to check</param>
        /// <param name="plane">The place to check</param>
        /// <returns>The perpendicular distance from the point to the plane</returns>
        public static double PerpendicularDistance(ref Vector3 point, ref Plane plane)
        {
            // dist = (ax + by + cz + d) / sqrt(a*a + b*b + c*c)
            return (double)Math.Abs((plane.Normal.x * point.x + plane.Normal.y * point.y + plane.Normal.z * point.z)
                                    / Math.Sqrt(plane.Normal.x * plane.Normal.x + plane.Normal.y * plane.Normal.y + plane.Normal.z * plane.Normal.z));
        }
    }

    public enum PlaneIntersectionType
    {
        Front,
        Back,
        Intersecting
    }

    public struct Plane : IEquatable<Plane>
    {
        #region Public Fields

        public double D;

        public Vector3 Normal;

        #endregion Public Fields


        #region Constructors

        public Plane(Vector4 value)
            : this(new Vector3(value.x, value.y, value.z), value.w)
        {

        }

        public Plane(Vector3 normal, double d)
        {
            Normal = normal;
            D = d;
        }

        public Plane(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;

            Vector3 cross = Vector3.Cross(ab, ac);
            Normal = Vector3.Normalize(cross);
            D = -(Vector3.Dot(Normal, a));
        }

        public Plane(double a, double b, double c, double d)
            : this(new Vector3(a, b, c), d)
        {

        }

        #endregion Constructors


        #region Public Methods

        public double Dot(Vector4 value)
        {
            return ((((this.Normal.x * value.x) + (this.Normal.y * value.y)) + (this.Normal.z * value.z)) + (this.D * value.w));
        }

        public void Dot(ref Vector4 value, out double result)
        {
            result = (((this.Normal.x * value.x) + (this.Normal.y * value.y)) + (this.Normal.z * value.z)) + (this.D * value.w);
        }

        public double DotCoordinate(Vector3 value)
        {
            return ((((this.Normal.x * value.x) + (this.Normal.y * value.y)) + (this.Normal.z * value.z)) + this.D);
        }

        public void DotCoordinate(ref Vector3 value, out double result)
        {
            result = (((this.Normal.x * value.x) + (this.Normal.y * value.y)) + (this.Normal.z * value.z)) + this.D;
        }

        public double DotNormal(Vector3 value)
        {
            return (((this.Normal.x * value.x) + (this.Normal.y * value.y)) + (this.Normal.z * value.z));
        }

        public void DotNormal(ref Vector3 value, out double result)
        {
            result = ((this.Normal.x * value.x) + (this.Normal.y * value.y)) + (this.Normal.z * value.z);
        }

        public void Normalize()
        {
            double factor;
            Vector3 normal = Normal;
            Normal = Vector3.Normalize(Normal);
            factor = (double)Math.Sqrt(Normal.x * Normal.x + Normal.y * Normal.y + Normal.z * Normal.z) /
                    (double)Math.Sqrt(normal.x * normal.x + normal.y * normal.y + normal.z * normal.z);
            D = D * factor;
        }

        public static Plane Normalize(Plane value)
        {
            Plane ret;
            Normalize(ref value, out ret);
            return ret;
        }

        public static void Normalize(ref Plane value, out Plane result)
        {
            double factor;
            result.Normal = Vector3.Normalize(value.Normal);
            factor = (double)Math.Sqrt(result.Normal.x * result.Normal.x + result.Normal.y * result.Normal.y + result.Normal.z * result.Normal.z) /
                    (double)Math.Sqrt(value.Normal.x * value.Normal.x + value.Normal.y * value.Normal.y + value.Normal.z * value.Normal.z);
            result.D = value.D * factor;
        }

        public static bool operator !=(Plane plane1, Plane plane2)
        {
            return !plane1.Equals(plane2);
        }

        public static bool operator ==(Plane plane1, Plane plane2)
        {
            return plane1.Equals(plane2);
        }

        public override bool Equals(object other)
        {
            return (other is Plane) ? this.Equals((Plane)other) : false;
        }

        public bool Equals(Plane other)
        {
            return ((Normal == other.Normal) && (D == other.D));
        }

        public override int GetHashCode()
        {
            return Normal.GetHashCode() ^ D.GetHashCode();
        }

        public PlaneIntersectionType Intersects(Bounds box)
        {
            return box.Intersects(this);
        }

        public void Intersects(ref Bounds box, out PlaneIntersectionType result)
        {
            box.Intersects(ref this, out result);
        }

        internal PlaneIntersectionType Intersects(ref Vector3 point)
        {
            double distance;
            DotCoordinate(ref point, out distance);

            if (distance > 0)
                return PlaneIntersectionType.Front;

            if (distance < 0)
                return PlaneIntersectionType.Back;

            return PlaneIntersectionType.Intersecting;
        }

        internal string DebugDisplayString
        {
            get
            {
                return string.Concat(
                    this.Normal.ToString(), "  ",
                    this.D.ToString()
                    );
            }
        }

        public override string ToString()
        {
            return "{Normal:" + Normal + " D:" + D + "}";
        }

        #endregion
    }
}
