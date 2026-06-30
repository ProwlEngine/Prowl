using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Shared 4x4 / TRS helpers used by both <see cref="SceneBaker"/> and the post-process steps
/// that need to compose, multiply, or decompose local transforms.
/// </summary>
internal static class SceneBakerHelpers
{
    public static Float4x4 ComposeTRS(Float3 t, Quaternion r, Float3 s)
    {
        float x = r.X, y = r.Y, z = r.Z, w = r.W;
        float xx = x * x, yy = y * y, zz = z * z;
        float xy = x * y, xz = x * z, yz = y * z;
        float wx = w * x, wy = w * y, wz = w * z;

        var c0 = new Float4(
            (1f - 2f * (yy + zz)) * s.X,
            (2f * (xy + wz)) * s.X,
            (2f * (xz - wy)) * s.X,
            0f);
        var c1 = new Float4(
            (2f * (xy - wz)) * s.Y,
            (1f - 2f * (xx + zz)) * s.Y,
            (2f * (yz + wx)) * s.Y,
            0f);
        var c2 = new Float4(
            (2f * (xz + wy)) * s.Z,
            (2f * (yz - wx)) * s.Z,
            (1f - 2f * (xx + yy)) * s.Z,
            0f);
        var c3 = new Float4(t.X, t.Y, t.Z, 1f);
        return new Float4x4(c0, c1, c2, c3);
    }

    public static Float4x4 Mul(Float4x4 a, Float4x4 b) => new(
        MulColumn(a, b.c0),
        MulColumn(a, b.c1),
        MulColumn(a, b.c2),
        MulColumn(a, b.c3));

    public static Float4 MulColumn(Float4x4 a, Float4 v) => new(
        a.c0.X * v.X + a.c1.X * v.Y + a.c2.X * v.Z + a.c3.X * v.W,
        a.c0.Y * v.X + a.c1.Y * v.Y + a.c2.Y * v.Z + a.c3.Y * v.W,
        a.c0.Z * v.X + a.c1.Z * v.Y + a.c2.Z * v.Z + a.c3.Z * v.W,
        a.c0.W * v.X + a.c1.W * v.Y + a.c2.W * v.Z + a.c3.W * v.W);

    public static void DecomposeMatrix(Float4x4 m, out Float3 translation, out Quaternion rotation, out Float3 scale)
    {
        translation = new Float3(m.c3.X, m.c3.Y, m.c3.Z);
        Float3 c0 = new Float3(m.c0.X, m.c0.Y, m.c0.Z);
        Float3 c1 = new Float3(m.c1.X, m.c1.Y, m.c1.Z);
        Float3 c2 = new Float3(m.c2.X, m.c2.Y, m.c2.Z);
        float sx = Length(c0), sy = Length(c1), sz = Length(c2);
        float det = c0.X * (c1.Y * c2.Z - c1.Z * c2.Y)
                  - c0.Y * (c1.X * c2.Z - c1.Z * c2.X)
                  + c0.Z * (c1.X * c2.Y - c1.Y * c2.X);
        if (det < 0f) sx = -sx;
        scale = new Float3(sx, sy, sz);
        Float3 r0 = Divide(c0, sx), r1 = Divide(c1, sy), r2 = Divide(c2, sz);
        rotation = QuatFromRotationColumns(r0, r1, r2);
    }

    private static float Length(Float3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    private static Float3 Divide(Float3 v, float s) => s == 0f ? Float3.Zero : new Float3(v.X / s, v.Y / s, v.Z / s);

    private static Quaternion QuatFromRotationColumns(Float3 c0, Float3 c1, Float3 c2)
    {
        float m00 = c0.X, m01 = c1.X, m02 = c2.X;
        float m10 = c0.Y, m11 = c1.Y, m12 = c2.Y;
        float m20 = c0.Z, m21 = c1.Z, m22 = c2.Z;
        float trace = m00 + m11 + m22;
        float x, y, z, w;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            w = 0.25f * s;
            x = (m21 - m12) / s;
            y = (m02 - m20) / s;
            z = (m10 - m01) / s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
            w = (m21 - m12) / s;
            x = 0.25f * s;
            y = (m01 + m10) / s;
            z = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
            w = (m02 - m20) / s;
            x = (m01 + m10) / s;
            y = 0.25f * s;
            z = (m12 + m21) / s;
        }
        else
        {
            float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
            w = (m10 - m01) / s;
            x = (m02 + m20) / s;
            y = (m12 + m21) / s;
            z = 0.25f * s;
        }
        return new Quaternion(x, y, z, w);
    }
}
