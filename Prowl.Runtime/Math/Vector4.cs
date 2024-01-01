// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Runtime
{
    /// <summary>
    /// A structure encapsulating four single precision floating point values and provides hardware accelerated methods.
    /// </summary>
    public partial struct Vector4 : IEquatable<Vector4>, IFormattable
    {
        #region Public Static Properties
        /// <summary>
        /// Returns the vector (0,0,0,0).
        /// </summary>
        public static Vector4 Zero { get { return new Vector4(); } }
        /// <summary>
        /// Returns the vector (1,1,1,1).
        /// </summary>
        public static Vector4 One { get { return new Vector4(1.0, 1.0, 1.0, 1.0); } }
        /// <summary>
        /// Returns the vector (1,0,0,0).
        /// </summary>
        public static Vector4 UnitX { get { return new Vector4(1.0, 0.0, 0.0, 0.0); } }
        /// <summary>
        /// Returns the vector (0,1,0,0).
        /// </summary>
        public static Vector4 UnitY { get { return new Vector4(0.0, 1.0, 0.0, 0.0); } }
        /// <summary>
        /// Returns the vector (0,0,1,0).
        /// </summary>
        public static Vector4 UnitZ { get { return new Vector4(0.0, 0.0, 1.0, 0.0); } }
        /// <summary>
        /// Returns the vector (0,0,0,1).
        /// </summary>
        public static Vector4 UnitW { get { return new Vector4(0.0, 0.0, 0.0, 1.0); } }
        #endregion Public Static Properties

        #region Public instance methods

        public System.Numerics.Vector4 ToFloat() => new System.Numerics.Vector4((float)x, (float)y, (float)z, (float)w);

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            int hash = this.x.GetHashCode();
            hash = HashCode.Combine(hash, this.y.GetHashCode());
            hash = HashCode.Combine(hash, this.z.GetHashCode());
            hash = HashCode.Combine(hash, this.w.GetHashCode());
            return hash;
        }

        /// <summary>
        /// Returns a boolean indicating whether the given Object is equal to this Vector4 instance.
        /// </summary>
        /// <param name="obj">The Object to compare against.</param>
        /// <returns>True if the Object is equal to this Vector4; False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj)
        {
            if (!(obj is Vector4))
                return false;
            return Equals((Vector4)obj);
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
        public string ToString(string format, IFormatProvider formatProvider)
        {
            StringBuilder sb = new StringBuilder();
            string separator = NumberFormatInfo.GetInstance(formatProvider).NumberGroupSeparator;
            sb.Append('<');
            sb.Append(this.x.ToString(format, formatProvider));
            sb.Append(separator);
            sb.Append(' ');
            sb.Append(this.y.ToString(format, formatProvider));
            sb.Append(separator);
            sb.Append(' ');
            sb.Append(this.z.ToString(format, formatProvider));
            sb.Append(separator);
            sb.Append(' ');
            sb.Append(this.w.ToString(format, formatProvider));
            sb.Append('>');
            return sb.ToString();
        }

        /// <summary>
        /// Returns the length of the vector. This operation is cheaper than Length().
        /// </summary>
        /// <returns>The vector's length.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Length()
        {
            double ls = x * x + y * y + z * z + w * w;

                return (double)Math.Sqrt((double)ls);
        }

        /// <summary>
        /// Returns the length of the vector squared.
        /// </summary>
        /// <returns>The vector's length squared.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double LengthSquared()
        {
            return x * x + y * y + z * z + w * w;
        }
        #endregion Public Instance Methods

        #region Public Static Methods
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

                return (double)Math.Sqrt((double)ls);
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
                double invNorm = 1.0 / (double)Math.Sqrt((double)ls);

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
            double x2 = rotation.X + rotation.X;
            double y2 = rotation.Y + rotation.Y;
            double z2 = rotation.Z + rotation.Z;

            double wx2 = rotation.W * x2;
            double wy2 = rotation.W * y2;
            double wz2 = rotation.W * z2;
            double xx2 = rotation.X * x2;
            double xy2 = rotation.X * y2;
            double xz2 = rotation.X * z2;
            double yy2 = rotation.Y * y2;
            double yz2 = rotation.Y * z2;
            double zz2 = rotation.Z * z2;

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
            double x2 = rotation.X + rotation.X;
            double y2 = rotation.Y + rotation.Y;
            double z2 = rotation.Z + rotation.Z;

            double wx2 = rotation.W * x2;
            double wy2 = rotation.W * y2;
            double wz2 = rotation.W * z2;
            double xx2 = rotation.X * x2;
            double xy2 = rotation.X * y2;
            double xz2 = rotation.X * z2;
            double yy2 = rotation.Y * y2;
            double yz2 = rotation.Y * z2;
            double zz2 = rotation.Z * z2;

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
            double x2 = rotation.X + rotation.X;
            double y2 = rotation.Y + rotation.Y;
            double z2 = rotation.Z + rotation.Z;

            double wx2 = rotation.W * x2;
            double wy2 = rotation.W * y2;
            double wz2 = rotation.W * z2;
            double xx2 = rotation.X * x2;
            double xy2 = rotation.X * y2;
            double xz2 = rotation.X * z2;
            double yy2 = rotation.Y * y2;
            double yz2 = rotation.Y * z2;
            double zz2 = rotation.Z * z2;

            return new Vector4(
                value.x * (1.0 - yy2 - zz2) + value.y * (xy2 - wz2) + value.z * (xz2 + wy2),
                value.x * (xy2 + wz2) + value.y * (1.0 - xx2 - zz2) + value.z * (yz2 - wx2),
                value.x * (xz2 - wy2) + value.y * (yz2 + wx2) + value.z * (1.0 - xx2 - yy2),
                value.w);
        }
        #endregion Public Static Methods

        #region Public operator methods
        // All these methods should be inlines as they are implemented
        // over JIT intrinsics

        /// <summary>
        /// Adds two vectors together.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The summed vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Add(Vector4 left, Vector4 right)
        {
            return left + right;
        }

        /// <summary>
        /// Subtracts the second vector from the first.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The difference vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Subtract(Vector4 left, Vector4 right)
        {
            return left - right;
        }

        /// <summary>
        /// Multiplies two vectors together.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The product vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Multiply(Vector4 left, Vector4 right)
        {
            return left * right;
        }

        /// <summary>
        /// Multiplies a vector by the given scalar.
        /// </summary>
        /// <param name="left">The source vector.</param>
        /// <param name="right">The scalar value.</param>
        /// <returns>The scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Multiply(Vector4 left, Double right)
        {
            return left * new Vector4(right, right, right, right);
        }

        /// <summary>
        /// Multiplies a vector by the given scalar.
        /// </summary>
        /// <param name="left">The scalar value.</param>
        /// <param name="right">The source vector.</param>
        /// <returns>The scaled vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Multiply(Double left, Vector4 right)
        {
            return new Vector4(left, left, left, left) * right;
        }

        /// <summary>
        /// Divides the first vector by the second.
        /// </summary>
        /// <param name="left">The first source vector.</param>
        /// <param name="right">The second source vector.</param>
        /// <returns>The vector resulting from the division.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Divide(Vector4 left, Vector4 right)
        {
            return left / right;
        }

        /// <summary>
        /// Divides the vector by the given scalar.
        /// </summary>
        /// <param name="left">The source vector.</param>
        /// <param name="divisor">The scalar value.</param>
        /// <returns>The result of the division.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Divide(Vector4 left, Double divisor)
        {
            return left / divisor;
        }

        /// <summary>
        /// Negates a given vector.
        /// </summary>
        /// <param name="value">The source vector.</param>
        /// <returns>The negated vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Negate(Vector4 value)
        {
            return -value;
        }
        #endregion Public operator methods
    }
}
