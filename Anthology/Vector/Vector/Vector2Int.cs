// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Vector
{
    /// <summary>
    /// A structure encapsulating two integar values.
    /// </summary>
    public struct Vector2Int : IEquatable<Vector2Int>, IFormattable
    {
        public int x, y;

        #region Constructors
        /// <summary> Constructs a vector whose elements are all the single specified value. </summary>
        public Vector2Int(int value) : this(value, value) { }

        /// <summary> Constructs a vector with the given individual elements. </summary>
        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
        #endregion Constructors

        #region Public Instance Properties

        public int this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    default:
                        throw new IndexOutOfRangeException("Invalid Vector2Int index.");
                }
            }

            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    default:
                        throw new IndexOutOfRangeException("Invalid Vector2Int index.");
                }
            }
        }

        #endregion

        #region Public Instance methods

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            int hash = x.GetHashCode();
            hash = HashCode.Combine(hash, y.GetHashCode());
            return hash;
        }

        /// <summary>
        /// Returns a boolean indicating whether the given Object is equal to this Vector2Int instance.
        /// </summary>
        /// <param name="obj">The Object to compare against.</param>
        /// <returns>True if the Object is equal to this Vector2Int; False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj)
        {
            if (!(obj is Vector2Int))
                return false;
            return Equals((Vector2Int)obj);
        }

        /// <summary>
        /// Returns a boolean indicating whether the given Vector2Int is equal to this Vector2Int instance.
        /// </summary>
        /// <param name="other">The Vector2Int to compare this instance to.</param>
        /// <returns>True if the other Vector2Int is equal to this instance; False otherwise.</returns>
        public bool Equals(Vector2Int other)
        {
            return x == other.x && y == other.y;
        }


        /// <summary>
        /// Returns a String representing this Vector2Int instance.
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            return ToString("G", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Returns a String representing this Vector2Int instance, using the specified format to format individual elements.
        /// </summary>
        /// <param name="format">The format of individual elements.</param>
        /// <returns>The string representation.</returns>
        public string ToString(string format)
        {
            return ToString(format, CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Returns a String representing this Vector2Int instance, using the specified format to format individual elements
        /// and the given IFormatProvider.
        /// </summary>
        /// <param name="format">The format of individual elements.</param>
        /// <param name="formatProvider">The format provider to use when formatting elements.</param>
        /// <returns>The string representation.</returns>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            StringBuilder sb = new StringBuilder();
            string separator = NumberFormatInfo.GetInstance(formatProvider).NumberGroupSeparator;
            sb.Append('<');
            sb.Append(x.ToString(format, formatProvider));
            sb.Append(separator);
            sb.Append(' ');
            sb.Append(y.ToString(format, formatProvider));
            sb.Append('>');
            return sb.ToString();
        }

        #endregion Public Instance Methods

        #region Public Static Properties

        public static Vector2Int zero { get { return new Vector2Int(); } }
        public static Vector2Int one { get { return new Vector2Int(1, 1); } }
        public static Vector2Int right { get { return new Vector2Int(1, 0); } }
        public static Vector2Int left { get { return new Vector2Int(-1, 0); } }
        public static Vector2Int up { get { return new Vector2Int(0, 1); } }
        public static Vector2Int down { get { return new Vector2Int(0, -1); } }

        public static Vector2Int maxValue { get { return new Vector2Int(int.MaxValue, int.MaxValue); } }
        public static Vector2Int minValue { get { return new Vector2Int(int.MinValue, int.MinValue); } }

        #endregion Public Static Properties

        #region Public Static Methods

        /// <summary>
        /// Returns the Euclidean distance between the two given points.
        /// </summary>
        /// <param name="value1">The first point.</param>
        /// <param name="value2">The second point.</param>
        /// <returns>The distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Distance(Vector2Int value1, Vector2Int value2)
        {
            double dx = value1.x - value2.x;
            double dy = value1.y - value2.y;

            double ls = dx * dx + dy * dy;

            return Math.Sqrt(ls);
        }

        /// <summary>
        /// Restricts a vector between a min and max value.
        /// </summary>
        /// <param name="value1">The source vector.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int Clamp(Vector2Int value1, Vector2Int min, Vector2Int max)
        {
            // This compare order is very important!!!
            // We must follow HLSL behavior in the case user specified min value is bigger than max value.
            int x = value1.x;
            x = (x > max.x) ? max.x : x;
            x = (x < min.x) ? min.x : x;

            int y = value1.y;
            y = (y > max.y) ? max.y : y;
            y = (y < min.y) ? min.y : y;

            return new Vector2Int(x, y);
        }

        /// <summary>
        /// Returns a vector whose elements are the minimum of each of the pairs of elements in the two source vectors.
        /// </summary>
        /// <param name="value1">The first source vector.</param>
        /// <param name="value2">The second source vector.</param>
        /// <returns>The minimized vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int Min(Vector2Int value1, Vector2Int value2)
        {
            return new Vector2Int(
                (value1.x < value2.x) ? value1.x : value2.x,
                (value1.y < value2.y) ? value1.y : value2.y);
        }

        /// <summary>
        /// Returns a vector whose elements are the maximum of each of the pairs of elements in the two source vectors
        /// </summary>
        /// <param name="value1">The first source vector</param>
        /// <param name="value2">The second source vector</param>
        /// <returns>The maximized vector</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int Max(Vector2Int value1, Vector2Int value2)
        {
            return new Vector2Int(
                (value1.x > value2.x) ? value1.x : value2.x,
                (value1.y > value2.y) ? value1.y : value2.y);
        }

        /// <summary>
        /// Returns a vector whose elements are the absolute values of each of the source vector's elements.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The absolute value vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int Abs(Vector2Int value)
        {
            return new Vector2Int(Math.Abs(value.x), Math.Abs(value.y));
        }

        /// <summary>
        /// Returns a vector whose elements are floored to the nearest integer.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The floored vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int FloorToInt(Vector2 value) => new Vector2Int((int)Math.Floor(value.x), (int)Math.Floor(value.y));
        /// <summary>
        /// Returns a vector whose elements are rounded to the nearest integer.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The rounded vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int RoundToInt(Vector2 value) => new Vector2Int((int)Math.Round(value.x), (int)Math.Round(value.y));
        /// <summary>
        /// Returns a vector whose elements are ceiled to the nearest integer.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The ceiled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int CeilToInt(Vector2 value) => new Vector2Int((int)Math.Ceiling(value.x), (int)Math.Ceiling(value.y));

        #endregion Public Static Methods

        #region Public Static Operators
        /// <summary>
        /// Adds two vectors together.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The summed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int operator +(Vector2Int left, Vector2Int right)
        {
            return new Vector2Int(left.x + right.x, left.y + right.y);
        }

        /// <summary>
        /// Subtracts the second vector from the first.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The difference vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int operator -(Vector2Int left, Vector2Int right)
        {
            return new Vector2Int(left.x - right.x, left.y - right.y);
        }

        /// <summary>
        /// Multiplies two vectors together.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The product vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int operator *(Vector2Int left, Vector2Int right)
        {
            return new Vector2Int(left.x * right.x, left.y * right.y);
        }

        /// <summary>
        /// Multiplies a vector by the given scalar.
        /// </summary>
        /// <param name="left">The scalar value.</param>
        /// <param name="right">The source vector.</param>
        /// <returns>The scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int operator *(int left, Vector2Int right)
        {
            return new Vector2Int(left, left) * right;
        }

        /// <summary>
        /// Multiplies a vector by the given scalar.
        /// </summary>
        /// <param name="left">The source vector.</param>
        /// <param name="right">The scalar value.</param>
        /// <returns>The scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int operator *(Vector2Int left, int right)
        {
            return left * new Vector2Int(right, right);
        }

        /// <summary>
        /// Divides the first vector by the second.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The vector resulting from the division.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int operator /(Vector2Int left, Vector2Int right)
        {
            return new Vector2Int(left.x / right.x, left.y / right.y);
        }

        /// <summary>
        /// Returns a boolean indicating whether the two given vectors are equal.
        /// </summary>
        /// <param name="left">The first vector to compare.</param>
        /// <param name="right">The second vector to compare.</param>
        /// <returns>True if the vectors are equal; False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector2Int left, Vector2Int right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Returns a boolean indicating whether the two given vectors are not equal.
        /// </summary>
        /// <param name="left">The first vector to compare.</param>
        /// <param name="right">The second vector to compare.</param>
        /// <returns>True if the vectors are not equal; False if they are equal.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vector2Int left, Vector2Int right)
        {
            return !(left == right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(Vector2Int value)
        {
            return new Vector2(value.x, value.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2Int(Vector2 value)
        {
            return new Vector2Int((int)value.x, (int)value.y);
        }

        #endregion Public Static Operators
    }
}
