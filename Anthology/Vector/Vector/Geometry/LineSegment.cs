// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Represents a 3D line segment defined by two endpoints.
    /// </summary>
    public struct LineSegment : IEquatable<LineSegment>, IFormattable
    {
        /// <summary>The starting point of the line segment.</summary>
        public Double3 Start;

        /// <summary>The ending point of the line segment.</summary>
        public Double3 End;

        /// <summary>
        /// Initializes a new line segment with the specified endpoints.
        /// </summary>
        /// <param name="start">The starting point.</param>
        /// <param name="end">The ending point.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LineSegment(Double3 start, Double3 end)
        {
            Start = start;
            End = end;
        }

        /// <summary>
        /// Initializes a new line segment with individual coordinate components.
        /// </summary>
        /// <param name="startX">Start point X coordinate.</param>
        /// <param name="startY">Start point Y coordinate.</param>
        /// <param name="startZ">Start point Z coordinate.</param>
        /// <param name="endX">End point X coordinate.</param>
        /// <param name="endY">End point Y coordinate.</param>
        /// <param name="endZ">End point Z coordinate.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LineSegment(double startX, double startY, double startZ, double endX, double endY, double endZ)
        {
            Start = new Double3(startX, startY, startZ);
            End = new Double3(endX, endY, endZ);
        }

        /// <summary>
        /// Gets the direction vector from start to end (not normalized).
        /// </summary>
        public Double3 Direction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => End - Start;
        }

        /// <summary>
        /// Gets the normalized direction vector from start to end.
        /// </summary>
        public Double3 NormalizedDirection
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Double3.Normalize(Direction);
        }

        /// <summary>
        /// Gets the length of the line segment.
        /// </summary>
        public double Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Double3.Length(Direction);
        }

        /// <summary>
        /// Gets the squared length of the line segment.
        /// </summary>
        public double LengthSquared
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Double3.LengthSquared(Direction);
        }

        /// <summary>
        /// Gets the center point of the line segment.
        /// </summary>
        public Double3 Center
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Start + End) / 2.0;
        }

        /// <summary>
        /// Gets a point at the specified parameter along the line segment.
        /// </summary>
        /// <param name="t">Parameter value (0 = start, 1 = end).</param>
        /// <returns>The interpolated point.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 GetPoint(double t)
        {
            return Maths.Lerp(Start, End, t);
        }

        /// <summary>
        /// Gets a point at the specified parameter along the line segment, clamped to [0,1].
        /// </summary>
        /// <param name="t">Parameter value.</param>
        /// <returns>The interpolated point.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 GetPointClamped(double t)
        {
            return GetPoint(Maths.Clamp(t, 0.0, 1.0));
        }

        /// <summary>
        /// Gets the closest point on this line segment to a given point.
        /// </summary>
        /// <param name="point">The point to find the closest point to.</param>
        /// <returns>The closest point on the line segment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 ClosestPointTo(Double3 point)
        {
            Double3 closestPoint;
            Intersection.ClosestPointOnLineSegmentToPoint(Start, End, point, out closestPoint);
            return closestPoint;
        }

        /// <summary>
        /// Gets the parameter t for the closest point on this line segment to a given point.
        /// </summary>
        /// <param name="point">The point to find the closest point to.</param>
        /// <returns>The parameter t (0 = start, 1 = end).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetClosestParameter(Double3 point)
        {
            Double3 direction = Direction;
            double lengthSq = Double3.LengthSquared(direction);
            
            if (lengthSq < double.Epsilon * double.Epsilon)
                return 0.0; // Degenerate segment
            
            double t = Double3.Dot(point - Start, direction) / lengthSq;
            return Maths.Clamp(t, 0.0, 1.0);
        }

        /// <summary>
        /// Gets the squared distance from a point to this line segment.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>The squared distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetSqrDistanceToPoint(Double3 point)
        {
            return Intersection.DistanceSqPointToLineSegment(Start, End, point);
        }

        /// <summary>
        /// Gets the distance from a point to this line segment.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>The distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetDistanceToPoint(Double3 point)
        {
            return Maths.Sqrt(GetSqrDistanceToPoint(point));
        }

        /// <summary>
        /// Checks if a point lies on this line segment within a tolerance.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <param name="tolerance">The distance tolerance.</param>
        /// <returns>True if the point is on the line segment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsPoint(Double3 point, double tolerance = double.Epsilon)
        {
            return GetDistanceToPoint(point) <= tolerance;
        }

        /// <summary>
        /// Intersects this line segment with a plane.
        /// </summary>
        /// <param name="plane">The plane to intersect with.</param>
        /// <param name="intersectionPoint">The intersection point if found.</param>
        /// <returns>True if intersection occurs within the segment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IntersectsPlane(Plane plane, out Double3 intersectionPoint)
        {
            return Intersection.LineSegmentPlane(Start, End, plane.Normal, plane.D, out intersectionPoint);
        }

        /// <summary>
        /// Gets the closest points between this line segment and another line segment.
        /// </summary>
        /// <param name="other">The other line segment.</param>
        /// <param name="point1">Closest point on this segment.</param>
        /// <param name="point2">Closest point on the other segment.</param>
        /// <param name="t1">Parameter on this segment.</param>
        /// <param name="t2">Parameter on the other segment.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetClosestPoints(LineSegment other, out Double3 point1, out Double3 point2, out double t1, out double t2)
        {
            Intersection.ClosestPointsLineSegmentLineSegment(Start, End, other.Start, other.End, out point1, out point2, out t1, out t2);
        }

        /// <summary>
        /// Gets the squared distance between this line segment and another line segment.
        /// </summary>
        /// <param name="other">The other line segment.</param>
        /// <returns>The squared distance between the segments.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetSqrDistanceToSegment(LineSegment other)
        {
            return Intersection.DistanceSqSegmentSegment(Start, End, other.Start, other.End);
        }

        /// <summary>
        /// Gets the distance between this line segment and another line segment.
        /// </summary>
        /// <param name="other">The other line segment.</param>
        /// <returns>The distance between the segments.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetDistanceToSegment(LineSegment other)
        {
            return Maths.Sqrt(GetSqrDistanceToSegment(other));
        }

        /// <summary>
        /// Extends the line segment by the specified amounts at both ends.
        /// </summary>
        /// <param name="startExtension">Amount to extend at the start.</param>
        /// <param name="endExtension">Amount to extend at the end.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Extend(double startExtension, double endExtension)
        {
            Double3 direction = NormalizedDirection;
            Start -= direction * startExtension;
            End += direction * endExtension;
        }

        /// <summary>
        /// Returns an extended version of this line segment.
        /// </summary>
        /// <param name="startExtension">Amount to extend at the start.</param>
        /// <param name="endExtension">Amount to extend at the end.</param>
        /// <returns>The extended line segment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LineSegment Extended(double startExtension, double endExtension)
        {
            var result = this;
            result.Extend(startExtension, endExtension);
            return result;
        }

        /// <summary>
        /// Reverses the direction of the line segment (swaps start and end).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reverse()
        {
            (End, Start) = (Start, End);
        }

        /// <summary>
        /// Returns a reversed version of this line segment.
        /// </summary>
        /// <returns>The reversed line segment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LineSegment Reversed()
        {
            return new LineSegment(End, Start);
        }

        /// <summary>
        /// Transforms the line segment by a 4x4 matrix.
        /// </summary>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>The transformed line segment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LineSegment Transform(Double4x4 matrix)
        {
            return new LineSegment(
                Double4x4.TransformPoint(Start, matrix),
                Double4x4.TransformPoint(End, matrix)
            );
        }

        /// <summary>
        /// Subdivides the line segment into the specified number of equal parts.
        /// </summary>
        /// <param name="subdivisions">Number of subdivisions.</param>
        /// <returns>Array of subdivision points including start and end.</returns>
        public Double3[] Subdivide(int subdivisions)
        {
            if (subdivisions < 1)
                throw new ArgumentException("Subdivisions must be at least 1", nameof(subdivisions));

            var points = new Double3[subdivisions + 1];
            for (int i = 0; i <= subdivisions; i++)
            {
                double t = i / (double)subdivisions;
                points[i] = GetPoint(t);
            }
            return points;
        }

        /// <summary>
        /// Checks if this line segment is degenerate (start equals end).
        /// </summary>
        /// <returns>True if the segment is degenerate.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDegenerate()
        {
            return LengthSquared < double.Epsilon * double.Epsilon;
        }

        /// <summary>
        /// Creates a line segment from a ray with a specific length.
        /// </summary>
        /// <param name="ray">The ray to create the segment from.</param>
        /// <param name="length">The length of the segment.</param>
        /// <returns>A line segment starting at the ray origin.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static LineSegment FromRay(Ray ray, double length)
        {
            return new LineSegment(ray.Origin, ray.Origin + ray.Direction * length);
        }

        // --- IEquatable & IFormattable Implementation ---
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(LineSegment other) => Start.Equals(other.Start) && End.Equals(other.End);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is LineSegment other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(Start, End);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format, IFormatProvider? formatProvider = null)
        {
            return string.Format(formatProvider, "LineSegmentD(Start: {0}, End: {1})", 
                Start.ToString(format, formatProvider), End.ToString(format, formatProvider));
        }

        public static bool operator ==(LineSegment left, LineSegment right) => left.Equals(right);
        public static bool operator !=(LineSegment left, LineSegment right) => !left.Equals(right);
    }
}
