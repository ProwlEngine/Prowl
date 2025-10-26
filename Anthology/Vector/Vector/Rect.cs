// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

using Prowl.Vector.Geometry;

namespace Prowl.Vector
{
    /// <summary>
    /// Represents an axis-aligned rectangle in 2D space.
    /// </summary>
    public struct Rect : IEquatable<Rect>, IFormattable
    {
        /// <summary>The minimum corner of the rectangle.</summary>
        public Double2 Min;

        /// <summary>The maximum corner of the rectangle.</summary>
        public Double2 Max;

        /// <summary>
        /// Initializes a new rectangle with the specified min and max corners.
        /// </summary>
        /// <param name="min">The minimum corner.</param>
        /// <param name="max">The maximum corner.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect(Double2 min, Double2 max)
        {
            Min = new Double2(
                Maths.Min(min.X, max.X),
                Maths.Min(min.Y, max.Y)
            );
            Max = new Double2(
                Maths.Max(min.X, max.X),
                Maths.Max(min.Y, max.Y)
            );
        }

        /// <summary>
        /// Initializes a new rectangle with individual min/max components.
        /// </summary>
        /// <param name="minX">Minimum X coordinate.</param>
        /// <param name="minY">Minimum Y coordinate.</param>
        /// <param name="maxX">Maximum X coordinate.</param>
        /// <param name="maxY">Maximum Y coordinate.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect(double minX, double minY, double maxX, double maxY)
            : this(new Double2(minX, minY), new Double2(maxX, maxY))
        {
        }

        /// <summary>
        /// Initializes a new rectangle centered at a point with the specified size.
        /// </summary>
        /// <param name="center">The center point.</param>
        /// <param name="size">The size (width, height).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rect FromCenterAndSize(Double2 center, Double2 size)
        {
            Double2 halfSize = size / 2.0;
            return new Rect(center - halfSize, center + halfSize);
        }

