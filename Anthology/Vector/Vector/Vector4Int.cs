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
    public struct Vector4Int : IEquatable<Vector4Int>, IFormattable
    {
        public int x, y, z, w;

        #region Constructors
        /// <summary> Constructs a vector whose elements are all the single specified value. </summary>
        public Vector4Int(int value) : this(value, value, value, value) { }

        /// <summary> Constructs a vector with the given individual elements. </summary>
        public Vector4Int(int x, int y, int z, int w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
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
                    case 2: return z;
                    case 3: return w;
                    default:
                        throw new IndexOutOfRangeException("Invalid Vector4Int index.");
                }
            }

            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    case 3: w = value; break;

                    default:
                        throw new IndexOutOfRangeException("Invalid Vector4Int index.");
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
            hash = HashCode.Combine(hash, z.GetHashCode());
            hash = HashCode.Combine(hash, w.GetHashCode());
            return hash;
        }

        /// <summary>
        /// Returns a boolean indicating whether the given Object is equal to this Vector4Int instance.
        /// </summary>
        /// <param name="obj">The Object to compare against.</param>
        /// <returns>True if the Object is equal to this Vector4Int; False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj)
        {
            if (!(obj is Vector4Int))
                return false;
            return Equals((Vector4Int)obj);
        }

        /// <summary>
        /// Returns a boolean indicating whether the given Vector4Int is equal to this Vector4Int instance.
        /// </summary>
        /// <param name="other">The Vector4Int to compare this instance to.</param>
        /// <returns>True if the other Vector4Int is equal to this instance; False otherwise.</returns>
        public bool Equals(Vector4Int other)
        {
            return x == other.x && y == other.y && z == other.z && w == other.w;
        }


        /// <summary>
        /// Returns a String representing this Vector4Int instance.
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            return ToString("G", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Returns a String representing this Vector4Int instance, using the specified format to format individual elements.
        /// </summary>
        /// <param name="format">The format of individual elements.</param>
        /// <returns>The string representation.</returns>
        public string ToString(string format)
        {
            return ToString(format, CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Returns a String representing this Vector4Int instance, using the specified format to format individual elements
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
            sb.Append(separator);
            sb.Append(' ');
            sb.Append(z.ToString(format, formatProvider));
            sb.Append(separator);
            sb.Append(' ');
            sb.Append(w.ToString(format, formatProvider));
            sb.Append('>');

            return sb.ToString();
        }

        #endregion Public Instance Methods

        public static Vector4Int zero { get { return new Vector4Int(); } }
        public static Vector4Int one { get { return new Vector4Int(1, 1, 1, 1); } }
        public static Vector4Int right { get { return new Vector4Int(1, 0, 0, 0); } }
        public static Vector4Int up { get { return new Vector4Int(0, 1, 0, 0); } }
        public static Vector4Int forward { get { return new Vector4Int(0, 0, 1, 0); } }
        public static Vector4Int unitW { get { return new Vector4Int(0, 0, 0, 1); } }
        public static Vector4Int maxValue { get { return new Vector4Int(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue); } }
        public static Vector4Int minValue { get { return new Vector4Int(int.MinValue, int.MinValue, int.MinValue, int.MinValue); } }

        #region Public Static Methods

        /// <summary>
        /// Restricts a vector between a min and max value.
        /// </summary>
        /// <param name="value1">The source vector.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int Clamp(Vector4Int value1, Vector4Int min, Vector4Int max)
        {
            // This compare order is very important!!!
            // We must follow HLSL behavior in the case user specified min value is bigger than max value.
            int x = value1.x;
            x = (x > max.x) ? max.x : x;
            x = (x < min.x) ? min.x : x;

            int y = value1.y;
            y = (y > max.y) ? max.y : y;
            y = (y < min.y) ? min.y : y;

            int z = value1.z;
            z = (z > max.z) ? max.z : z;
            z = (z < min.z) ? min.z : z;

            int w = value1.w;
            w = (w > max.w) ? max.w : w;
            w = (w < min.w) ? min.w : w;

            return new Vector4Int(x, y, z, w);
        }

        /// <summary>
        /// Returns a vector whose elements are the minimum of each of the pairs of elements in the two source vectors.
        /// </summary>
        /// <param name="value1">The first source vector.</param>
        /// <param name="value2">The second source vector.</param>
        /// <returns>The minimized vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int Min(Vector4Int value1, Vector4Int value2)
        {
            return new Vector4Int(
                (value1.x < value2.x) ? value1.x : value2.x,
                (value1.y < value2.y) ? value1.y : value2.y,
                (value1.z < value2.z) ? value1.z : value2.z,
                (value1.w < value2.w) ? value1.w : value2.w);
        }

        /// <summary>
        /// Returns a vector whose elements are the maximum of each of the pairs of elements in the two source vectors
        /// </summary>
        /// <param name="value1">The first source vector</param>
        /// <param name="value2">The second source vector</param>
        /// <returns>The maximized vector</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int Max(Vector4Int value1, Vector4Int value2)
        {
            return new Vector4Int(
                (value1.x > value2.x) ? value1.x : value2.x,
                (value1.y > value2.y) ? value1.y : value2.y,
                (value1.z > value2.z) ? value1.z : value2.z,
                (value1.w > value2.w) ? value1.w : value2.w);
        }

        /// <summary>
        /// Returns a vector whose elements are the absolute values of each of the source vector's elements.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The absolute value vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int Abs(Vector4Int value)
        {
            return new Vector4Int(Math.Abs(value.x), Math.Abs(value.y), Math.Abs(value.z), Math.Abs(value.w));
        }

        /// <summary>
        /// Returns a vector whose elements are floored to the nearest integer.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The floored vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int FloorToInt(Vector4 value) => new Vector4Int((int)Math.Floor(value.x), (int)Math.Floor(value.y), (int)Math.Floor(value.z), (int)Math.Floor(value.w));
        /// <summary>
        /// Returns a vector whose elements are rounded to the nearest integer.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The rounded vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int RoundToInt(Vector4 value) => new Vector4Int((int)Math.Round(value.x), (int)Math.Round(value.y), (int)Math.Round(value.z), (int)Math.Round(value.w));
        /// <summary>
        /// Returns a vector whose elements are ceiled to the nearest integer.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The ceiled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int CeilToInt(Vector4 value) => new Vector4Int((int)Math.Ceiling(value.x), (int)Math.Ceiling(value.y), (int)Math.Ceiling(value.z), (int)Math.Ceiling(value.w));

        #endregion Public Static Methods

        #region Public Static Operators
        /// <summary>
        /// Adds two vectors together.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The summed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int operator +(Vector4Int left, Vector4Int right)
        {
            return new Vector4Int(left.x + right.x, left.y + right.y, left.z + right.z, left.w + right.w);
        }

        /// <summary>
        /// Subtracts the second vector from the first.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The difference vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int operator -(Vector4Int left, Vector4Int right)
        {
            return new Vector4Int(left.x - right.x, left.y - right.y, left.z - right.z, left.w - right.w);
        }

        /// <summary>
        /// Multiplies two vectors together.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The product vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int operator *(Vector4Int left, Vector4Int right)
        {
            return new Vector4Int(left.x * right.x, left.y * right.y, left.z * right.z, left.w * right.w);
        }

        /// <summary>
        /// Multiplies a vector by the given scalar.
        /// </summary>
        /// <param name="left">The scalar value.</param>
        /// <param name="right">The source vector.</param>
        /// <returns>The scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int operator *(int left, Vector4Int right)
        {
            return new Vector4Int(left, left, left, left) * right;
        }

        /// <summary>
        /// Multiplies a vector by the given scalar.
        /// </summary>
        /// <param name="left">The source vector.</param>
        /// <param name="right">The scalar value.</param>
        /// <returns>The scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int operator *(Vector4Int left, int right)
        {
            return left * new Vector4Int(right, right, right, right);
        }

        /// <summary>
        /// Divides the first vector by the second.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The vector resulting from the division.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4Int operator /(Vector4Int left, Vector4Int right)
        {
            return new Vector4Int(left.x / right.x, left.y / right.y, left.z / right.z, left.w / right.w);
        }

        /// <summary>
        /// Returns a boolean indicating whether the two given vectors are equal.
        /// </summary>
        /// <param name="left">The first vector to compare.</param>
        /// <param name="right">The second vector to compare.</param>
        /// <returns>True if the vectors are equal; False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector4Int left, Vector4Int right)
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
        public static bool operator !=(Vector4Int left, Vector4Int right)
        {
            return !(left == right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(Vector4Int value)
        {
            return new Vector4(value.x, value.y, value.z, value.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4Int(Vector4 value)
        {
            return new Vector4Int((int)value.x, (int)value.y, (int)value.z, (int)value.w);
        }

        #endregion Public Static Operators
    }
}
