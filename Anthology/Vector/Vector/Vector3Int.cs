// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Vector
{
    /// <summary>
    /// A structure encapsulating three integar values.
    /// </summary>
    public struct Vector3Int : IEquatable<Vector3Int>, IFormattable
    {
        public int x, y, z;

        #region Constructors
        /// <summary> Constructs a vector whose elements are all the single specified value. </summary>
        public Vector3Int(int value) : this(value, value, value) { }

        /// <summary> Constructs a vector with the given individual elements. </summary>
        public Vector3Int(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
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
                    default:
                        throw new IndexOutOfRangeException("Invalid Vector3 index.");
                }
            }

            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    default:
                        throw new IndexOutOfRangeException("Invalid Vector3 index.");
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
            return hash;
        }

        /// <summary>
        /// Returns a boolean indicating whether the given Object is equal to this Vector3Int instance.
        /// </summary>
        /// <param name="obj">The Object to compare against.</param>
        /// <returns>True if the Object is equal to this Vector3Int; False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj)
        {
            if (!(obj is Vector3Int))
                return false;
            return Equals((Vector3Int)obj);
        }

        /// <summary>
        /// Returns a boolean indicating whether the given Vector3Int is equal to this Vector3Int instance.
        /// </summary>
        /// <param name="other">The Vector3Int to compare this instance to.</param>
        /// <returns>True if the other Vector3Int is equal to this instance; False otherwise.</returns>
        public bool Equals(Vector3Int other)
        {
            return x == other.x && y == other.y && z == other.z;
        }


        /// <summary>
        /// Returns a String representing this Vector3Int instance.
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            return ToString("G", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Returns a String representing this Vector3Int instance, using the specified format to format individual elements.
        /// </summary>
        /// <param name="format">The format of individual elements.</param>
        /// <returns>The string representation.</returns>
        public string ToString(string format)
        {
            return ToString(format, CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Returns a String representing this Vector3Int instance, using the specified format to format individual elements
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
            sb.Append('>');
            return sb.ToString();
        }

        public bool IsFinate() => MathD.IsValid(x) && MathD.IsValid(y) && MathD.IsValid(z);
        #endregion Public Instance Methods

        public static Vector3Int zero { get { return new Vector3Int(); } }
        public static Vector3Int one { get { return new Vector3Int(1, 1, 1); } }

        #region Public Static Methods

        /// <summary>
        /// Returns the Euclidean distance between the two given points.
        /// </summary>
        /// <param name="value1">The first point.</param>
        /// <param name="value2">The second point.</param>
        /// <returns>The distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Distance(Vector3Int value1, Vector3Int value2)
        {
            double dx = value1.x - value2.x;
            double dy = value1.y - value2.y;
            double dz = value1.z - value2.z;

            double ls = dx * dx + dy * dy + dz * dz;

            return Math.Sqrt(ls);
        }

        /// <summary>
        /// Restricts a vector between a min and max value.
        /// </summary>
        /// <param name="value1">The source vector.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3Int Clamp(Vector3Int value1, Vector3Int min, Vector3Int max)
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

            return new Vector3Int(x, y, z);
        }

        /// <summary>
        /// Returns a vector whose elements are the minimum of each of the pairs of elements in the two source vectors.
        /// </summary>
        /// <param name="value1">The first source vector.</param>
        /// <param name="value2">The second source vector.</param>
        /// <returns>The minimized vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3Int Min(Vector3Int value1, Vector3Int value2)
        {
            return new Vector3Int(
                (value1.x < value2.x) ? value1.x : value2.x,
                (value1.y < value2.y) ? value1.y : value2.y,
                (value1.z < value2.z) ? value1.z : value2.z);
        }

        /// <summary>
        /// Returns a vector whose elements are the maximum of each of the pairs of elements in the two source vectors
        /// </summary>
        /// <param name="value1">The first source vector</param>
        /// <param name="value2">The second source vector</param>
        /// <returns>The maximized vector</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3Int Max(Vector3Int value1, Vector3Int value2)
        {
            return new Vector3Int(
                (value1.x > value2.x) ? value1.x : value2.x,
                (value1.y > value2.y) ? value1.y : value2.y,
                (value1.z > value2.z) ? value1.z : value2.z);
        }

        /// <summary>
        /// Returns a vector whose elements are the absolute values of each of the source vector's elements.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The absolute value vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3Int Abs(Vector3Int value)
        {
            return new Vector3Int(Math.Abs(value.x), Math.Abs(value.y), Math.Abs(value.z));
        }

        #endregion Public Static Methods

        #region Public Static Operators
        /// <summary>
        /// Adds two vectors together.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The summed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3Int operator +(Vector3Int left, Vector3Int right)
        {
            return new Vector3Int(left.x + right.x, left.y + right.y, left.z + right.z);
        }

        /// <summary>
        /// Subtracts the second vector from the first.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The difference vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3Int operator -(Vector3Int left, Vector3Int right)
        {
            return new Vector3Int(left.x - right.x, left.y - right.y, left.z - right.z);
        }

        /// <summary>
        /// Multiplies two vectors together.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The product vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3Int operator *(Vector3Int left, Vector3Int right)
        {
            return new Vector3Int(left.x * right.x, left.y * right.y, left.z * right.z);
        }

        /// <summary>
        /// Multiplies a vector by the given scalar.
        /// </summary>
        /// <param name="left">The scalar value.</param>
        /// <param name="right">The source vector.</param>
        /// <returns>The scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3Int operator *(int left, Vector3Int right)
        {
            return new Vector3Int(left, left, left) * right;
        }

        /// <summary>
        /// Multiplies a vector by the given scalar.
        /// </summary>
        /// <param name="left">The source vector.</param>
        /// <param name="right">The scalar value.</param>
        /// <returns>The scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3Int operator *(Vector3Int left, int right)
        {
            return left * new Vector3Int(right, right, right);
        }

        /// <summary>
        /// Divides the first vector by the second.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The vector resulting from the division.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3Int operator /(Vector3Int left, Vector3Int right)
        {
            return new Vector3Int(left.x / right.x, left.y / right.y, left.z / right.z);
        }

        /// <summary>
        /// Returns a boolean indicating whether the two given vectors are equal.
        /// </summary>
        /// <param name="left">The first vector to compare.</param>
        /// <param name="right">The second vector to compare.</param>
        /// <returns>True if the vectors are equal; False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector3Int left, Vector3Int right)
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
        public static bool operator !=(Vector3Int left, Vector3Int right)
        {
            return !(left == right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3(Vector3Int value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3Int(Vector3 value)
        {
            return new Vector3Int((int)value.x, (int)value.y, (int)value.z);
        }

        #endregion Public Static Operators
    }
}
