// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Represents a 3D plane defined by a normal vector and distance from origin.
    /// The plane equation is: Normal � Point = D
    /// </summary>
    public struct Plane : IEquatable<Plane>, IFormattable
    {
        /// <summary>The normalized normal vector of the plane.</summary>
        public Double3 Normal;

        /// <summary>The distance from the origin to the plane along the normal.</summary>
        public double D;

        /// <summary>
        /// Initializes a new plane from a normal vector and distance.
        /// The normal will be normalized.
        /// </summary>
        /// <param name="normal">The normal vector (will be normalized).</param>
        /// <param name="d">The distance from origin.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Plane(Double3 normal, double d)
        {
            Normal = Double3.Normalize(normal);
            D = d;
        }

        /// <summary>
        /// Initializes a new plane from three points.
        /// Points should be in counter-clockwise order for outward-facing normal.
        /// </summary>
        /// <param name="point1">First point on the plane.</param>
        /// <param name="point2">Second point on the plane.</param>
        /// <param name="point3">Third point on the plane.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Plane(Double3 point1, Double3 point2, Double3 point3)
        {
            Double3 edge1 = point2 - point1;
            Double3 edge2 = point3 - point1;
            Normal = Double3.Normalize(Double3.Cross(edge1, edge2));
            D = Double3.Dot(Normal, point1);
        }

        /// <summary>
        /// Initializes a new plane from a normal vector and a point on the plane.
        /// </summary>
        /// <param name="normal">The normal vector (will be normalized).</param>
        /// <param name="pointOnPlane">A point that lies on the plane.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Plane FromNormalAndPoint(Double3 normal, Double3 pointOnPlane)
        {
            Double3 normalizedNormal = Double3.Normalize(normal);
            return new Plane(normalizedNormal, Double3.Dot(normalizedNormal, pointOnPlane));
        }

        /// <summary>
        /// Gets the signed distance from a point to this plane.
        /// Positive if the point is on the side of the normal, negative otherwise.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>The signed distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetSignedDistanceToPoint(Double3 point)
        {
            return Double3.Dot(Normal, point) - D;
        }

        /// <summary>
        /// Gets the absolute distance from a point to this plane.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>The absolute distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetDistanceToPoint(Double3 point)
        {
            return Maths.Abs(GetSignedDistanceToPoint(point));
        }

        /// <summary>
        /// Projects a point onto this plane.
        /// </summary>
        /// <param name="point">The point to project.</param>
        /// <returns>The closest point on the plane.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 ClosestPointOnPlane(Double3 point)
        {
            double distance = GetSignedDistanceToPoint(point);
            return point - Normal * distance;
        }

        /// <summary>
        /// Determines which side of the plane a point is on.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is on the positive side (normal side) of the plane.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetSide(Double3 point)
        {
            return GetSignedDistanceToPoint(point) > 0.0;
        }

        /// <summary>
        /// Checks if two points are on the same side of the plane.
        /// </summary>
        /// <param name="point1">First point.</param>
        /// <param name="point2">Second point.</param>
        /// <returns>True if both points are on the same side.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool SameSide(Double3 point1, Double3 point2)
        {
            double d1 = GetSignedDistanceToPoint(point1);
            double d2 = GetSignedDistanceToPoint(point2);
            return (d1 > 0.0) == (d2 > 0.0);
        }

        /// <summary>
        /// Flips the plane to face the opposite direction.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Flip()
        {
            Normal = -Normal;
            D = -D;
        }

        /// <summary>
        /// Returns a flipped version of this plane.
        /// </summary>
        /// <returns>A plane facing the opposite direction.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Plane Flipped()
        {
            return new Plane(-Normal, -D);
        }

        /// <summary>
        /// Translates the plane by a given offset.
        /// </summary>
        /// <param name="translation">The translation vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Translate(Double3 translation)
        {
            D += Double3.Dot(Normal, translation);
        }

        /// <summary>
        /// Returns a translated version of this plane.
        /// </summary>
        /// <param name="translation">The translation vector.</param>
        /// <returns>The translated plane.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Plane Translated(Double3 translation)
        {
            return new Plane(Normal, D + Double3.Dot(Normal, translation));
        }

        // --- IEquatable & IFormattable Implementation ---
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Plane other) => Normal.Equals(other.Normal) && D.Equals(other.D);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is Plane other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(Normal, D);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format, IFormatProvider? formatProvider = null)
        {
            return string.Format(formatProvider, "PlaneD(Normal: {0}, D: {1})", 
                Normal.ToString(format, formatProvider), D.ToString(format, formatProvider));
        }

        public static bool operator ==(Plane left, Plane right) => left.Equals(right);
        public static bool operator !=(Plane left, Plane right) => !left.Equals(right);
    }
}
