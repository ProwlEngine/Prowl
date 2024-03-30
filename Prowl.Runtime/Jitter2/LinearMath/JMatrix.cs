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
/// 3x3 matrix of 32 bit float values in column major format.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 36)]
public struct JMatrix
{
    [FieldOffset(0)] public float M11;
    [FieldOffset(4)] public float M21;
    [FieldOffset(8)] public float M31;
    [FieldOffset(12)] public float M12;
    [FieldOffset(16)] public float M22;
    [FieldOffset(20)] public float M32;
    [FieldOffset(24)] public float M13;
    [FieldOffset(28)] public float M23;
    [FieldOffset(32)] public float M33;

    public static readonly JMatrix Identity;
    public static readonly JMatrix Zero;

    static JMatrix()
    {
        Zero = new JMatrix();

        Identity = new JMatrix
        {
            M11 = 1.0f,
            M22 = 1.0f,
            M33 = 1.0f
        };
    }

    public JMatrix(float m11, float m12, float m13, float m21, float m22, float m23, float m31, float m32, float m33)
    {
        M11 = m11;
        M12 = m12;
        M13 = m13;
        M21 = m21;
        M22 = m22;
        M23 = m23;
        M31 = m31;
        M32 = m32;
        M33 = m33;
    }

    public static JMatrix FromColumns(in JVector col1, in JVector col2, in JVector col3)
    {
        Unsafe.SkipInit(out JMatrix res);
        res.UnsafeGet(0) = col1;
        res.UnsafeGet(1) = col2;
        res.UnsafeGet(2) = col3;
        return res;
    }

    public unsafe ref JVector UnsafeGet(int index)
    {
        JVector* ptr = (JVector*)Unsafe.AsPointer(ref this);
        return ref ptr[index];
    }

    public unsafe JVector GetColumn(int index)
    {
        fixed (float* ptr = &M11)
        {
            JVector* vptr = (JVector*)ptr;
            return vptr[index];
        }
    }

    public static JMatrix Multiply(in JMatrix matrix1, in JMatrix matrix2)
    {
        Multiply(matrix1, matrix2, out JMatrix result);
        return result;
    }

    public static JMatrix MultiplyTransposed(in JMatrix matrix1, in JMatrix matrix2)
    {
        MultiplyTransposed(matrix1, matrix2, out JMatrix result);
        return result;
    }

    public static JMatrix TransposedMultiply(in JMatrix matrix1, in JMatrix matrix2)
    {
        TransposedMultiply(matrix1, matrix2, out JMatrix result);
        return result;
    }

