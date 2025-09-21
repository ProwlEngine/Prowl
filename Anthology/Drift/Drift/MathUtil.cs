using System;
using System.Collections.Generic;
using System.Numerics;

namespace Prowl.Drift
{
    public struct Vec2(float x, float y)
    {
        public float X = x;
        public float Y = y;

        public static readonly Vec2 Zero = new(0, 0);

        // ===== Instance methods =====

        public Vec2 Set(float x, float y) { X = x; Y = y; return this; }

        public Vec2 Mad(Vec2 v, float s) { X += v.X * s; Y += v.Y * s; return this; }

        public float LengthSquared() => X * X + Y * Y;

        public float Length() => MathF.Sqrt(X * X + Y * Y);

        // ===== Static methods =====

        public static Vec2 Mad(Vec2 v1, Vec2 v2, float s) => new Vec2(v1.X + v2.X * s, v1.Y + v2.Y * s);

        public static Vec2 Neg(Vec2 v) => new Vec2(-v.X, -v.Y);

        public static Vec2 Normalize(Vec2 v)
        {
            float inv = (v.X != 0 || v.Y != 0) ? 1f / MathF.Sqrt(v.X * v.X + v.Y * v.Y) : 0f;
            return new Vec2(v.X * inv, v.Y * inv);
        }

        public static float Dot(Vec2 v1, Vec2 v2) => v1.X * v2.X + v1.Y * v2.Y;

        public static float Cross(Vec2 v1, Vec2 v2) => v1.X * v2.Y - v1.Y * v2.X;

        public static Vec2 Rotate(Vec2 v, float angle)
        {
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            return new Vec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        public static Vec2 Perp(Vec2 v) => new Vec2(-v.Y, v.X);

        public static float Distance(Vec2 v1, Vec2 v2)
        {
            float dx = v2.X - v1.X, dy = v2.Y - v1.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        public static Vec2 Truncate(Vec2 v, float length)
        {
            float lengthSq = v.X * v.X + v.Y * v.Y;
            if (lengthSq > length * length)
            {
                var s = length / MathF.Sqrt(lengthSq);
                return new Vec2(v.X * s, v.Y * s);
            }
            return v;
        }

        // ===== Operators =====

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);

        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public override bool Equals(object obj) => obj is Vec2 v && this == v;
        public override int GetHashCode() => HashCode.Combine(X, Y);
    }

    //-----------------------------------
    // Bounds
    //-----------------------------------
    public struct Bounds
    {
        public Vec2 Mins, Maxs;

        public Bounds(Vec2 min, Vec2 max) { Mins = min; Maxs = max; }

        public void Clear() 
        { 
            Mins = new Vec2(float.MaxValue, float.MaxValue);
            Maxs = new Vec2(float.MinValue, float.MinValue); 
        }

        public void AddPoint(Vec2 p)
        {
            Mins = new Vec2(MathF.Min(Mins.X, p.X), MathF.Min(Mins.Y, p.Y));
            Maxs = new Vec2(MathF.Max(Maxs.X, p.X), MathF.Max(Maxs.Y, p.Y));
        }

        public void AddBounds(Bounds b)
        {
            Mins = new Vec2(MathF.Min(Mins.X, b.Mins.X), MathF.Min(Mins.Y, b.Mins.Y));
            Maxs = new Vec2(MathF.Max(Maxs.X, b.Maxs.X), MathF.Max(Maxs.Y, b.Maxs.Y));
        }

        public bool Intersects(Bounds b) =>
            !(Maxs.X < b.Mins.X || Mins.X > b.Maxs.X || Maxs.Y < b.Mins.Y || Mins.Y > b.Maxs.Y);

        public bool ContainsPoint(Vec2 p) =>
            p.X >= Mins.X && p.X <= Maxs.X && p.Y >= Mins.Y && p.Y <= Maxs.Y;

        public bool IsValid() => Mins.X <= Maxs.X && Mins.Y <= Maxs.Y;
    }

    //-----------------------------------
    // MathUtils
    //-----------------------------------
    public static class MathUtil
    {
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        public static float Clamp(float v, float min, float max) =>
            v < min ? min : (v > max ? max : v);

        public static Vec2 Solve(float m11, float m12, float m21, float m22, Vec2 b)
        {
            float det = m11 * m22 - m12 * m21;
            if (det != 0) det = 1 / det;
            return new Vec2(det * (m22 * b.X - m12 * b.Y),
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
