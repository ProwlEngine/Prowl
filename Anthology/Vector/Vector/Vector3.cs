// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Vector;

/// <summary>
/// A structure encapsulating three double precision floating point values.
/// </summary>
public struct Vector3 : IEquatable<Vector3>, IFormattable
{
    public Double x, y, z;

    #region Constructors
    /// <summary> Constructs a vector whose elements are all the single specified value. </summary>
    public Vector3(Double value) : this(value, value, value) { }

    /// <summary> Constructs a Vector3 from the given Vector2 and a third value. </summary>
    public Vector3(Vector2 value, double z = 0f) : this(value.x, value.y, z) { }

    /// <summary> Constructs a vector with the given individual elements. </summary>
    public Vector3(Double x, Double y, Double z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
    #endregion Constructors

    #region Public Instance Properties
    public Vector3 normalized { get { return Normalize(this); } }

    public double magnitude { get { return MathD.Sqrt(x * x + y * y + z * z); } }

    public double sqrMagnitude { get { return x * x + y * y + z * z; } }

    public double this[int index]
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

    #region Public Instance Methods

    public System.Numerics.Vector3 ToFloat() => new System.Numerics.Vector3((float)x, (float)y, (float)z);

    public void Scale(Vector3 scale) { x *= scale.x; y *= scale.y; z *= scale.z; }

    public static Vector3 Scale(Vector3 v, Vector3 scale)
    {
        return new Vector3(v.x * scale.x, v.y * scale.y, v.z * scale.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Normalize()
    {
        double ls = x * x + y * y + z * z;
        double invNorm = 1.0 / Math.Sqrt(ls);
        x *= invNorm;
        y *= invNorm;
        z *= invNorm;
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
        return hash;
    }

    /// <summary>
    /// Returns a boolean indicating whether the given Object is equal to this Vector3 instance.
    /// </summary>
    /// <param name="obj">The Object to compare against.</param>
    /// <returns>True if the Object is equal to this Vector3; False otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Vector3 vector3 && Equals(vector3);

    /// <summary>
    /// Returns a boolean indicating whether the given Vector3 is equal to this Vector3 instance.
    /// </summary>
    /// <param name="other">The Vector3 to compare this instance to.</param>
    /// <returns>True if the other Vector3 is equal to this instance; False otherwise.</returns>
    public bool Equals(Vector3 other)
    {
        return MathD.ApproximatelyEquals(x, other.x) &&
               MathD.ApproximatelyEquals(y, other.y) &&
               MathD.ApproximatelyEquals(z, other.z);
    }

    /// <summary>
    /// Returns a String representing this Vector3 instance.
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
    {
        return ToString("G", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Returns a String representing this Vector3 instance, using the specified format to format individual elements.
    /// </summary>
    /// <param name="format">The format of individual elements.</param>
    /// <returns>The string representation.</returns>
    public string ToString(string format)
    {
        return ToString(format, CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Returns a String representing this Vector3 instance, using the specified format to format individual elements
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

    #region Public Static Properties
    public static Vector3 zero { get { return new Vector3(); } }
    public static Vector3 one { get { return new Vector3(1.0, 1.0, 1.0); } }
    public static Vector3 right { get { return new Vector3(1.0, 0.0, 0.0); } }
    public static Vector3 left { get { return new Vector3(-1.0, 0.0, 0.0); } }
    public static Vector3 up { get { return new Vector3(0.0, 1.0, 0.0); } }
    public static Vector3 down { get { return new Vector3(0.0, -1.0, 0.0); } }
    public static Vector3 forward { get { return new Vector3(0.0, 0.0, 1.0); } }
    public static Vector3 backward { get { return new Vector3(0.0, 0.0, -1.0); } }

    public static Vector3 infinity = new Vector3(MathD.Infinity, MathD.Infinity, MathD.Infinity);

    #endregion Public Static Properties

    #region Public Static Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double AngleBetween(Vector3 from, Vector3 to) => MathD.Acos(MathD.Clamp(Dot(from.normalized, to.normalized), -1f, 1f));

    /// <summary> Returns the Euclidean distance between the two given points. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Distance(Vector3 value1, Vector3 value2)
    {
        double dx = value1.x - value2.x;
        double dy = value1.y - value2.y;
        double dz = value1.z - value2.z;

        double ls = dx * dx + dy * dy + dz * dz;

        return Math.Sqrt(ls);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 MoveTowards(Vector3 current, Vector3 target, double maxDistanceDelta)
    {
        Vector3 toVector = target - current;
        double dist = toVector.magnitude;
        if (dist <= maxDistanceDelta || dist == 0) return target;
        return current + toVector / dist * maxDistanceDelta;
    }

    public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, double smoothTime, double maxSpeed = MathD.Infinity, double deltaTime = 0.02)
    {
        // Based on Game Programming Gems 4 Chapter 1.10
        smoothTime = MathD.Max(0.0001F, smoothTime);
        double omega = 2 / smoothTime;

        double x = omega * deltaTime;
        double exp = 1 / (1 + x + 0.48 * x * x + 0.235 * x * x * x);
        Vector3 change = current - target;
        Vector3 originalTo = target;

        // Clamp maximum speed
        double maxChange = maxSpeed * smoothTime;
        change = ClampMagnitude(change, maxChange);
        target = current - change;

        Vector3 temp = (currentVelocity + omega * change) * deltaTime;
        currentVelocity = (currentVelocity - omega * temp) * exp;
        Vector3 output = target + (change + temp) * exp;

        // Prevent overshooting
        if (Dot(originalTo - current, output - originalTo) > 0)
        {
            output = originalTo;
            currentVelocity = (output - originalTo) / deltaTime;
        }

        return output;
    }

    /// <summary> Returns a vector with the same direction as the given vector, but with a length of 1. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Normalize(Vector3 value)
    {
        double ls = value.x * value.x + value.y * value.y + value.z * value.z;
        double length = Math.Sqrt(ls);
        return new Vector3(value.x / length, value.y / length, value.z / length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ClampMagnitude(Vector3 vector, double maxLength)
    {
        if (vector.sqrMagnitude > maxLength * maxLength)
            return vector.normalized * maxLength;
        return vector;
    }

    /// <summary>
    /// Computes the cross product of two vectors.
    /// </summary>
    /// <param name="vector1">The first vector.</param>
    /// <param name="vector2">The second vector.</param>
    /// <returns>The cross product.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Cross(Vector3 vector1, Vector3 vector2)
    {
        return new Vector3(
            vector1.y * vector2.z - vector1.z * vector2.y,
            vector1.z * vector2.x - vector1.x * vector2.z,
            vector1.x * vector2.y - vector1.y * vector2.x);
    }

    /// <summary>
    /// Returns the reflection of a vector off a surface that has the specified normal.
    /// </summary>
    /// <param name="vector">The source vector.</param>
    /// <param name="normal">The normal of the surface being reflected off.</param>
    /// <returns>The reflected vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Reflect(Vector3 vector, Vector3 normal)
    {
        double dot = vector.x * normal.x + vector.y * normal.y + vector.z * normal.z;
        double tempX = normal.x * dot * 2;
        double tempY = normal.y * dot * 2;
        double tempZ = normal.z * dot * 2;
        return new Vector3(vector.x - tempX, vector.y - tempY, vector.z - tempZ);
    }

    /// <summary>
    /// Restricts a vector between a min and max value.
    /// </summary>
    /// <param name="value1">The source vector.</param>
    /// <param name="min">The minimum value.</param>
    /// <param name="max">The maximum value.</param>
    /// <returns>The restricted vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Clamp(Vector3 value1, Vector3 min, Vector3 max)
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

        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Linearly interpolates between two vectors based on the given weighting.
    /// </summary>
    /// <param name="value1">The first source vector.</param>
    /// <param name="value2">The second source vector.</param>
    /// <param name="amount">Value between 0 and 1 indicating the weight of the second source vector.</param>
    /// <returns>The interpolated vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Lerp(Vector3 value1, Vector3 value2, double amount)
    {
        return new Vector3(
            value1.x + (value2.x - value1.x) * amount,
            value1.y + (value2.y - value1.y) * amount,
            value1.z + (value2.z - value1.z) * amount);
    }

    /// <summary>
    /// Transforms a vector by the given matrix.
    /// </summary>
    /// <param name="position">The source vector.</param>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>The transformed vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Transform(Vector3 position, Matrix4x4 matrix)
    {
        return new Vector3(
            position.x * matrix.M11 + position.y * matrix.M21 + position.z * matrix.M31 + matrix.M41,
            position.x * matrix.M12 + position.y * matrix.M22 + position.z * matrix.M32 + matrix.M42,
            position.x * matrix.M13 + position.y * matrix.M23 + position.z * matrix.M33 + matrix.M43);
    }

    /// <summary>
    /// Transforms a vector normal by the given matrix.
    /// </summary>
    /// <param name="normal">The source vector.</param>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>The transformed vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 TransformNormal(Vector3 normal, Matrix4x4 matrix)
    {
        return new Vector3(
            normal.x * matrix.M11 + normal.y * matrix.M21 + normal.z * matrix.M31,
            normal.x * matrix.M12 + normal.y * matrix.M22 + normal.z * matrix.M32,
            normal.x * matrix.M13 + normal.y * matrix.M23 + normal.z * matrix.M33);
    }

    /// <summary>
    /// Transforms a vector by the given Quaternion rotation value.
    /// </summary>
    /// <param name="value">The source vector to be rotated.</param>
    /// <param name="rotation">The rotation to apply.</param>
    /// <returns>The transformed vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Transform(Vector3 value, Quaternion rotation)
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

        return new Vector3(
            value.x * (1.0 - yy2 - zz2) + value.y * (xy2 - wz2) + value.z * (xz2 + wy2),
            value.x * (xy2 + wz2) + value.y * (1.0 - xx2 - zz2) + value.z * (yz2 - wx2),
            value.x * (xz2 - wy2) + value.y * (yz2 + wx2) + value.z * (1.0 - xx2 - yy2));
    }

    /// <summary>
    /// Returns the dot product of two vectors.
    /// </summary>
    /// <param name="vector1">The first vector.</param>
    /// <param name="vector2">The second vector.</param>
    /// <returns>The dot product.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(Vector3 vector1, Vector3 vector2)
    {
        return vector1.x * vector2.x +
               vector1.y * vector2.y +
               vector1.z * vector2.z;
    }

    /// <summary>
    /// Returns a vector whose elements are the minimum of each of the pairs of elements in the two source vectors.
    /// </summary>
    /// <param name="value1">The first source vector.</param>
    /// <param name="value2">The second source vector.</param>
    /// <returns>The minimized vector.</returns>
    public static Vector3 Min(Vector3 value1, Vector3 value2)
    {
        return new Vector3(
            (value1.x < value2.x) ? value1.x : value2.x,
            (value1.y < value2.y) ? value1.y : value2.y,
            (value1.z < value2.z) ? value1.z : value2.z);
    }

    /// <summary>
    /// Returns a vector whose elements are the maximum of each of the pairs of elements in the two source vectors.
    /// </summary>
    /// <param name="value1">The first source vector.</param>
    /// <param name="value2">The second source vector.</param>
    /// <returns>The maximized vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Max(Vector3 value1, Vector3 value2)
    {
        return new Vector3(
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
    public static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Math.Abs(value.x), Math.Abs(value.y), Math.Abs(value.z));
    }

    /// <summary>
    /// Returns a vector whose elements are the square root of each of the source vector's elements.
    /// </summary>
    /// <param name="value">The source vector.</param>
    /// <returns>The square root vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 SquareRoot(Vector3 value)
    {
        return new Vector3(Math.Sqrt(value.x), Math.Sqrt(value.y), Math.Sqrt(value.z));
    }

    public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal)
    {
        // Normalize the plane normal to ensure it's a unit vector.
        planeNormal = Normalize(planeNormal);

        // Calculate the distance of the vector from the plane along the normal.
        double distance = Dot(vector, planeNormal);

        // Project the vector onto the plane.
        Vector3 projectedVector = vector - distance * planeNormal;

        return projectedVector;
    }

    /// <summary>
    /// Returns a normalized vector perpendicular to the input vector.
    /// Uses Vector3.up as the primary reference vector, falling back to Vector3.right
    /// when the input is parallel to up.
    /// </summary>
    /// <param name="vector">The input vector to find a perpendicular for</param>
    /// <returns>A normalized vector perpendicular to the input</returns>
    public static Vector3 GetPerpendicularVector(Vector3 vector)
    {
        var normalizedInput = vector.normalized;
        if (MathD.Abs(Dot(normalizedInput, up)) > 0.8f)
            return Cross(normalizedInput, right).normalized;
        return Cross(normalizedInput, up).normalized;
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
    public static Vector3 operator +(Vector3 left, Vector3 right)
    {
        return new Vector3(left.x + right.x, left.y + right.y, left.z + right.z);
    }

    /// <summary>
    /// Subtracts the second vector from the first.
    /// </summary>
    /// <param name="left">The first source vector.</param>
    /// <param name="right">The second source vector.</param>
    /// <returns>The difference vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator -(Vector3 left, Vector3 right)
    {
        return new Vector3(left.x - right.x, left.y - right.y, left.z - right.z);
    }

    /// <summary>
    /// Multiplies two vectors together.
    /// </summary>
    /// <param name="left">The first source vector.</param>
    /// <param name="right">The second source vector.</param>
    /// <returns>The product vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator *(Vector3 left, Vector3 right)
    {
        return new Vector3(left.x * right.x, left.y * right.y, left.z * right.z);
    }

    /// <summary>
    /// Multiplies a vector by the given scalar.
    /// </summary>
    /// <param name="left">The source vector.</param>
    /// <param name="right">The scalar value.</param>
    /// <returns>The scaled vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator *(Vector3 left, Double right)
    {
        return left * new Vector3(right);
    }

    /// <summary>
    /// Multiplies a vector by the given scalar.
    /// </summary>
    /// <param name="left">The scalar value.</param>
    /// <param name="right">The source vector.</param>
    /// <returns>The scaled vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator *(Double left, Vector3 right)
    {
        return new Vector3(left) * right;
    }

    /// <summary>
    /// Divides the first vector by the second.
    /// </summary>
    /// <param name="left">The first source vector.</param>
    /// <param name="right">The second source vector.</param>
    /// <returns>The vector resulting from the division.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator /(Vector3 left, Vector3 right)
    {
        return new Vector3(left.x / right.x, left.y / right.y, left.z / right.z);
    }

    /// <summary>
    /// Divides the vector by the given scalar.
    /// </summary>
    /// <param name="value1">The source vector.</param>
    /// <param name="value2">The scalar value.</param>
    /// <returns>The result of the division.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator /(Vector3 value1, double value2)
    {
        double invDiv = 1.0 / value2;

        return new Vector3(
            value1.x * invDiv,
            value1.y * invDiv,
            value1.z * invDiv);
    }

    /// <summary>
    /// Negates a given vector.
    /// </summary>
    /// <param name="value">The source vector.</param>
    /// <returns>The negated vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator -(Vector3 value)
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
    public static bool operator ==(Vector3 left, Vector3 right)
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
    public static bool operator !=(Vector3 left, Vector3 right)
    {
        return !left.Equals(right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator System.Numerics.Vector3(Vector3 value)
    {
        return new System.Numerics.Vector3((float)value.x, (float)value.y, (float)value.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector3(System.Numerics.Vector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }
    #endregion Public operator methods
}
