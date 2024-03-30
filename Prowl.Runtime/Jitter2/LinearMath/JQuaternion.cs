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

namespace Jitter2.LinearMath;

/// <summary>
/// Quaternion Q = W + Xi + Yj + Zk. Uses Hamilton's definition of ij=k.
/// </summary>
public struct JQuaternion
{
    public float W;
    public float X;
    public float Y;
    public float Z;

    public static JQuaternion Identity => new(0, 0, 0, 1);

    public JQuaternion(float x, float y, float z, float w)
    {
        W = w;
        X = x;
        Y = y;
        Z = z;
    }

    public JQuaternion(float w, in JVector v)
    {
        W = w;
        X = v.X;
        Y = v.Y;
        Z = v.Z;
    }

    public static JQuaternion Add(JQuaternion quaternion1, JQuaternion quaternion2)
    {
        Add(in quaternion1, in quaternion2, out JQuaternion result);
        return result;
    }

    public static void Add(in JQuaternion quaternion1, in JQuaternion quaternion2, out JQuaternion result)
    {
        result.X = quaternion1.X + quaternion2.X;
        result.Y = quaternion1.Y + quaternion2.Y;
        result.Z = quaternion1.Z + quaternion2.Z;
        result.W = quaternion1.W + quaternion2.W;
    }

    public static JQuaternion Conjugate(in JQuaternion value)
    {
        JQuaternion quaternion;
        quaternion.X = -value.X;
        quaternion.Y = -value.Y;
        quaternion.Z = -value.Z;
        quaternion.W = value.W;
        return quaternion;
    }

    public readonly JQuaternion Conj()
    {
        JQuaternion quaternion;
        quaternion.X = -X;
        quaternion.Y = -Y;
        quaternion.Z = -Z;
        quaternion.W = W;
        return quaternion;
    }

    public readonly override string ToString()
    {
        return $"{W:F6} {X:F6} {Y:F6} {Z:F6}";
    }

    public static JQuaternion Subtract(in JQuaternion quaternion1, in JQuaternion quaternion2)
    {
        Subtract(quaternion1, quaternion2, out JQuaternion result);
        return result;
    }

    public static void Subtract(in JQuaternion quaternion1, in JQuaternion quaternion2, out JQuaternion result)
    {
        result.X = quaternion1.X - quaternion2.X;
        result.Y = quaternion1.Y - quaternion2.Y;
        result.Z = quaternion1.Z - quaternion2.Z;
        result.W = quaternion1.W - quaternion2.W;
    }

    public static JQuaternion Multiply(in JQuaternion quaternion1, in JQuaternion quaternion2)
    {
        Multiply(quaternion1, quaternion2, out JQuaternion result);
        return result;
    }

    public static void Multiply(in JQuaternion quaternion1, in JQuaternion quaternion2, out JQuaternion result)
    {
        float r1 = quaternion1.W;
        float i1 = quaternion1.X;
        float j1 = quaternion1.Y;
        float k1 = quaternion1.Z;

        float r2 = quaternion2.W;
        float i2 = quaternion2.X;
        float j2 = quaternion2.Y;
        float k2 = quaternion2.Z;

        result.W = r1 * r2 - (i1 * i2 + j1 * j2 + k1 * k2);
        result.X = r1 * i2 + r2 * i1 + j1 * k2 - k1 * j2;
        result.Y = r1 * j2 + r2 * j1 + k1 * i2 - i1 * k2;
        result.Z = r1 * k2 + r2 * k1 + i1 * j2 - j1 * i2;
    }

    public static JQuaternion Multiply(in JQuaternion quaternion1, float scaleFactor)
    {
        Multiply(in quaternion1, scaleFactor, out JQuaternion result);
        return result;
    }

    public static void Multiply(in JQuaternion quaternion1, float scaleFactor, out JQuaternion result)
    {
        result.W = quaternion1.W * scaleFactor;
        result.X = quaternion1.X * scaleFactor;
        result.Y = quaternion1.Y * scaleFactor;
        result.Z = quaternion1.Z * scaleFactor;
    }

    public readonly float Length()
    {
        return (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
    }

    public void Normalize()
    {
        float num2 = X * X + Y * Y + Z * Z + W * W;
        float num = 1f / (float)Math.Sqrt(num2);
        X *= num;
        Y *= num;
        Z *= num;
        W *= num;
    }

    public static JQuaternion CreateFromMatrix(in JMatrix matrix)
    {
        CreateFromMatrix(matrix, out JQuaternion result);
        return result;
    }

    public static void CreateFromMatrix(in JMatrix matrix, out JQuaternion result)
    {
        float t;

        if (matrix.M33 < 0)
        {
            if (matrix.M11 > matrix.M22)
            {
                t = 1.0f + matrix.M11 - matrix.M22 - matrix.M33;
                result = new JQuaternion(t, matrix.M21 + matrix.M12, matrix.M31 + matrix.M13, matrix.M32 - matrix.M23);
            }
            else
            {
                t = 1.0f - matrix.M11 + matrix.M22 - matrix.M33;
                result = new JQuaternion(matrix.M21 + matrix.M12, t, matrix.M32 + matrix.M23, matrix.M13 - matrix.M31);
            }
        }
        else
        {
            if (matrix.M11 < -matrix.M22)
            {
                t = 1.0f - matrix.M11 - matrix.M22 + matrix.M33;
                result = new JQuaternion(matrix.M13 + matrix.M31, matrix.M32 + matrix.M23, t, matrix.M21 - matrix.M12);
            }
            else
            {
                t = 1.0f + matrix.M11 + matrix.M22 + matrix.M33;
                result = new JQuaternion(matrix.M32 - matrix.M23, matrix.M13 - matrix.M31, matrix.M21 - matrix.M12, t);
            }
        }

        t = (float)(0.5d / Math.Sqrt(t));
        result.X *= t;
        result.Y *= t;
        result.Z *= t;
        result.W *= t;
    }

    public static JQuaternion operator *(in JQuaternion value1, in JQuaternion value2)
    {
        Multiply(value1, value2, out JQuaternion result);
        return result;
    }

    public static JQuaternion operator *(float value1, in JQuaternion value2)
    {
        Multiply(value2, value1, out JQuaternion result);
        return result;
    }

    public static JQuaternion operator *(in JQuaternion value1, float value2)
    {
        Multiply(value1, value2, out JQuaternion result);
        return result;
    }

    public static JQuaternion operator +(in JQuaternion value1, in JQuaternion value2)
    {
        Add(value1, value2, out JQuaternion result);
        return result;
    }

    public static JQuaternion operator -(in JQuaternion value1, in JQuaternion value2)
    {
        Subtract(value1, value2, out JQuaternion result);
        return result;
    }
}