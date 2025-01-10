// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Vector;

/// <summary>
/// A structure encapsulating two double precision floating point values.
/// </summary>
public struct Vector2 : IEquatable<Vector2>, IFormattable
{
    public Double x, y;

    #region Constructors
    /// <summary> Constructs a vector whose elements are all the single specified value. </summary>
    public Vector2(Double value) : this(value, value) { }

    /// <summary> Constructs a vector with the given individual elements. </summary>
    public Vector2(Double x, Double y)
    {
        this.x = x;
        this.y = y;
    }
    #endregion Constructors

    #region Public Instance Properties
    public Vector2 normalized { get { return Normalize(this); } }

    public double magnitude { get { return MathD.Sqrt(x * x + y * y); } }

    public double sqrMagnitude { get { return x * x + y * y; } }

    public double this[int index]
    {
        get
        {
            switch (index)
            {
                case 0: return x;
                case 1: return y;
                default:
                    throw new IndexOutOfRangeException("Invalid Vector2 index.");
            }
        }

        set
        {
            switch (index)
            {
                case 0: x = value; break;
                case 1: y = value; break;
                default:
                    throw new IndexOutOfRangeException("Invalid Vector2 index.");
            }
        }
    }

    #endregion

    #region Public Instance methods

    public System.Numerics.Vector2 ToFloat() => new System.Numerics.Vector2((float)x, (float)y);

