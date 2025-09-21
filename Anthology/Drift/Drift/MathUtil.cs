using System.Numerics;

namespace Prowl.Drift
{

    //-----------------------------------
    // MathUtils
    //-----------------------------------
    public static class MathUtil
    {
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        public static float Cross(Vector2 v1, Vector2 v2) => v1.X * v2.Y - v1.Y * v2.X;
        public static Vector2 Mad(Vector2 v1, Vector2 v2, float s) => new Vector2(v1.X + v2.X * s, v1.Y + v2.Y * s);
        public static Vector2 Perp(Vector2 n) => new Vector2(-n.Y, n.X);

        public static Vector2 Rotate(Vector2 v, float angle)
        {
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        public static Vector2 Truncate(Vector2 v, float length)
        {
            float lengthSq = v.X * v.X + v.Y * v.Y;
            if (lengthSq > length * length)
            {
                var s = length / MathF.Sqrt(lengthSq);
                return new Vector2(v.X * s, v.Y * s);
            }
            return v;
        }

        public static float Clamp(float v, float min, float max) =>
            v < min ? min : (v > max ? max : v);

        public static Vector2 Solve(float m11, float m12, float m21, float m22, Vector2 b)
        {
            float det = m11 * m22 - m12 * m21;
            if (det != 0) det = 1 / det;
            return new Vector2(det * (m22 * b.X - m12 * b.Y),
                            det * (m11 * b.Y - m21 * b.X));
        }

        public static Vector3 Solve3x3(float m11, float m12, float m13,
                                       float m21, float m22, float m23,
                                       float m31, float m32, float m33, Vector3 b)
        {
            float det2_11 = m22 * m33 - m23 * m32;
            float det2_12 = m23 * m31 - m21 * m33;
            float det2_13 = m21 * m32 - m22 * m31;

            float det = m11 * det2_11 + m12 * det2_12 + m13 * det2_13;
            if (det != 0) det = 1 / det;

            float det2_21 = m13 * m32 - m12 * m33;
            float det2_22 = m11 * m33 - m13 * m31;
            float det2_23 = m12 * m31 - m11 * m32;
            float det2_31 = m12 * m23 - m13 * m22;
            float det2_32 = m13 * m21 - m11 * m23;
            float det2_33 = m11 * m22 - m12 * m21;

            return new Vector3(
                det * (det2_11 * b.X + det2_12 * b.Y + det2_13 * b.Z),
                det * (det2_21 * b.X + det2_22 * b.Y + det2_23 * b.Z),
                det * (det2_31 * b.X + det2_32 * b.Y + det2_33 * b.Z));
        }
    }
}
