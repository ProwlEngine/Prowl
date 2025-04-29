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
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    public enum PlaneIntersectionType
    {
        Front,
        Back,
        Intersecting
    }

    public struct Plane : IEquatable<Plane>
    {
        #region Public Fields

        public double distance;

        public Vector3 normal;

        #endregion Public Fields


        #region Constructors

        public Plane(Vector4 value)
            : this(new Vector3(value.x, value.y, value.z), value.w)
        {

        }

        public Plane(Vector3 normal, double d)
        {
            this.normal = normal;
            distance = d;
        }

        public Plane(Vector3 a, Vector3 b, Vector3 c)
        {
            // Same as Set3Points, Cant call it due to c# version differences
            normal = Vector3.Normalize(Vector3.Cross(a - b, a - c));
            distance = Vector3.Dot(normal, a);
        }

        public Plane(double a, double b, double c, double d)
            : this(new Vector3(a, b, c), d)
        {

        }

        #endregion Constructors


        #region Public Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Dot(Vector4 value) => ((((normal.x * value.x) + (normal.y * value.y)) + (normal.z * value.z)) + (distance * value.w));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dot(ref Vector4 value, out double result) => result = (((normal.x * value.x) + (normal.y * value.y)) + (normal.z * value.z)) + (distance * value.w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double DotCoordinate(Vector3 value) => ((((normal.x * value.x) + (normal.y * value.y)) + (normal.z * value.z)) + distance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DotCoordinate(ref Vector3 value, out double result) => result = (((normal.x * value.x) + (normal.y * value.y)) + (normal.z * value.z)) + distance;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double DotNormal(Vector3 value) => (((normal.x * value.x) + (normal.y * value.y)) + (normal.z * value.z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DotNormal(ref Vector3 value, out double result) => result = ((normal.x * value.x) + (normal.y * value.y)) + (normal.z * value.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetSide(Vector3 inPt) => Vector3.Dot(normal, inPt) + distance > 0.0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsOnPositiveSide(Vector3 point) => Vector3.Dot(normal, point) > distance;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetDistanceToPoint(Vector3 inPt) => Math.Abs(Vector3.Dot(normal, inPt) + distance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsOnPlane(Vector3 point, double tolerance = 0) => MathD.Abs(Vector3.Dot(normal, point) - distance) <= tolerance;

        public void Normalize()
        {
            double factor;
            Vector3 normal = this.normal;
            this.normal = Vector3.Normalize(this.normal);
            factor = Math.Sqrt(normal.x * normal.x + normal.y * normal.y + normal.z * normal.z) /
                     Math.Sqrt(normal.x * normal.x + normal.y * normal.y + normal.z * normal.z);
            distance = distance * factor;
        }

        public void Set3Points(Vector3 a, Vector3 b, Vector3 c)
        {
            normal = Vector3.Normalize(Vector3.Cross(a - b, a - c));
            distance = Vector3.Dot(normal, a);
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
            result.normal = Vector3.Normalize(value.normal);
            factor = Math.Sqrt(result.normal.x * result.normal.x + result.normal.y * result.normal.y + result.normal.z * result.normal.z) /
                     Math.Sqrt(value.normal.x * value.normal.x + value.normal.y * value.normal.y + value.normal.z * value.normal.z);
            result.distance = value.distance * factor;
        }

        public static bool operator !=(Plane plane1, Plane plane2) => !plane1.Equals(plane2);

        public static bool operator ==(Plane plane1, Plane plane2) => plane1.Equals(plane2);

        public override bool Equals(object? other) => (other is Plane plane) ? Equals(plane) : false;

        public bool Equals(Plane other) => ((normal == other.normal) && (MathD.ApproximatelyEquals(distance, other.distance)));

        public override int GetHashCode() => normal.GetHashCode() ^ distance.GetHashCode();

        public PlaneIntersectionType Intersects(Bounds box) => box.Intersects(this);

        public void Intersects(ref Bounds box, out PlaneIntersectionType result) => box.Intersects(ref this, out result);

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

        public bool DoesLineIntersectPlane(Vector3 lineStart, Vector3 lineEnd, out Vector3 result)
        {
            result = Vector3.zero;

            Vector3 segment = lineStart - lineEnd;
            double den = Vector3.Dot(normal, segment);

            if (MathD.Abs(den) < MathD.Small)
                return false;

            double dist = (Vector3.Dot(normal, lineStart) - distance) / den;

            if (dist < -MathD.Small || dist > (1.0f + MathD.Small))
                return false;

            dist = -dist;
            result = lineStart + segment * dist;

            return true;
        }

        internal string DebugDisplayString => string.Concat(normal.ToString(), "  ", distance.ToString());

        public override string ToString() => "{Normal:" + normal + " Distance:" + distance + "}";

        #endregion
    }
}