    public void Scale(Vector2 scale) { x *= scale.x; y *= scale.y; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Normalize()
    {
        double ls = x * x + y * y;
        double invNorm = 1.0 / Math.Sqrt(ls);
        x *= invNorm;
        y *= invNorm;
    }


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
    /// Returns a boolean indicating whether the given Object is equal to this Vector2 instance.
    /// </summary>
    /// <param name="obj">The Object to compare against.</param>
    /// <returns>True if the Object is equal to this Vector2; False otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj)
    {
        if (obj is not Vector2)
            return false;
        return Equals((Vector2)obj);
    }

    /// <summary>
    /// Returns a boolean indicating whether the given Vector2 is equal to this Vector2 instance.
    /// </summary>
    /// <param name="other">The Vector2 to compare this instance to.</param>
    /// <returns>True if the other Vector2 is equal to this instance; False otherwise.</returns>
    public bool Equals(Vector2 other)
    {
        return MathD.ApproximatelyEquals(x, other.x) && MathD.ApproximatelyEquals(y, other.y);
    }


    /// <summary>
    /// Returns a String representing this Vector2 instance.
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
    {
        return ToString("G", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Returns a String representing this Vector2 instance, using the specified format to format individual elements.
    /// </summary>
    /// <param name="format">The format of individual elements.</param>
    /// <returns>The string representation.</returns>
    public string ToString(string format)
    {
        return ToString(format, CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Returns a String representing this Vector2 instance, using the specified format to format individual elements
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

    public bool IsFinate() => MathD.IsValid(x) && MathD.IsValid(y);
    #endregion Public Instance Methods

    #region Public Static Properties
    public static Vector2 zero { get { return new Vector2(); } }
    public static Vector2 one { get { return new Vector2(1.0, 1.0); } }
    public static Vector2 right { get { return new Vector2(1.0, 0.0); } }
    public static Vector2 left { get { return new Vector2(-1.0, 0.0); } }
    public static Vector2 up { get { return new Vector2(0.0, 1.0); } }
    public static Vector2 down { get { return new Vector2(0.0, 1.0); } }

    public static Vector2 infinity = new Vector2(MathD.Infinity, MathD.Infinity);
    #endregion Public Static Properties

    #region Public Static Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double AngleBetween(Vector2 from, Vector2 to) { return MathD.Acos(MathD.Clamp(Dot(from.normalized, to.normalized), -1, 1)) * MathD.Rad2Deg; }


    /// <summary>
    /// Returns the Euclidean distance between the two given points.
    /// </summary>
    /// <param name="value1">The first point.</param>
    /// <param name="value2">The second point.</param>
    /// <returns>The distance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Distance(Vector2 value1, Vector2 value2)
    {
        double dx = value1.x - value2.x;
        double dy = value1.y - value2.y;

        double ls = dx * dx + dy * dy;

        return Math.Sqrt(ls);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 MoveTowards(Vector2 current, Vector2 target, double maxDistanceDelta)
    {
        Vector2 toVector = target - current;
        double dist = toVector.magnitude;
        if (dist <= maxDistanceDelta || dist == 0) return target;
        return current + toVector / dist * maxDistanceDelta;
    }

    /// <summary>
    /// Returns a vector with the same direction as the given vector, but with a length of 1.
    /// </summary>
    /// <param name="value">The vector to normalize.</param>
    /// <returns>The normalized vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Normalize(Vector2 value)
    {
        double ls = value.x * value.x + value.y * value.y;
        double invNorm = 1.0 / Math.Sqrt(ls);

        return new Vector2(
            value.x * invNorm,
            value.y * invNorm);
    }

    /// <summary>
    /// Clamps the magnitude of the given vector to the given value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 ClampMagnitude(Vector2 vector, double maxLength)
    {
        if (vector.sqrMagnitude > maxLength * maxLength)
            return vector.normalized * maxLength;
        return vector;
    }

    /// <summary>
    /// Returns the reflection of a vector off a surface that has the specified normal.
    /// </summary>
    /// <param name="vector">The source vector.</param>
    /// <param name="normal">The normal of the surface being reflected off.</param>
    /// <returns>The reflected vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Reflect(Vector2 vector, Vector2 normal)
    {
        double dot = vector.x * normal.x + vector.y * normal.y;

        return new Vector2(
            vector.x - 2.0 * dot * normal.x,
            vector.y - 2.0 * dot * normal.y);
    }

    /// <summary>
    /// Restricts a vector between a min and max value.
    /// </summary>
    /// <param name="value1">The source vector.</param>
    /// <param name="min">The minimum value.</param>
    /// <param name="max">The maximum value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Clamp(Vector2 value1, Vector2 min, Vector2 max)
    {
        // This compare order is very important!!!
        // We must follow HLSL behavior in the case user specified min value is bigger than max value.
        double x = value1.x;
        x = (x > max.x) ? max.x : x;
        x = (x < min.x) ? min.x : x;

        double y = value1.y;
        y = (y > max.y) ? max.y : y;
        y = (y < min.y) ? min.y : y;

        return new Vector2(x, y);
    }

    /// <summary>
    /// Linearly interpolates between two vectors based on the given weighting.
    /// </summary>
    /// <param name="value1">The first source vector.</param>
    /// <param name="value2">The second source vector.</param>
    /// <param name="amount">Value between 0 and 1 indicating the weight of the second source vector.</param>
    /// <returns>The interpolated vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Lerp(Vector2 value1, Vector2 value2, double amount)
    {
        return new Vector2(
            value1.x + (value2.x - value1.x) * amount,
            value1.y + (value2.y - value1.y) * amount);
    }

    /// <summary>
    /// Transforms a vector by the given matrix.
    /// </summary>
    /// <param name="position">The source vector.</param>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>The transformed vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Transform(Vector2 position, Matrix4x4 matrix)
    {
        return new Vector2(
            position.x * matrix.M11 + position.y * matrix.M21 + matrix.M41,
            position.x * matrix.M12 + position.y * matrix.M22 + matrix.M42);
    }

    /// <summary>
    /// Transforms a vector normal by the given matrix.
    /// </summary>
    /// <param name="normal">The source vector.</param>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>The transformed vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 TransformNormal(Vector2 normal, Matrix4x4 matrix)
    {
        return new Vector2(
            normal.x * matrix.M11 + normal.y * matrix.M21,
            normal.x * matrix.M12 + normal.y * matrix.M22);
    }

    /// <summary>
    /// Transforms a vector by the given Quaternion rotation value.
    /// </summary>
    /// <param name="value">The source vector to be rotated.</param>
    /// <param name="rotation">The rotation to apply.</param>
    /// <returns>The transformed vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Transform(Vector2 value, Quaternion rotation)
    {
        double x2 = rotation.x + rotation.x;
        double y2 = rotation.y + rotation.y;
        double z2 = rotation.z + rotation.z;

        double wz2 = rotation.w * z2;
        double xx2 = rotation.x * x2;
        double xy2 = rotation.x * y2;
        double yy2 = rotation.y * y2;
        double zz2 = rotation.z * z2;

        return new Vector2(
            value.x * (1.0 - yy2 - zz2) + value.y * (xy2 - wz2),
            value.x * (xy2 + wz2) + value.y * (1.0 - xx2 - zz2));
    }

    /// <summary>
    /// Returns the dot product of two vectors.
    /// </summary>
    /// <param name="value1">The first vector.</param>
    /// <param name="value2">The second vector.</param>
    /// <returns>The dot product.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(Vector2 value1, Vector2 value2)
    {
        return value1.x * value2.x +
               value1.y * value2.y;
    }

    /// <summary>
    /// Returns a vector whose elements are the minimum of each of the pairs of elements in the two source vectors.
    /// </summary>
    /// <param name="value1">The first source vector.</param>
    /// <param name="value2">The second source vector.</param>
    /// <returns>The minimized vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Min(Vector2 value1, Vector2 value2)
    {
        return new Vector2(
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
    public static Vector2 Max(Vector2 value1, Vector2 value2)
    {
        return new Vector2(
            (value1.x > value2.x) ? value1.x : value2.x,
            (value1.y > value2.y) ? value1.y : value2.y);
    }

    /// <summary>
    /// Returns a vector whose elements are the absolute values of each of the source vector's elements.
    /// </summary>
    /// <param name="value">The source vector.</param>
    /// <returns>The absolute value vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Abs(Vector2 value)
    {
        return new Vector2(Math.Abs(value.x), Math.Abs(value.y));
    }

    /// <summary>
    /// Returns a vector whose elements are the square root of each of the source vector's elements.
    /// </summary>
    /// <param name="value">The source vector.</param>
    /// <returns>The square root vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 SquareRoot(Vector2 value)
    {
        return new Vector2(Math.Sqrt(value.x), Math.Sqrt(value.y));
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
    public static Vector2 operator +(Vector2 left, Vector2 right)
    {
        return new Vector2(left.x + right.x, left.y + right.y);
    }

    /// <summary>
    /// Subtracts the second vector from the first.
    /// </summary>
    /// <param name="left">The first source vector.</param>
    /// <param name="right">The second source vector.</param>
    /// <returns>The difference vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator -(Vector2 left, Vector2 right)
    {
        return new Vector2(left.x - right.x, left.y - right.y);
    }

    /// <summary>
    /// Multiplies two vectors together.
    /// </summary>
    /// <param name="left">The first source vector.</param>
    /// <param name="right">The second source vector.</param>
    /// <returns>The product vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(Vector2 left, Vector2 right)
    {
        return new Vector2(left.x * right.x, left.y * right.y);
    }

    /// <summary>
    /// Multiplies a vector by the given scalar.
    /// </summary>
    /// <param name="left">The scalar value.</param>
    /// <param name="right">The source vector.</param>
    /// <returns>The scaled vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(Double left, Vector2 right)
    {
        return new Vector2(left, left) * right;
    }

    /// <summary>
    /// Multiplies a vector by the given scalar.
    /// </summary>
    /// <param name="left">The source vector.</param>
    /// <param name="right">The scalar value.</param>
    /// <returns>The scaled vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(Vector2 left, Double right)
    {
        return left * new Vector2(right, right);
    }

    /// <summary>
    /// Divides the first vector by the second.
    /// </summary>
    /// <param name="left">The first source vector.</param>
    /// <param name="right">The second source vector.</param>
    /// <returns>The vector resulting from the division.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator /(Vector2 left, Vector2 right)
    {
        return new Vector2(left.x / right.x, left.y / right.y);
    }

    /// <summary>
    /// Divides the vector by the given scalar.
    /// </summary>
    /// <param name="value1">The source vector.</param>
    /// <param name="value2">The scalar value.</param>
    /// <returns>The result of the division.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator /(Vector2 value1, double value2)
    {
        double invDiv = 1.0 / value2;
        return new Vector2(
            value1.x * invDiv,
            value1.y * invDiv);
    }

    /// <summary>
    /// Negates a given vector.
    /// </summary>
    /// <param name="value">The source vector.</param>
    /// <returns>The negated vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator -(Vector2 value)
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
    public static bool operator ==(Vector2 left, Vector2 right)
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
    public static bool operator !=(Vector2 left, Vector2 right)
    {
        return !(left == right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator System.Numerics.Vector2(Vector2 value)
    {
        return new System.Numerics.Vector2((float)value.x, (float)value.y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector2(System.Numerics.Vector2 value)
    {
        return new Vector2(value.X, value.Y);
    }

    #endregion Public Static Operators
}
