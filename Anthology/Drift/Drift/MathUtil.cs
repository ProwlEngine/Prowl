using System;
using System.Collections.Generic;

namespace Prowl.Drift
{

    public struct Plane(Vec2 normal, float distance)
    {
        public Vec2 Normal = normal;
        public float Distance = distance;
    }

    public struct Vec2(float x, float y)
    {
        public float X = x;
        public float Y = y;

        public static readonly Vec2 Zero = new(0, 0);

        // ===== Instance methods =====

        public Vec2 Set(float x, float y) { X = x; Y = y; return this; }

        public Vec2 Copy(Vec2 v) { X = v.X; Y = v.Y; return this; }

        public Vec2 Duplicate() => new Vec2(X, Y);

        public Vec2 Scale(float s) { X *= s; Y *= s; return this; }

        public Vec2 Mad(Vec2 v, float s) { X += v.X * s; Y += v.Y * s; return this; }

        public Vec2 Neg() { X = -X; Y = -Y; return this; }

        public float LengthSquared() => X * X + Y * Y;

        public float Length() => MathF.Sqrt(X * X + Y * Y);

        public float Dot(Vec2 v) => X * v.X + Y * v.Y;

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
            var ret = v.Duplicate();
            float lengthSq = v.X * v.X + v.Y * v.Y;
            if (lengthSq > length * length)
            {
                ret.Scale(length / MathF.Sqrt(lengthSq));
            }
            return ret;
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
    // Vec3
    //-----------------------------------
    public struct Vec3
    {
        public float X, Y, Z;

        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static readonly Vec3 Zero = new(0, 0, 0);

        public void Set(float x, float y, float z) { X = x; Y = y; Z = z; }

        public void AddSelf(Vec3 v) { X += v.X; Y += v.Y; Z += v.Z; }

        public Vec3 Neg() => new(-X, -Y, -Z);

        public Vec2 ToVec2() => new(X, Y);

        // Operators
        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static bool operator ==(Vec3 a, Vec3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        public static bool operator !=(Vec3 a, Vec3 b) => !(a == b);

        public override bool Equals(object obj) => obj is Vec3 v && this == v;
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    }

    //-----------------------------------
    // Mat2
    //-----------------------------------
    public struct Mat2
    {
        public float M11, M12, M21, M22;

        public Mat2(float m11, float m12, float m21, float m22)
        { M11 = m11; M12 = m12; M21 = m21; M22 = m22; }

        public Vec2 Solve(Vec2 b)
        {
            float det = M11 * M22 - M12 * M21;
            if (det != 0) det = 1 / det;
            return new Vec2(det * (M22 * b.X - M12 * b.Y),
                            det * (M11 * b.Y - M21 * b.X));
        }
    }

    //-----------------------------------
    // Mat3
    //-----------------------------------
    public struct Mat3
    {
        public float M11, M12, M13;
        public float M21, M22, M23;
        public float M31, M32, M33;

        public Mat3(float m11, float m12, float m13,
                    float m21, float m22, float m23,
                    float m31, float m32, float m33)
        {
            M11 = m11; M12 = m12; M13 = m13;
            M21 = m21; M22 = m22; M23 = m23;
            M31 = m31; M32 = m32; M33 = m33;
        }

        public Vec2 Solve2x2(Vec2 b)
        {
            float det = M11 * M22 - M12 * M21;
            if (det != 0) det = 1 / det;
            return new Vec2(det * (M22 * b.X - M12 * b.Y),
                            det * (M11 * b.Y - M21 * b.X));
        }

        public Vec3 Solve(Vec3 b)
        {
            float det2_11 = M22 * M33 - M23 * M32;
            float det2_12 = M23 * M31 - M21 * M33;
            float det2_13 = M21 * M32 - M22 * M31;

            float det = M11 * det2_11 + M12 * det2_12 + M13 * det2_13;
            if (det != 0) det = 1 / det;

            float det2_21 = M13 * M32 - M12 * M33;
            float det2_22 = M11 * M33 - M13 * M31;
            float det2_23 = M12 * M31 - M11 * M32;
            float det2_31 = M12 * M23 - M13 * M22;
            float det2_32 = M13 * M21 - M11 * M23;
            float det2_33 = M11 * M22 - M12 * M21;

            return new Vec3(
                det * (det2_11 * b.X + det2_12 * b.Y + det2_13 * b.Z),
                det * (det2_21 * b.X + det2_22 * b.Y + det2_23 * b.Z),
                det * (det2_31 * b.X + det2_32 * b.Y + det2_33 * b.Z));
        }
    }

    //-----------------------------------
    // Transform
    //-----------------------------------
    public struct Transform
    {
        public Vec2 T;   // position
        public float C;  // cos(angle)
        public float S;  // sin(angle)

        public Transform(Vec2 pos, float angle)
        {
            T = pos.Duplicate();
            C = MathF.Cos(angle);
            S = MathF.Sin(angle);
        }

        public Transform Set(Vec2 pos, float angle)
        {
            T.Copy(pos);
            C = MathF.Cos(angle);
            S = MathF.Sin(angle);
            return this;
        }

        public Transform SetRotation(float angle)
        {
            C = MathF.Cos(angle);
            S = MathF.Sin(angle);
            return this;
        }

        public Transform SetPosition(Vec2 p)
        {
            T.Copy(p);
            return this;
        }

        public Vec2 Rotate(Vec2 v)
        {
            return new Vec2(v.X * C - v.Y * S, v.X * S + v.Y * C);
        }

        public Vec2 Unrotate(Vec2 v)
        {
            return new Vec2(v.X * C + v.Y * S, -v.X * S + v.Y * C);
        }

        public Vec2 TransformPoint(Vec2 v)
        {
            return new Vec2(v.X * C - v.Y * S + T.X, v.X * S + v.Y * C + T.Y);
        }

        public Vec2 UntransformPoint(Vec2 v)
        {
            float px = v.X - T.X;
            float py = v.Y - T.Y;
            return new Vec2(px * C + py * S, -px * S + py * C);
        }
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
    }
}
