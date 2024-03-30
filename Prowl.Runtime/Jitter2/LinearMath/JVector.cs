/*
 * Copyright (c) Thorben Linneweber and others
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Jitter2.LinearMath;

/// <summary>
/// Represents a three-dimensional vector using three floating-point numbers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 12)]
public struct JVector
{
    internal static JVector InternalZero;
    internal static JVector Arbitrary;

    public float X;
    public float Y;
    public float Z;

    public static readonly JVector Zero;
    public static readonly JVector UnitX;
    public static readonly JVector UnitY;
    public static readonly JVector UnitZ;
    public static readonly JVector One;
    public static readonly JVector MinValue;
    public static readonly JVector MaxValue;

    static JVector()
    {
        One = new JVector(1, 1, 1);
        Zero = new JVector(0, 0, 0);
        UnitX = new JVector(1, 0, 0);
        UnitY = new JVector(0, 1, 0);
        UnitZ = new JVector(0, 0, 1);
        MinValue = new JVector(float.MinValue);
        MaxValue = new JVector(float.MaxValue);
        Arbitrary = new JVector(1, 1, 1);
        InternalZero = Zero;
    }

    public JVector(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public void Set(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public JVector(float xyz)
    {
        X = xyz;
        Y = xyz;
        Z = xyz;
    }

    public unsafe ref float UnsafeGet(int index)
    {
        float* ptr = (float*)Unsafe.AsPointer(ref this);
        return ref ptr[index];
    }

    public unsafe float this[int i]
    {
        get
        {
            fixed (float* ptr = &X)
            {
                return ptr[i];
            }
        }
        set
        {
            fixed (float* ptr = &X)
            {
                ptr[i] = value;
            }
        }
    }

    public readonly override string ToString()
    {
        return $"{X:F6} {Y:F6} {Z:F6}";
    }

    public readonly override bool Equals(object? obj)
    {
        if (obj is not JVector) return false;
        JVector other = (JVector)obj;

        return X == other.X && Y == other.Y && Z == other.Z;
    }

    public static bool operator ==(JVector value1, JVector value2)
    {
        return value1.X == value2.X && value1.Y == value2.Y && value1.Z == value2.Z;
    }

    public static bool operator !=(JVector value1, JVector value2)
    {
        if (value1.X == value2.X && value1.Y == value2.Y)
        {
            return value1.Z != value2.Z;
        }

        return true;
    }

    public static JVector Min(in JVector value1, in JVector value2)
    {
        Min(value1, value2, out JVector result);
        return result;
    }

    public static void Min(in JVector value1, in JVector value2, out JVector result)
    {
        result.X = value1.X < value2.X ? value1.X : value2.X;
        result.Y = value1.Y < value2.Y ? value1.Y : value2.Y;
        result.Z = value1.Z < value2.Z ? value1.Z : value2.Z;
    }

    public static JVector Max(in JVector value1, in JVector value2)
    {
        Max(value1, value2, out JVector result);
        return result;
    }

    public static JVector Abs(in JVector value1)
    {
        return new JVector(MathF.Abs(value1.X), MathF.Abs(value1.Y), MathF.Abs(value1.Z));
    }

    public static float MaxAbs(in JVector value1)
    {
        JVector abs = Abs(value1);
        return MathF.Max(MathF.Max(abs.X, abs.Y), abs.Z);
    }

    public static void Max(in JVector value1, in JVector value2, out JVector result)
    {
        result.X = value1.X > value2.X ? value1.X : value2.X;
        result.Y = value1.Y > value2.Y ? value1.Y : value2.Y;
        result.Z = value1.Z > value2.Z ? value1.Z : value2.Z;
    }

    public void MakeZero()
    {
        X = 0.0f;
        Y = 0.0f;
        Z = 0.0f;
    }

    /// <summary>
    /// Calculates matrix \times vector, where vector is a column vector.
    /// </summary>
    public static JVector Transform(in JVector vector, in JMatrix matrix)
    {
        Transform(vector, matrix, out JVector result);
        return result;
    }

    /// <summary>
    /// Calculates matrix^\mathrf{T} \times vector, where vector is a column vector.
    /// </summary>
    public static JVector TransposedTransform(in JVector vector, in JMatrix matrix)
    {
        TransposedTransform(vector, matrix, out JVector result);
        return result;
    }

    /// <summary>
    /// Calculates matrix \times vector, where vector is a column vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Transform(in JVector vector, in JMatrix matrix, out JVector result)
    {
        float num0 = vector.X * matrix.M11 + vector.Y * matrix.M12 + vector.Z * matrix.M13;
        float num1 = vector.X * matrix.M21 + vector.Y * matrix.M22 + vector.Z * matrix.M23;
        float num2 = vector.X * matrix.M31 + vector.Y * matrix.M32 + vector.Z * matrix.M33;

        result.X = num0;
        result.Y = num1;
        result.Z = num2;
    }

    /// <summary>
    /// Calculates matrix^\mathrf{T} \times vector, where vector is a column vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransposedTransform(in JVector vector, in JMatrix matrix, out JVector result)
    {
        float num0 = vector.X * matrix.M11 + vector.Y * matrix.M21 + vector.Z * matrix.M31;
        float num1 = vector.X * matrix.M12 + vector.Y * matrix.M22 + vector.Z * matrix.M32;
        float num2 = vector.X * matrix.M13 + vector.Y * matrix.M23 + vector.Z * matrix.M33;

        result.X = num0;
        result.Y = num1;
        result.Z = num2;
    }

    /// <summary>
    /// Calculates the outer product.
    /// </summary>
    public static JMatrix Outer(in JVector u, in JVector v)
    {
        JMatrix result;
        result.M11 = u.X * v.X;
        result.M12 = u.X * v.Y;
        result.M13 = u.X * v.Z;
        result.M21 = u.Y * v.X;
        result.M22 = u.Y * v.Y;
        result.M23 = u.Y * v.Z;
        result.M31 = u.Z * v.X;
        result.M32 = u.Z * v.Y;
        result.M33 = u.Z * v.Z;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(in JVector vector1, in JVector vector2)
    {
        return vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z;
    }

    public static JVector Add(in JVector value1, in JVector value2)
    {
        Add(value1, value2, out JVector result);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(in JVector value1, in JVector value2, out JVector result)
    {
        result.X = value1.X + value2.X;
        result.Y = value1.Y + value2.Y;
        result.Z = value1.Z + value2.Z;
    }

    public static JVector Subtract(JVector value1, JVector value2)
    {
        Subtract(value1, value2, out JVector result);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Subtract(in JVector value1, in JVector value2, out JVector result)
    {
        float num0 = value1.X - value2.X;
        float num1 = value1.Y - value2.Y;
        float num2 = value1.Z - value2.Z;

        result.X = num0;
        result.Y = num1;
        result.Z = num2;
    }

    public static JVector Cross(in JVector vector1, in JVector vector2)
    {
        Cross(vector1, vector2, out JVector result);
        return result;
    }

    public static void Cross(in JVector vector1, in JVector vector2, out JVector result)
    {
        float num0 = vector1.Y * vector2.Z - vector1.Z * vector2.Y;
        float num1 = vector1.Z * vector2.X - vector1.X * vector2.Z;
        float num2 = vector1.X * vector2.Y - vector1.Y * vector2.X;

        result.X = num0;
        result.Y = num1;
        result.Z = num2;
    }

    public readonly override int GetHashCode()
    {
        return X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode();
    }

    public void Negate()
    {
        X = -X;
        Y = -Y;
        Z = -Z;
    }

    public static JVector Negate(in JVector value)
    {
        Negate(value, out JVector result);
        return result;
    }

    public static void Negate(in JVector value, out JVector result)
    {
        float num0 = -value.X;
        float num1 = -value.Y;
        float num2 = -value.Z;

        result.X = num0;
        result.Y = num1;
        result.Z = num2;
    }

    public static JVector Normalize(in JVector value)
    {
        Normalize(value, out JVector result);
        return result;
    }

    public void Normalize()
    {
        float num2 = X * X + Y * Y + Z * Z;
        float num = 1f / (float)Math.Sqrt(num2);
        X *= num;
        Y *= num;
        Z *= num;
    }

    public static void Normalize(in JVector value, out JVector result)
    {
        float num2 = value.X * value.X + value.Y * value.Y + value.Z * value.Z;
        float num = 1f / (float)Math.Sqrt(num2);
        result.X = value.X * num;
        result.Y = value.Y * num;
        result.Z = value.Z * num;
    }

    public readonly float LengthSquared()
    {
        return X * X + Y * Y + Z * Z;
    }

    public readonly float Length()
    {
        return MathF.Sqrt(X * X + Y * Y + Z * Z);
    }

    public static void Swap(ref JVector vector1, ref JVector vector2)
    {
        (vector2, vector1) = (vector1, vector2);
    }

    public static JVector Multiply(in JVector value1, float scaleFactor)
    {
        Multiply(value1, scaleFactor, out JVector result);
        return result;
    }

    public void Multiply(float factor)
    {
        X *= factor;
        Y *= factor;
        Z *= factor;
    }

    public static void Multiply(in JVector value1, float scaleFactor, out JVector result)
    {
        result.X = value1.X * scaleFactor;
        result.Y = value1.Y * scaleFactor;
        result.Z = value1.Z * scaleFactor;
    }

    /// <summary>
    /// Calculates the cross product.
    /// </summary>
    public static JVector operator %(in JVector vector1, in JVector vector2)
    {
        JVector result;
        result.X = vector1.Y * vector2.Z - vector1.Z * vector2.Y;
        result.Y = vector1.Z * vector2.X - vector1.X * vector2.Z;
        result.Z = vector1.X * vector2.Y - vector1.Y * vector2.X;
        return result;
    }

    public static float operator *(in JVector vector1, in JVector vector2)
    {
        return vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z;
    }

    public static JVector operator *(in JVector value1, float value2)
    {
        JVector result;
        result.X = value1.X * value2;
        result.Y = value1.Y * value2;
        result.Z = value1.Z * value2;
        return result;
    }

    public static JVector operator *(float value1, in JVector value2)
    {
        JVector result;
        result.X = value2.X * value1;
        result.Y = value2.Y * value1;
        result.Z = value2.Z * value1;
        return result;
    }

    public static JVector operator -(in JVector value1, in JVector value2)
    {
        JVector result;
        result.X = value1.X - value2.X;
        result.Y = value1.Y - value2.Y;
        result.Z = value1.Z - value2.Z;
        return result;
    }

    public static JVector operator -(JVector left)
    {
        return Multiply(left, -1.0f);
    }

    public static JVector operator +(in JVector value1, in JVector value2)
    {
        JVector result;
        result.X = value1.X + value2.X;
        result.Y = value1.Y + value2.Y;
        result.Z = value1.Z + value2.Z;

        return result;
    }
}