    public static JMatrix CreateRotationMatrix(JVector axis, float angle)
    {
        float c = MathF.Cos(angle / 2.0f);
        float s = MathF.Sin(angle / 2.0f);
        axis *= s;
        JQuaternion jq = new(axis.X, axis.Y, axis.Z, c);
        CreateFromQuaternion(in jq, out JMatrix result);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Multiply(in JMatrix matrix1, in JMatrix matrix2, out JMatrix result)
    {
        float num0 = matrix1.M11 * matrix2.M11 + matrix1.M12 * matrix2.M21 + matrix1.M13 * matrix2.M31;
        float num1 = matrix1.M11 * matrix2.M12 + matrix1.M12 * matrix2.M22 + matrix1.M13 * matrix2.M32;
        float num2 = matrix1.M11 * matrix2.M13 + matrix1.M12 * matrix2.M23 + matrix1.M13 * matrix2.M33;
        float num3 = matrix1.M21 * matrix2.M11 + matrix1.M22 * matrix2.M21 + matrix1.M23 * matrix2.M31;
        float num4 = matrix1.M21 * matrix2.M12 + matrix1.M22 * matrix2.M22 + matrix1.M23 * matrix2.M32;
        float num5 = matrix1.M21 * matrix2.M13 + matrix1.M22 * matrix2.M23 + matrix1.M23 * matrix2.M33;
        float num6 = matrix1.M31 * matrix2.M11 + matrix1.M32 * matrix2.M21 + matrix1.M33 * matrix2.M31;
        float num7 = matrix1.M31 * matrix2.M12 + matrix1.M32 * matrix2.M22 + matrix1.M33 * matrix2.M32;
        float num8 = matrix1.M31 * matrix2.M13 + matrix1.M32 * matrix2.M23 + matrix1.M33 * matrix2.M33;

        result.M11 = num0;
        result.M12 = num1;
        result.M13 = num2;
        result.M21 = num3;
        result.M22 = num4;
        result.M23 = num5;
        result.M31 = num6;
        result.M32 = num7;
        result.M33 = num8;
    }

    public static JMatrix Add(JMatrix matrix1, JMatrix matrix2)
    {
        Add(matrix1, matrix2, out JMatrix result);
        return result;
    }

    /// <summary>
    /// Calculates matrix1 \times matrix2^\mathrm{T}.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MultiplyTransposed(in JMatrix matrix1, in JMatrix matrix2, out JMatrix result)
    {
        float num0 = matrix1.M11 * matrix2.M11 + matrix1.M12 * matrix2.M12 + matrix1.M13 * matrix2.M13;
        float num1 = matrix1.M11 * matrix2.M21 + matrix1.M12 * matrix2.M22 + matrix1.M13 * matrix2.M23;
        float num2 = matrix1.M11 * matrix2.M31 + matrix1.M12 * matrix2.M32 + matrix1.M13 * matrix2.M33;
        float num3 = matrix1.M21 * matrix2.M11 + matrix1.M22 * matrix2.M12 + matrix1.M23 * matrix2.M13;
        float num4 = matrix1.M21 * matrix2.M21 + matrix1.M22 * matrix2.M22 + matrix1.M23 * matrix2.M23;
        float num5 = matrix1.M21 * matrix2.M31 + matrix1.M22 * matrix2.M32 + matrix1.M23 * matrix2.M33;
        float num6 = matrix1.M31 * matrix2.M11 + matrix1.M32 * matrix2.M12 + matrix1.M33 * matrix2.M13;
        float num7 = matrix1.M31 * matrix2.M21 + matrix1.M32 * matrix2.M22 + matrix1.M33 * matrix2.M23;
        float num8 = matrix1.M31 * matrix2.M31 + matrix1.M32 * matrix2.M32 + matrix1.M33 * matrix2.M33;

        result.M11 = num0;
        result.M12 = num1;
        result.M13 = num2;
        result.M21 = num3;
        result.M22 = num4;
        result.M23 = num5;
        result.M31 = num6;
        result.M32 = num7;
        result.M33 = num8;
    }

    public static JMatrix CreateRotationX(float radians)
    {
        JMatrix result = Identity;

        float c = (float)Math.Cos(radians);
        float s = (float)Math.Sin(radians);

        // [  1  0  0  0 ]
        // [  0  c -s  0 ]
        // [  0  s  c  0 ]
        // [  0  0  0  1 ]
        result.M22 = c;
        result.M23 = -s;
        result.M32 = s;
        result.M33 = c;

        return result;
    }

    public static JMatrix CreateRotationY(float radians)
    {
        JMatrix result = Identity;

        float c = (float)Math.Cos(radians);
        float s = (float)Math.Sin(radians);

        // [  c  0  s  0 ]
        // [  0  1  0  0 ]
        // [ -s  0  c  0 ]
        // [  0  0  0  1 ]
        result.M11 = c;
        result.M13 = s;
        result.M31 = -s;
        result.M33 = c;

        return result;
    }

    public static JMatrix CreateRotationZ(float radians)
    {
        JMatrix result = Identity;

        float c = (float)Math.Cos(radians);
        float s = (float)Math.Sin(radians);

        // [  c -s  0  0 ]
        // [  s  c  0  0 ]
        // [  0  0  1  0 ]
        // [  0  0  0  1 ]
        result.M11 = c;
        result.M12 = -s;
        result.M21 = s;
        result.M22 = c;

        return result;
    }

    /// <summary>
    /// Create a scaling matrix.
    /// </summary>
    /// <returns></returns>
    public static JMatrix CreateScale(in JVector scale)
    {
        JMatrix result = Zero;

        result.M11 = scale.X;
        result.M22 = scale.Y;
        result.M33 = scale.Z;

        return result;
    }

    /// <summary>
    /// Create a scaling matrix.
    /// </summary>
    /// <returns></returns>
    public static JMatrix CreateScale(float x, float y, float z)
    {
        return CreateScale(new JVector(x, y, z));
    }

    /// <summary>
    /// Calculates matrix1^\mathrm{T} \times matrix2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransposedMultiply(in JMatrix matrix1, in JMatrix matrix2, out JMatrix result)
    {
        float num0 = matrix1.M11 * matrix2.M11 + matrix1.M21 * matrix2.M21 + matrix1.M31 * matrix2.M31;
        float num1 = matrix1.M11 * matrix2.M12 + matrix1.M21 * matrix2.M22 + matrix1.M31 * matrix2.M32;
        float num2 = matrix1.M11 * matrix2.M13 + matrix1.M21 * matrix2.M23 + matrix1.M31 * matrix2.M33;
        float num3 = matrix1.M12 * matrix2.M11 + matrix1.M22 * matrix2.M21 + matrix1.M32 * matrix2.M31;
        float num4 = matrix1.M12 * matrix2.M12 + matrix1.M22 * matrix2.M22 + matrix1.M32 * matrix2.M32;
        float num5 = matrix1.M12 * matrix2.M13 + matrix1.M22 * matrix2.M23 + matrix1.M32 * matrix2.M33;
        float num6 = matrix1.M13 * matrix2.M11 + matrix1.M23 * matrix2.M21 + matrix1.M33 * matrix2.M31;
        float num7 = matrix1.M13 * matrix2.M12 + matrix1.M23 * matrix2.M22 + matrix1.M33 * matrix2.M32;
        float num8 = matrix1.M13 * matrix2.M13 + matrix1.M23 * matrix2.M23 + matrix1.M33 * matrix2.M33;

        result.M11 = num0;
        result.M12 = num1;
        result.M13 = num2;
        result.M21 = num3;
        result.M22 = num4;
        result.M23 = num5;
        result.M31 = num6;
        result.M32 = num7;
        result.M33 = num8;
    }

    public static void Add(in JMatrix matrix1, in JMatrix matrix2, out JMatrix result)
    {
        result.M11 = matrix1.M11 + matrix2.M11;
        result.M12 = matrix1.M12 + matrix2.M12;
        result.M13 = matrix1.M13 + matrix2.M13;
        result.M21 = matrix1.M21 + matrix2.M21;
        result.M22 = matrix1.M22 + matrix2.M22;
        result.M23 = matrix1.M23 + matrix2.M23;
        result.M31 = matrix1.M31 + matrix2.M31;
        result.M32 = matrix1.M32 + matrix2.M32;
        result.M33 = matrix1.M33 + matrix2.M33;
    }

    public static void Subtract(in JMatrix matrix1, in JMatrix matrix2, out JMatrix result)
    {
        result.M11 = matrix1.M11 - matrix2.M11;
        result.M12 = matrix1.M12 - matrix2.M12;
        result.M13 = matrix1.M13 - matrix2.M13;
        result.M21 = matrix1.M21 - matrix2.M21;
        result.M22 = matrix1.M22 - matrix2.M22;
        result.M23 = matrix1.M23 - matrix2.M23;
        result.M31 = matrix1.M31 - matrix2.M31;
        result.M32 = matrix1.M32 - matrix2.M32;
        result.M33 = matrix1.M33 - matrix2.M33;
    }

    public readonly float Determinant()
    {
        return M11 * M22 * M33 + M12 * M23 * M31 + M13 * M21 * M32 -
               M31 * M22 * M13 - M32 * M23 * M11 - M33 * M21 * M12;
    }

    public static bool Inverse(in JMatrix matrix, out JMatrix result)
    {
        float det = matrix.Determinant();

        if (float.IsNaN(det) || float.IsInfinity(det))
        {
            result = new JMatrix();
            return false;
        }

        float num11 = matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32;
        float num12 = matrix.M13 * matrix.M32 - matrix.M12 * matrix.M33;
        float num13 = matrix.M12 * matrix.M23 - matrix.M22 * matrix.M13;

        float num21 = matrix.M23 * matrix.M31 - matrix.M33 * matrix.M21;
        float num22 = matrix.M11 * matrix.M33 - matrix.M31 * matrix.M13;
        float num23 = matrix.M13 * matrix.M21 - matrix.M23 * matrix.M11;

        float num31 = matrix.M21 * matrix.M32 - matrix.M31 * matrix.M22;
        float num32 = matrix.M12 * matrix.M31 - matrix.M32 * matrix.M11;
        float num33 = matrix.M11 * matrix.M22 - matrix.M21 * matrix.M12;

        float idet = 1.0f / det;

        result.M11 = num11 * idet;
        result.M12 = num12 * idet;
        result.M13 = num13 * idet;
        result.M21 = num21 * idet;
        result.M22 = num22 * idet;
        result.M23 = num23 * idet;
        result.M31 = num31 * idet;
        result.M32 = num32 * idet;
        result.M33 = num33 * idet;

        return true;
    }

    public static JMatrix Multiply(JMatrix matrix1, float scaleFactor)
    {
        Multiply(in matrix1, scaleFactor, out JMatrix result);
        return result;
    }

    public static void Multiply(in JMatrix matrix1, float scaleFactor, out JMatrix result)
    {
        float num = scaleFactor;
        result.M11 = matrix1.M11 * num;
        result.M12 = matrix1.M12 * num;
        result.M13 = matrix1.M13 * num;
        result.M21 = matrix1.M21 * num;
        result.M22 = matrix1.M22 * num;
        result.M23 = matrix1.M23 * num;
        result.M31 = matrix1.M31 * num;
        result.M32 = matrix1.M32 * num;
        result.M33 = matrix1.M33 * num;
    }

    public static JMatrix CreateFromQuaternion(JQuaternion quaternion)
    {
        CreateFromQuaternion(quaternion, out JMatrix result);
        return result;
    }

    public static void Absolute(in JMatrix matrix, out JMatrix result)
    {
        result.M11 = Math.Abs(matrix.M11);
        result.M12 = Math.Abs(matrix.M12);
        result.M13 = Math.Abs(matrix.M13);
        result.M21 = Math.Abs(matrix.M21);
        result.M22 = Math.Abs(matrix.M22);
        result.M23 = Math.Abs(matrix.M23);
        result.M31 = Math.Abs(matrix.M31);
        result.M32 = Math.Abs(matrix.M32);
        result.M33 = Math.Abs(matrix.M33);
    }

    public static void CreateFromQuaternion(in JQuaternion quaternion, out JMatrix result)
    {
        float r = quaternion.W;
        float i = quaternion.X;
        float j = quaternion.Y;
        float k = quaternion.Z;

        result.M11 = 1.0f - 2.0f * (j * j + k * k);
        result.M12 = 2.0f * (i * j - k * r);
        result.M13 = 2.0f * (i * k + j * r);
        result.M21 = 2.0f * (i * j + k * r);
        result.M22 = 1.0f - 2.0f * (i * i + k * k);
        result.M23 = 2.0f * (j * k - i * r);
        result.M31 = 2.0f * (i * k - j * r);
        result.M32 = 2.0f * (j * k + i * r);
        result.M33 = 1.0f - 2.0f * (i * i + j * j);
    }

    public static JMatrix Transpose(in JMatrix matrix)
    {
        Transpose(in matrix, out JMatrix result);
        return result;
    }

    /// <summary>
    /// Returns JMatrix(0, -vec.Z, vec.Y, vec.Z, 0, -vec.X, -vec.Y, vec.X, 0)-
    /// </summary>
    public static JMatrix CreateCrossProduct(in JVector vec)
    {
        return new JMatrix(0, -vec.Z, vec.Y, vec.Z, 0, -vec.X, -vec.Y, vec.X, 0);
    }

    private static void Transpose(in JMatrix matrix, out JMatrix result)
    {
        result.M11 = matrix.M11;
        result.M12 = matrix.M21;
        result.M13 = matrix.M31;
        result.M21 = matrix.M12;
        result.M22 = matrix.M22;
        result.M23 = matrix.M32;
        result.M31 = matrix.M13;
        result.M32 = matrix.M23;
        result.M33 = matrix.M33;
    }

    public static JMatrix operator *(in JMatrix matrix1, in JMatrix matrix2)
    {
        JMatrix result;
        result.M11 = matrix1.M11 * matrix2.M11 + matrix1.M12 * matrix2.M21 + matrix1.M13 * matrix2.M31;
        result.M12 = matrix1.M11 * matrix2.M12 + matrix1.M12 * matrix2.M22 + matrix1.M13 * matrix2.M32;
        result.M13 = matrix1.M11 * matrix2.M13 + matrix1.M12 * matrix2.M23 + matrix1.M13 * matrix2.M33;
        result.M21 = matrix1.M21 * matrix2.M11 + matrix1.M22 * matrix2.M21 + matrix1.M23 * matrix2.M31;
        result.M22 = matrix1.M21 * matrix2.M12 + matrix1.M22 * matrix2.M22 + matrix1.M23 * matrix2.M32;
        result.M23 = matrix1.M21 * matrix2.M13 + matrix1.M22 * matrix2.M23 + matrix1.M23 * matrix2.M33;
        result.M31 = matrix1.M31 * matrix2.M11 + matrix1.M32 * matrix2.M21 + matrix1.M33 * matrix2.M31;
        result.M32 = matrix1.M31 * matrix2.M12 + matrix1.M32 * matrix2.M22 + matrix1.M33 * matrix2.M32;
        result.M33 = matrix1.M31 * matrix2.M13 + matrix1.M32 * matrix2.M23 + matrix1.M33 * matrix2.M33;
        return result;
    }

    public float Trace()
    {
        return M11 + M22 + M33;
    }

    public static JMatrix operator *(float factor, in JMatrix matrix)
    {
        Multiply(matrix, factor, out JMatrix result);
        return result;
    }

    public static JMatrix operator *(in JMatrix matrix, float factor)
    {
        Multiply(matrix, factor, out JMatrix result);
        return result;
    }

    public static JMatrix operator +(in JMatrix value1, in JMatrix value2)
    {
        Add(value1, value2, out JMatrix result);
        return result;
    }

    public static JMatrix operator -(in JMatrix value1, in JMatrix value2)
    {
        Subtract(value1, value2, out JMatrix result);
        return result;
    }
}