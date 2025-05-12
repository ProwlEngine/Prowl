// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Vector
{
    /// <summary>
    /// A structure encapsulating four double precision floating point values.
    /// </summary>
    public struct Vector4 : IEquatable<Vector4>, IFormattable
    {
        public Double x, y, z, w;

        #region Constructors

        /// <summary> Constructs a vector whose elements are all the single specified value. </summary>
        public Vector4(Double value) : this(value, value, value, value) { }
        /// <summary> Constructs a vector with the given individual elements. </summary>
        public Vector4(Double x, Double y, Double z, Double w)
        {
            this.w = w;
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary> Constructs a Vector4 from the given Vector2 and a Z and W component. </summary>
        public Vector4(Vector2 value, Double z = 0.0, Double w = 0.0)
        {
            x = value.x;
            y = value.y;
            this.z = z;
            this.w = w;
        }

        /// <summary> Constructs a Vector4 from 2 given Vector2's. </summary>
        public Vector4(Vector2 xy, Vector2 zw)
        {
            x = xy.x;
            y = xy.y;
            z = zw.x;
            w = zw.y;
        }

        /// <summary> Constructs a Vector4 from the given Vector3 and a W component. </summary>
        public Vector4(Vector3 value, Double w = 0.0)
        {
            x = value.x;
            y = value.y;
            z = value.z;
            this.w = w;
        }
        #endregion Constructors

        #region Public Instance Properties
        public Vector4 normalized { get { return Normalize(this); } }

        public double magnitude { get { return MathD.Sqrt(x * x + y * y + z * z + w * w); } }

        public double sqrMagnitude { get { return x * x + y * y + z * z + w * w; } }

        public Vector3 xyz => new Vector3(x, y, z);

        public double this[int index]
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
                        throw new IndexOutOfRangeException("Invalid Vector4 index.");
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
                        throw new IndexOutOfRangeException("Invalid Vector4 index.");
                }
            }
        }

        #endregion

        #region Public Static Properties
        public static Vector4 zero { get { return new Vector4(); } }
        public static Vector4 one { get { return new Vector4(1.0, 1.0, 1.0, 1.0); } }
        public static Vector4 right { get { return new Vector4(1.0, 0.0, 0.0, 0.0); } }
        public static Vector4 up { get { return new Vector4(0.0, 1.0, 0.0, 0.0); } }
        public static Vector4 forward { get { return new Vector4(0.0, 0.0, 1.0, 0.0); } }
        public static Vector4 unitW { get { return new Vector4(0.0, 0.0, 0.0, 1.0); } }

        public static Vector4 infinity = new Vector4(MathD.Infinity, MathD.Infinity, MathD.Infinity, MathD.Infinity);
        #endregion Public Static Properties

        #region Public Instance methods

        public System.Numerics.Vector4 ToFloat() => new System.Numerics.Vector4((float)x, (float)y, (float)z, (float)w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Scale(Vector4 scale) { x *= scale.x; y *= scale.y; z *= scale.z; w *= scale.w; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Normalize()
        {
            double ls = x * x + y * y + z * z + w * w;
            double invNorm = 1.0 / Math.Sqrt(ls);
            x *= invNorm;
            y *= invNorm;
            z *= invNorm;
            w *= invNorm;
        }

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
        /// Returns a boolean indicating whether the given Object is equal to this Vector4 instance.
        /// </summary>
        /// <param name="obj">The Object to compare against.</param>
        /// <returns>True if the Object is equal to this Vector4; False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj)
        {
            if (!(obj is Vector4))
                return false;
            return Equals((Vector4)obj);
        }

        /// <summary>
        /// Returns a boolean indicating whether the given Vector4 is equal to this Vector4 instance.
        /// </summary>
        /// <param name="other">The Vector4 to compare this instance to.</param>
        /// <returns>True if the other Vector4 is equal to this instance; False otherwise.</returns>
        public bool Equals(Vector4 other)
        {
            return MathD.ApproximatelyEquals(x, other.x)
                   && MathD.ApproximatelyEquals(y, other.y)
                   && MathD.ApproximatelyEquals(z, other.z)
                   && MathD.ApproximatelyEquals(w, other.w);
        }

        /// <summary>
        /// Returns a String representing this Vector4 instance.
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            return ToString("G", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Returns a String representing this Vector4 instance, using the specified format to format individual elements.
        /// </summary>
        /// <param name="format">The format of individual elements.</param>
        /// <returns>The string representation.</returns>
        public string ToString(string format)
        {
            return ToString(format, CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Returns a String representing this Vector4 instance, using the specified format to format individual elements
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
        public bool IsFinate() => MathD.IsValid(x) && MathD.IsValid(y) && MathD.IsValid(z) && MathD.IsValid(w);
        #endregion Public Instance Methods

        #region Public Static Methods


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 MoveTowards(Vector4 current, Vector4 target, double maxDistanceDelta)
        {
            Vector4 toVector = target - current;
            double dist = toVector.magnitude;
            if (dist <= maxDistanceDelta || dist == 0) return target;
            return current + toVector / dist * maxDistanceDelta;
        }

        /// <summary>
        /// Returns the Euclidean distance between the two given points.
        /// </summary>
        /// <param name="value1">The first point.</param>
        /// <param name="value2">The second point.</param>
        /// <returns>The distance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Distance(Vector4 value1, Vector4 value2)
        {
            double dx = value1.x - value2.x;
            double dy = value1.y - value2.y;
            double dz = value1.z - value2.z;
            double dw = value1.w - value2.w;

            double ls = dx * dx + dy * dy + dz * dz + dw * dw;

            return Math.Sqrt(ls);
        }

        /// <summary>
        /// Returns the Euclidean distance squared between the two given points.
        /// </summary>
        /// <param name="value1">The first point.</param>
        /// <param name="value2">The second point.</param>
        /// <returns>The distance squared.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DistanceSquared(Vector4 value1, Vector4 value2)
        {
            double dx = value1.x - value2.x;
            double dy = value1.y - value2.y;
            double dz = value1.z - value2.z;
            double dw = value1.w - value2.w;

            return dx * dx + dy * dy + dz * dz + dw * dw;
        }

        /// <summary>
        /// Returns a vector with the same direction as the given vector, but with a length of 1.
        /// </summary>
        /// <param name="vector">The vector to normalize.</param>
        /// <returns>The normalized vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Normalize(Vector4 vector)
        {
            double ls = vector.x * vector.x + vector.y * vector.y + vector.z * vector.z + vector.w * vector.w;
            double invNorm = 1.0 / Math.Sqrt(ls);

            return new Vector4(
                vector.x * invNorm,
                vector.y * invNorm,
                vector.z * invNorm,
                vector.w * invNorm);
        }

        /// <summary>
        /// Restricts a vector between a min and max value.
        /// </summary>
        /// <param name="value1">The source vector.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        /// <returns>The restricted vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Clamp(Vector4 value1, Vector4 min, Vector4 max)
        {
            // This compare order is very important!!!
            // We must follow HLSL behavior in the case user specified min value is bigger than max value.

            double x = value1.x;
            x = (x > max.x) ? max.x : x;
            x = (x < min.x) ? min.x : x;

            double y = value1.y;
            y = (y > max.y) ? max.y : y;
            y = (y < min.y) ? min.y : y;

            double z = value1.z;
            z = (z > max.z) ? max.z : z;
            z = (z < min.z) ? min.z : z;

            double w = value1.w;
            w = (w > max.w) ? max.w : w;
            w = (w < min.w) ? min.w : w;

            return new Vector4(x, y, z, w);
        }

        /// <summary>
        /// Linearly interpolates between two vectors based on the given weighting.
        /// </summary>
        /// <param name="value1">The first source vector.</param>
        /// <param name="value2">The second source vector.</param>
        /// <param name="amount">Value between 0 and 1 indicating the weight of the second source vector.</param>
        /// <returns>The interpolated vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Lerp(Vector4 value1, Vector4 value2, double amount)
        {
            return new Vector4(
                value1.x + (value2.x - value1.x) * amount,
                value1.y + (value2.y - value1.y) * amount,
                value1.z + (value2.z - value1.z) * amount,
                value1.w + (value2.w - value1.w) * amount);
        }

        /// <summary>
        /// Transforms a vector by the given matrix.
        /// </summary>
        /// <param name="position">The source vector.</param>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>The transformed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Transform(Vector2 position, Matrix4x4 matrix)
        {
            return new Vector4(
                position.x * matrix.M11 + position.y * matrix.M21 + matrix.M41,
                position.x * matrix.M12 + position.y * matrix.M22 + matrix.M42,
                position.x * matrix.M13 + position.y * matrix.M23 + matrix.M43,
                position.x * matrix.M14 + position.y * matrix.M24 + matrix.M44);
        }

        /// <summary>
        /// Transforms a vector by the given matrix.
        /// </summary>
        /// <param name="position">The source vector.</param>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>The transformed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Transform(Vector3 position, Matrix4x4 matrix)
        {
            return new Vector4(
                position.x * matrix.M11 + position.y * matrix.M21 + position.z * matrix.M31 + matrix.M41,
                position.x * matrix.M12 + position.y * matrix.M22 + position.z * matrix.M32 + matrix.M42,
                position.x * matrix.M13 + position.y * matrix.M23 + position.z * matrix.M33 + matrix.M43,
                position.x * matrix.M14 + position.y * matrix.M24 + position.z * matrix.M34 + matrix.M44);
        }

        /// <summary>
        /// Transforms a vector by the given matrix.
        /// </summary>
        /// <param name="vector">The source vector.</param>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>The transformed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Transform(Vector4 vector, Matrix4x4 matrix)
        {
            return new Vector4(
                vector.x * matrix.M11 + vector.y * matrix.M21 + vector.z * matrix.M31 + vector.w * matrix.M41,
                vector.x * matrix.M12 + vector.y * matrix.M22 + vector.z * matrix.M32 + vector.w * matrix.M42,
                vector.x * matrix.M13 + vector.y * matrix.M23 + vector.z * matrix.M33 + vector.w * matrix.M43,
                vector.x * matrix.M14 + vector.y * matrix.M24 + vector.z * matrix.M34 + vector.w * matrix.M44);
        }

        /// <summary>
        /// Transforms a vector by the given Quaternion rotation value.
        /// </summary>
        /// <param name="value">The source vector to be rotated.</param>
        /// <param name="rotation">The rotation to apply.</param>
        /// <returns>The transformed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Transform(Vector2 value, Quaternion rotation)
        {
            double x2 = rotation.x + rotation.x;
            double y2 = rotation.y + rotation.y;
            double z2 = rotation.z + rotation.z;

            double wx2 = rotation.w * x2;
            double wy2 = rotation.w * y2;
            double wz2 = rotation.w * z2;
            double xx2 = rotation.x * x2;
            double xy2 = rotation.x * y2;
            double xz2 = rotation.x * z2;
            double yy2 = rotation.y * y2;
            double yz2 = rotation.y * z2;
            double zz2 = rotation.z * z2;

            return new Vector4(
                value.x * (1.0 - yy2 - zz2) + value.y * (xy2 - wz2),
                value.x * (xy2 + wz2) + value.y * (1.0 - xx2 - zz2),
                value.x * (xz2 - wy2) + value.y * (yz2 + wx2),
                1.0);
        }

        /// <summary>
        /// Transforms a vector by the given Quaternion rotation value.
        /// </summary>
        /// <param name="value">The source vector to be rotated.</param>
        /// <param name="rotation">The rotation to apply.</param>
        /// <returns>The transformed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Transform(Vector3 value, Quaternion rotation)
        {
            double x2 = rotation.x + rotation.x;
            double y2 = rotation.y + rotation.y;
            double z2 = rotation.z + rotation.z;

            double wx2 = rotation.w * x2;
            double wy2 = rotation.w * y2;
            double wz2 = rotation.w * z2;
            double xx2 = rotation.x * x2;
            double xy2 = rotation.x * y2;
            double xz2 = rotation.x * z2;
            double yy2 = rotation.y * y2;
            double yz2 = rotation.y * z2;
            double zz2 = rotation.z * z2;

            return new Vector4(
                value.x * (1.0 - yy2 - zz2) + value.y * (xy2 - wz2) + value.z * (xz2 + wy2),
                value.x * (xy2 + wz2) + value.y * (1.0 - xx2 - zz2) + value.z * (yz2 - wx2),
                value.x * (xz2 - wy2) + value.y * (yz2 + wx2) + value.z * (1.0 - xx2 - yy2),
                1.0);
        }

        /// <summary>
        /// Transforms a vector by the given Quaternion rotation value.
        /// </summary>
        /// <param name="value">The source vector to be rotated.</param>
        /// <param name="rotation">The rotation to apply.</param>
        /// <returns>The transformed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Transform(Vector4 value, Quaternion rotation)
        {
            double x2 = rotation.x + rotation.x;
            double y2 = rotation.y + rotation.y;
            double z2 = rotation.z + rotation.z;

            double wx2 = rotation.w * x2;
            double wy2 = rotation.w * y2;
            double wz2 = rotation.w * z2;
            double xx2 = rotation.x * x2;
            double xy2 = rotation.x * y2;
            double xz2 = rotation.x * z2;
            double yy2 = rotation.y * y2;
            double yz2 = rotation.y * z2;
            double zz2 = rotation.z * z2;

            return new Vector4(
                value.x * (1.0 - yy2 - zz2) + value.y * (xy2 - wz2) + value.z * (xz2 + wy2),
                value.x * (xy2 + wz2) + value.y * (1.0 - xx2 - zz2) + value.z * (yz2 - wx2),
                value.x * (xz2 - wy2) + value.y * (yz2 + wx2) + value.z * (1.0 - xx2 - yy2),
                value.w);
        }

        /// <summary>
        /// Returns the dot product of two vectors.
        /// </summary>
        /// <param name="vector1">The first vector.</param>
        /// <param name="vector2">The second vector.</param>
        /// <returns>The dot product.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot(Vector4 vector1, Vector4 vector2)
        {
            return vector1.x * vector2.x +
                   vector1.y * vector2.y +
                   vector1.z * vector2.z +
                   vector1.w * vector2.w;
        }

        /// <summary>
        /// Returns a vector whose elements are the minimum of each of the pairs of elements in the two source vectors.
        /// </summary>
        /// <param name="value1">The first source vector.</param>
        /// <param name="value2">The second source vector.</param>
        /// <returns>The minimized vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Min(Vector4 value1, Vector4 value2)
        {
            return new Vector4(
                (value1.x < value2.x) ? value1.x : value2.x,
                (value1.y < value2.y) ? value1.y : value2.y,
                (value1.z < value2.z) ? value1.z : value2.z,
                (value1.w < value2.w) ? value1.w : value2.w);
        }

        /// <summary>
        /// Returns a vector whose elements are the maximum of each of the pairs of elements in the two source vectors.
        /// </summary>
        /// <param name="value1">The first source vector.</param>
        /// <param name="value2">The second source vector.</param>
        /// <returns>The maximized vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Max(Vector4 value1, Vector4 value2)
        {
            return new Vector4(
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
        public static Vector4 Abs(Vector4 value)
        {
            return new Vector4(Math.Abs(value.x), Math.Abs(value.y), Math.Abs(value.z), Math.Abs(value.w));
        }

        /// <summary>
        /// Returns a vector whose elements are the square root of each of the source vector's elements.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The square root vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 SquareRoot(Vector4 value)
        {
            return new Vector4(Math.Sqrt(value.x), Math.Sqrt(value.y), Math.Sqrt(value.z), Math.Sqrt(value.w));
        }
        #endregion Public Static Methods

        #region Public static operators
        /// <summary>
        /// Adds two vectors together.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The summed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator +(Vector4 left, Vector4 right)
        {
            return new Vector4(left.x + right.x, left.y + right.y, left.z + right.z, left.w + right.w);
        }

        /// <summary>
        /// Subtracts the second vector from the first.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The difference vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 left, Vector4 right)
        {
            return new Vector4(left.x - right.x, left.y - right.y, left.z - right.z, left.w - right.w);
        }

        /// <summary>
        /// Multiplies two vectors together.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The product vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Vector4 left, Vector4 right)
        {
            return new Vector4(left.x * right.x, left.y * right.y, left.z * right.z, left.w * right.w);
        }

        /// <summary>
        /// Multiplies a vector by the given scalar.
        /// </summary>
        /// <param name="left">The source vector.</param>
        /// <param name="right">The scalar value.</param>
        /// <returns>The scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Vector4 left, Double right)
        {
            return left * new Vector4(right);
        }

        /// <summary>
        /// Multiplies a vector by the given scalar.
        /// </summary>
        /// <param name="left">The scalar value.</param>
        /// <param name="right">The source vector.</param>
        /// <returns>The scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Double left, Vector4 right)
        {
            return new Vector4(left) * right;
        }

        /// <summary>
        /// Divides the first vector by the second.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The vector resulting from the division.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator /(Vector4 left, Vector4 right)
        {
            return new Vector4(left.x / right.x, left.y / right.y, left.z / right.z, left.w / right.w);
        }

        /// <summary>
        /// Divides the vector by the given scalar.
        /// </summary>
        /// <param name="value1">The source vector.</param>
        /// <param name="value2">The scalar value.</param>
        /// <returns>The result of the division.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator /(Vector4 value1, double value2)
        {
            double invDiv = 1.0 / value2;

            return new Vector4(
                value1.x * invDiv,
                value1.y * invDiv,
                value1.z * invDiv,
                value1.w * invDiv);
        }

        /// <summary>
        /// Negates a given vector.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The negated vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 value)
        {
            return zero - value;
        }

        /// <summary>
        /// Returns a boolean indicating whether the two given vectors are equal.
        /// </summary>
        /// <param name="left">The first vector to compare.</param>
        /// <param name="right">The second vector to compare.</param>
        /// <returns>True if the vectors are equal; False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector4 left, Vector4 right)
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
        public static bool operator !=(Vector4 left, Vector4 right)
        {
            return !(left == right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator System.Numerics.Vector4(Vector4 value)
        {
            return new System.Numerics.Vector4((float)value.x, (float)value.y, (float)value.z, (float)value.w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector4(System.Numerics.Vector4 value)
        {
            return new Vector4(value.X, value.Y, value.Z, value.W);
        }
        #endregion Public static operators
    }
}
