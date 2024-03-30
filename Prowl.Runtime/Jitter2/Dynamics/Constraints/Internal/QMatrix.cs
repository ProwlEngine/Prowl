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

using System.Runtime.CompilerServices;
using Jitter2.LinearMath;
using Jitter2.UnmanagedMemory;

[assembly: InternalsVisibleTo("JitterTests")]

namespace Jitter2.Dynamics.Constraints;

internal unsafe struct QMatrix
{
    private MemoryHelper.MemBlock64 mem;

    public float* Pointer => (float*)Unsafe.AsPointer(ref mem);

    private static QMatrix Multiply(float* left, float* right)
    {
        Unsafe.SkipInit(out QMatrix res);
        float* result = res.Pointer;

        for (int c = 0; c < 4; c++)
        {
            for (int r = 0; r < 4; r++)
            {
                float* tt = &result[4 * c + r];
                *tt = 0;
                for (int k = 0; k < 4; k++)
                {
                    *tt += left[4 * k + r] * right[4 * c + k];
                }
            }
        }

        return res;
    }

    public static JMatrix ProjectMultiplyLeftRight(in JQuaternion left, in JQuaternion right)
    {
        Unsafe.SkipInit(out JMatrix res);

        res.M11 = -left.X * right.X + left.W * right.W + left.Z * right.Z + left.Y * right.Y;
        res.M12 = -left.X * right.Y + left.W * right.Z - left.Z * right.W - left.Y * right.X;
        res.M13 = -left.X * right.Z - left.W * right.Y - left.Z * right.X + left.Y * right.W;
        res.M21 = -left.Y * right.X + left.Z * right.W - left.W * right.Z - left.X * right.Y;
        res.M22 = -left.Y * right.Y + left.Z * right.Z + left.W * right.W + left.X * right.X;
        res.M23 = -left.Y * right.Z - left.Z * right.Y + left.W * right.X - left.X * right.W;
        res.M31 = -left.Z * right.X - left.Y * right.W - left.X * right.Z + left.W * right.Y;
        res.M32 = -left.Z * right.Y - left.Y * right.Z + left.X * right.W - left.W * right.X;
        res.M33 = -left.Z * right.Z + left.Y * right.Y + left.X * right.X + left.W * right.W;

        return res;
    }

    public JMatrix Projection()
    {
        float* m = Pointer;

        return new JMatrix(m[0x5], m[0x9], m[0xD],
            m[0x6], m[0xA], m[0xE],
            m[0x7], m[0xB], m[0xF]);
    }

    public static QMatrix CreateLM(in JQuaternion quat)
    {
        Unsafe.SkipInit(out QMatrix result);
        float* q = result.Pointer;

        q[0x0] = +quat.W;
        q[0x4] = -quat.X;
        q[0x8] = -quat.Y;
        q[0xC] = -quat.Z;
        q[0x1] = +quat.X;
        q[0x5] = +quat.W;
        q[0x9] = -quat.Z;
        q[0xD] = +quat.Y;
        q[0x2] = +quat.Y;
        q[0x6] = +quat.Z;
        q[0xA] = +quat.W;
        q[0xE] = -quat.X;
        q[0x3] = +quat.Z;
        q[0x7] = -quat.Y;
        q[0xB] = +quat.X;
        q[0xF] = +quat.W;

        return result;
    }

    public static QMatrix CreateRM(in JQuaternion quat)
    {
        Unsafe.SkipInit(out QMatrix result);
        float* q = result.Pointer;

        q[0x0] = +quat.W;
        q[0x4] = -quat.X;
        q[0x8] = -quat.Y;
        q[0xC] = -quat.Z;
        q[0x1] = +quat.X;
        q[0x5] = +quat.W;
        q[0x9] = +quat.Z;
        q[0xD] = -quat.Y;
        q[0x2] = +quat.Y;
        q[0x6] = -quat.Z;
        q[0xA] = +quat.W;
        q[0xE] = +quat.X;
        q[0x3] = +quat.Z;
        q[0x7] = +quat.Y;
        q[0xB] = -quat.X;
        q[0xF] = +quat.W;

        return result;
    }

    public static QMatrix Multiply(ref QMatrix left, ref QMatrix right)
    {
        fixed (QMatrix* lptr = &left)
        {
            fixed (QMatrix* rptr = &right)
            {
                return Multiply((float*)lptr, (float*)rptr);
            }
        }
    }
}