        /// <summary>
        /// Gets the center point of the rectangle.
        /// </summary>
        public Double2 Center
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Min + Max) / 2.0;
        }

        /// <summary>
        /// Gets the size (width, height) of the rectangle.
        /// </summary>
        public Double2 Size
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Max - Min;
        }

        /// <summary>
        /// Gets the extents (half-size) of the rectangle.
        /// </summary>
        public Double2 Extents
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Size / 2.0;
        }

        /// <summary>
        /// Gets the area of the rectangle.
        /// </summary>
        public double Area
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Double2 size = Size;
                return size.X * size.Y;
            }
        }

        /// <summary>
        /// Gets a corner of the rectangle by index (0-3).
        /// </summary>
        /// <param name="index">Corner index (0-3).</param>
        /// <returns>The corner position.</returns>
        public Double2 GetCorner(int index)
        {
            switch (index)
            {
                case 0: return new Double2(Min.X, Min.Y);
                case 1: return new Double2(Max.X, Min.Y);
                case 2: return new Double2(Max.X, Max.Y);
                case 3: return new Double2(Min.X, Max.Y);
                default: throw new IndexOutOfRangeException("Rect corner index must be between 0 and 3.");
            }
        }

        /// <summary>
        /// Gets all 4 corners of the rectangle.
        /// </summary>
        /// <returns>Array of 4 corner positions.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double2[] GetCorners()
        {
            return new Double2[]
            {
                new Double2(Min.X, Min.Y),
                new Double2(Max.X, Min.Y),
                new Double2(Max.X, Max.Y),
                new Double2(Min.X, Max.Y)
            };
        }

        /// <summary>
        /// Checks if this rectangle contains a point.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside or on the rectangle boundary.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Double2 point)
        {
            return point.X >= Min.X - double.Epsilon && point.X <= Max.X + double.Epsilon &&
                   point.Y >= Min.Y - double.Epsilon && point.Y <= Max.Y + double.Epsilon;
        }

        /// <summary>
        /// Checks if this rectangle completely contains another rectangle.
        /// </summary>
        /// <param name="other">The other rectangle to test.</param>
        /// <returns>True if the other rectangle is completely inside this rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Rect other)
        {
            return other.Min.X >= Min.X - double.Epsilon && other.Max.X <= Max.X + double.Epsilon &&
                   other.Min.Y >= Min.Y - double.Epsilon && other.Max.Y <= Max.Y + double.Epsilon;
        }

        /// <summary>
        /// Checks if this rectangle intersects with another rectangle.
        /// </summary>
        /// <param name="other">The other rectangle to test.</param>
        /// <returns>True if the rectangles intersect or touch.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(Rect other)
        {
            return !(other.Max.X < Min.X || other.Min.X > Max.X ||
                     other.Max.Y < Min.Y || other.Min.Y > Max.Y);
        }

        /// <summary>
        /// Gets the closest point on the rectangle to a given point.
        /// </summary>
        /// <param name="point">The point to find the closest point to.</param>
        /// <returns>The closest point on the rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double2 ClosestPointTo(Double2 point)
        {
            return new Double2(
                Maths.Clamp(point.X, Min.X, Max.X),
                Maths.Clamp(point.Y, Min.Y, Max.Y)
            );
        }

        /// <summary>
        /// Gets the squared distance from a point to the rectangle.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>The squared distance (0 if point is inside).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetSqrDistanceToPoint(Double2 point)
        {
            Double2 closest = ClosestPointTo(point);
            Double2 diff = point - closest;
            return diff.X * diff.X + diff.Y * diff.Y;
        }

        /// <summary>
        /// Gets the distance from a point to the rectangle.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>The distance (0 if point is inside).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetDistanceToPoint(Double2 point)
        {
            return Maths.Sqrt(GetSqrDistanceToPoint(point));
        }

        /// <summary>
        /// Expands the rectangle to include a point.
        /// </summary>
        /// <param name="point">The point to include.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Encapsulate(Double2 point)
        {
            Min = new Double2(
                Maths.Min(Min.X, point.X),
                Maths.Min(Min.Y, point.Y)
            );
            Max = new Double2(
                Maths.Max(Max.X, point.X),
                Maths.Max(Max.Y, point.Y)
            );
        }

        /// <summary>
        /// Expands the rectangle to include another rectangle.
        /// </summary>
        /// <param name="other">The rectangle to include.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Encapsulate(Rect other)
        {
            Min = new Double2(
                Maths.Min(Min.X, other.Min.X),
                Maths.Min(Min.Y, other.Min.Y)
            );
            Max = new Double2(
                Maths.Max(Max.X, other.Max.X),
                Maths.Max(Max.Y, other.Max.Y)
            );
        }

        /// <summary>
        /// Returns a rectangle that encapsulates both this rectangle and a point.
        /// </summary>
        /// <param name="point">The point to include.</param>
        /// <returns>The encapsulating rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Encapsulating(Double2 point)
        {
            var result = this;
            result.Encapsulate(point);
            return result;
        }

        /// <summary>
        /// Returns a rectangle that encapsulates both this rectangle and another rectangle.
        /// </summary>
        /// <param name="other">The rectangle to include.</param>
        /// <returns>The encapsulating rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Encapsulating(Rect other)
        {
            var result = this;
            result.Encapsulate(other);
            return result;
        }

        /// <summary>
        /// Expands the rectangle by a given amount in all directions.
        /// </summary>
        /// <param name="amount">The amount to expand by.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Expand(double amount)
        {
            Double2 expansion = new Double2(amount, amount);
            Min -= expansion;
            Max += expansion;
        }

        /// <summary>
        /// Expands the rectangle by different amounts in each direction.
        /// </summary>
        /// <param name="expansion">The amount to expand by in each direction.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Expand(Double2 expansion)
        {
            Min -= expansion;
            Max += expansion;
        }

        /// <summary>
        /// Returns an expanded version of this rectangle.
        /// </summary>
        /// <param name="amount">The amount to expand by.</param>
        /// <returns>The expanded rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Expanded(double amount)
        {
            var result = this;
            result.Expand(amount);
            return result;
        }

        /// <summary>
        /// Returns an expanded version of this rectangle.
        /// </summary>
        /// <param name="expansion">The amount to expand by in each direction.</param>
        /// <returns>The expanded rectangle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rect Expanded(Double2 expansion)
        {
            var result = this;
            result.Expand(expansion);
            return result;
        }

        /// <summary>
        /// Checks if this rectangle is valid (min <= max in both dimensions).
        /// </summary>
        /// <returns>True if the rectangle is valid.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid()
        {
            return Min.X <= Max.X && Min.Y <= Max.Y;
        }
        
        /// <summary>
        /// Checks if this rectangle has zero area.
        /// </summary>
        /// <returns>True if the rectangle has zero area.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEmpty()
        {
            return Maths.Abs(Area) < double.Epsilon;
        }

        // --- IEquatable & IFormattable Implementation ---
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Rect other) => Min.Equals(other.Min) && Max.Equals(other.Max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is Rect other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(Min, Max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format, IFormatProvider? formatProvider = null)
        {
            return string.Format(formatProvider, "RectD(Min: {0}, Max: {1})",
                Min.ToString(format, formatProvider), Max.ToString(format, formatProvider));
        }

        public static bool operator ==(Rect left, Rect right) => left.Equals(right);
        public static bool operator !=(Rect left, Rect right) => !left.Equals(right);
    }
}
