// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{

    /// <summary>
    /// Represents a quaternion used for 3D rotations.
    /// Components are ordered X, Y, Z, W.
    /// </summary>
    [System.Serializable]
    public struct Quaternion : IEquatable<Quaternion>, IFormattable
    {
        /// <summary>A quaternion representing the identity transform (no rotation).</summary>
        public static readonly Quaternion Identity = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

        /// <summary>The X component of the quaternion.</summary>
        public float X;
        /// <summary>The Y component of the quaternion.</summary>
        public float Y;
        /// <summary>The Z component of the quaternion.</summary>
        public float Z;
        /// <summary>The W component of the quaternion (scalar part).</summary>
        public float W;

        #region Properties

        /// <summary>
        /// Gets or sets the rotation as Euler angles in degrees, using the ZXYr order.
        /// This is useful for editor inspectors and simple rotation control.
        /// </summary>
        public Float3 EulerAngles
        {
            get => ToEuler(this);
            set => this = FromEuler(value);
        }

        /// <summary>Gets or sets the component at the specified index (0=X, 1=Y, 2=Z, 3=W).</summary>
        public float this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                switch (index)
                {
                    case 0: return X;
                    case 1: return Y;
                    case 2: return Z;
                    case 3: return W;
                    default: throw new IndexOutOfRangeException("Quaternion index out of range.");
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                switch (index)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    case 2: Z = value; break;
                    case 3: W = value; break;
                    default: throw new IndexOutOfRangeException("Quaternion index out of range.");
                }
            }
        }

        #endregion

        #region Constructors

        /// <summary>Constructs a quaternion from four float values.</summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <param name="z">The Z component.</param>
        /// <param name="w">The W component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>Constructs a quaternion from a Float4 vector (xyzw).</summary>
        /// <param name="value">The Float4 vector containing xyzw components.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(Float4 value)
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
            W = value.W;
        }

        /// <summary>Constructs a quaternion from a vector part (Float3) and a scalar part (float).</summary>
        /// <param name="vectorPart">The vector part (xyz).</param>
        /// <param name="scalarPart">The scalar part (w).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(Float3 vectorPart, float scalarPart)
        {
            X = vectorPart.X;
            Y = vectorPart.Y;
            Z = vectorPart.Z;
            W = scalarPart;
        }

        #endregion

        #region Static Methods

        /// <summary>
        /// Creates a quaternion from a 4x4 rotation matrix.
        /// The matrix must be orthonormal in its upper-left 3x3 part.
        /// </summary>
        public static Quaternion FromMatrix(Float4x4 m) => FromMatrix(new Float3x3(m.c0.XYZ, m.c1.XYZ, m.c2.XYZ));

        /// <summary>
        /// Creates a quaternion from a 3x3 rotation matrix.
        /// The matrix must be orthonormal (a pure rotation matrix).
        /// </summary>
        public static Quaternion FromMatrix(Float3x3 m)
        {
            float trace = m.c0.X + m.c1.Y + m.c2.Z;
            float x, y, z, w;

            if (trace > 0.0)
            {
                float s = Maths.Sqrt(trace + 1.0f) * 2.0f;
                w = 0.25f * s;
                x = (m.c1.Z - m.c2.Y) / s;
                y = (m.c2.X - m.c0.Z) / s;
                z = (m.c0.Y - m.c1.X) / s;
            }
            else if ((m.c0.X > m.c1.Y) && (m.c0.X > m.c2.Z))
            {
                float s = Maths.Sqrt(1.0f + m.c0.X - m.c1.Y - m.c2.Z) * 2.0f;
                w = (m.c1.Z - m.c2.Y) / s;
                x = 0.25f * s;
                y = (m.c0.Y + m.c1.X) / s;
                z = (m.c0.Z + m.c2.X) / s;
            }
            else if (m.c1.Y > m.c2.Z)
            {
                float s = Maths.Sqrt(1.0f + m.c1.Y - m.c0.X - m.c2.Z) * 2.0f;
                w = (m.c2.X - m.c0.Z) / s;
                x = (m.c0.Y + m.c1.X) / s;
                y = 0.25f * s;
                z = (m.c2.Y + m.c1.Z) / s;
            }
            else
            {
                float s = Maths.Sqrt(1.0f + m.c2.Z - m.c0.X - m.c1.Y) * 2.0f;
                w = (m.c0.Y - m.c1.X) / s;
                x = (m.c2.X + m.c0.Z) / s;
                y = (m.c2.Y + m.c1.Z) / s;
                z = 0.25f * s;
            }
            return new Quaternion(x, y, z, w);
        }

        public static Quaternion FromEuler(Float3 euler) => FromEuler(euler.X, euler.Y, euler.Z);
        public static Quaternion FromEuler(float x, float y, float z)
        {
            float yawOver2 = Maths.Deg2Rad * x * 0.5f;
            float pitchOver2 = Maths.Deg2Rad * y * 0.5f;
            float rollOver2 = Maths.Deg2Rad * z * 0.5f;

            float cosYawOver2 = Maths.Cos(yawOver2);
            float sinYawOver2 = Maths.Sin(yawOver2);
            float cosPitchOver2 = Maths.Cos(pitchOver2);
            float sinPitchOver2 = Maths.Sin(pitchOver2);
            float cosRollOver2 = Maths.Cos(rollOver2);
            float sinRollOver2 = Maths.Sin(rollOver2);

            Quaternion result = new Quaternion();
            result.W = cosYawOver2 * cosPitchOver2 * cosRollOver2 + sinYawOver2 * sinPitchOver2 * sinRollOver2;
            result.X = sinYawOver2 * cosPitchOver2 * cosRollOver2 + cosYawOver2 * sinPitchOver2 * sinRollOver2;
            result.Y = cosYawOver2 * sinPitchOver2 * cosRollOver2 - sinYawOver2 * cosPitchOver2 * sinRollOver2;
            result.Z = cosYawOver2 * cosPitchOver2 * sinRollOver2 - sinYawOver2 * sinPitchOver2 * cosRollOver2;

            return result;
        }

        public static Float3 ToEuler(Quaternion q)
        {
            float CUTOFF = (1.0f - 2.0f * float.Epsilon) * (1.0f - 2.0f * float.Epsilon);

            Float4 qv = new Float4(q.X, q.Y, q.Z, q.W);

            Float4 d1 = qv * (qv.W * 2.0f);
            Float4 d2 = new Float4(qv.X * qv.Y * 2.0f, qv.Y * qv.Z * 2.0f, qv.Z * qv.X * 2.0f, qv.W * qv.W * 2.0f);
            Float4 d3 = new Float4(qv.X * qv.X, qv.Y * qv.Y, qv.Z * qv.Z, qv.W * qv.W);

            Float3 euler = Float3.Zero;
            float y1 = d2.Y - d1.X;

            if (y1 * y1 < CUTOFF)
            {
                float x1 = d2.X + d1.Z;
                float x2 = d3.Y + d3.W - d3.X - d3.Z;
                float z1 = d2.Z + d1.Y;
                float z2 = d3.Z + d3.W - d3.X - d3.Y;

                euler = new Float3(
                    Maths.Atan2(x1, x2),
                    -Maths.Asin(y1),
                    Maths.Atan2(z1, z2)
                );
            }
            else
            {
                y1 = Maths.Clamp(y1, -1.0f, 1.0f);

                Float4 abcd = new Float4(d2.Z, d1.Y, d2.Y, d1.X);
                float x1 = 2.0f * (abcd.X * abcd.W + abcd.Y * abcd.Z);

                Float4 x = new Float4(
                    abcd.X * abcd.X * -1.0f,
                    abcd.Y * abcd.Y * 1.0f,
                    abcd.Z * abcd.Z * -1.0f,
                    abcd.W * abcd.W * 1.0f
                );

                float x2 = (x.X + x.Y) + (x.Z + x.W);
                euler = new Float3(
                    Maths.Atan2(x1, x2),
                    -Maths.Asin(y1),
                    0.0f
                );
            }

            // Convert from radians to degrees
            euler *= (float)Maths.Rad2Deg;

            // Reorder YZX
            euler = new Float3(euler.Y, euler.Z, euler.X);

            euler.X = NormalizeAngle(euler.X);
            euler.Y = NormalizeAngle(euler.Y);
            euler.Z = NormalizeAngle(euler.Z);

            return euler;
        }

        private static float NormalizeAngle(float angle)
        {
            // Unity-style normalization
            angle %= 360;
            if (angle < 0)
                angle += 360;
            return angle;
        }

        /// <summary>Creates a quaternion representing a rotation around an axis by an angle.</summary>
        /// <param name="angle">The angle of rotation in radians.</param>
        /// <param name="axis">The axis of rotation (must be normalized).</param>
        public static Quaternion AngleAxis(float angle, Float3 axis) => AxisAngle(axis, angle);

        /// <summary>Creates a quaternion representing a rotation around an axis by an angle.</summary>
        /// <param name="axis">The axis of rotation (must be normalized).</param>
        /// <param name="angle">The angle of rotation in radians.</param>
        public static Quaternion AxisAngle(Float3 axis, float angle)
        {
            float halfAngle = angle * 0.5f;
            //Maths.Sincos(halfAngle, out float s, out float c);
            float s = Maths.Sin(halfAngle);
            float c = Maths.Cos(halfAngle);
            return new Quaternion(axis.X * s, axis.Y * s, axis.Z * s, c);
        }

        /// <summary>Returns the conjugate of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Conjugate(Quaternion q)
        {
            return new Quaternion(-q.X, -q.Y, -q.Z, q.W);
        }

        /// <summary>Returns the inverse of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Inverse(Quaternion q)
        {
            float lengthSq = LengthSquared(q);
            if (lengthSq <= float.Epsilon) // Should not happen with valid rotations
                return Identity;
            float invLengthSq = 1.0f / lengthSq;
            return new Quaternion(
                -q.X * invLengthSq,
                -q.Y * invLengthSq,
                -q.Z * invLengthSq,
                 q.W * invLengthSq
            );
        }

        /// <summary>Returns the dot product of two quaternions.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Quaternion a, Quaternion b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        }

        /// <summary>Returns the length (magnitude) of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Length(Quaternion q)
        {
            return Maths.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        }

        /// <summary>Returns the squared length (magnitude squared) of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LengthSquared(Quaternion q)
        {
            return q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
        }

        /// <summary>Returns a normalized version of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Normalize(Quaternion q)
        {
            float len = Length(q);
            if (len <= float.Epsilon)
                return Identity; // Or throw
            float invLen = 1.0f / len;
            return new Quaternion(q.X * invLen, q.Y * invLen, q.Z * invLen, q.W * invLen);
        }

        /// <summary>Returns a safe normalized version of the quaternion. Returns identity if normalization fails.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion NormalizeSafe(Quaternion q)
        {
            float lenSq = LengthSquared(q);
            if (lenSq < float.Epsilon)
                return Identity;
            float invLen = Maths.Rsqrt(lenSq);
            return new Quaternion(q.X * invLen, q.Y * invLen, q.Z * invLen, q.W * invLen);
        }

        /// <summary>Returns a safe normalized version of the quaternion. Returns defaultValue if normalization fails.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion NormalizeSafe(Quaternion q, Quaternion defaultValue)
        {
            float lenSq = LengthSquared(q);
            if (lenSq < float.Epsilon)
                return defaultValue;
            float invLen = Maths.Rsqrt(lenSq);
            return new Quaternion(q.X * invLen, q.Y * invLen, q.Z * invLen, q.W * invLen);
        }

        /// <summary>Creates a quaternion for a rotation around the X axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion RotateX(float angle)
        {
            float halfAngle = angle * 0.5f;
            //Maths.Sincos(angle * 0.5, out float s, out float c);
            float s = Maths.Sin(halfAngle);
            float c = Maths.Cos(halfAngle);
            return new Quaternion(s, 0.0f, 0.0f, c);
        }

        /// <summary>Creates a quaternion for a rotation around the Y axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion RotateY(float angle)
        {
            float halfAngle = angle * 0.5f;
            //Maths.Sincos(angle * 0.5, out float s, out float c);
            float s = Maths.Sin(halfAngle);
            float c = Maths.Cos(halfAngle);
            return new Quaternion(0.0f, s, 0.0f, c);
        }

        /// <summary>Creates a quaternion for a rotation around the Z axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion RotateZ(float angle)
        {
            float halfAngle = angle * 0.5f;
            //Maths.Sincos(angle * 0.5, out float s, out float c);
            float s = Maths.Sin(halfAngle);
            float c = Maths.Cos(halfAngle);
            return new Quaternion(0.0f, 0.0f, s, c);
        }

        /// <summary>Creates a quaternion looking from a 'forward' direction with an 'up' vector.</summary>
        public static Quaternion LookRotation(Float3 forward, Float3 up)
        {
            // Ensure forward vector is normalized
            forward = Float3.Normalize(forward);

            Float3 right = Float3.Cross(up, forward);

            // Handle degenerate case when forward and up are parallel
            if (Float3.LengthSquared(right) < float.Epsilon)
            {
                // use X-axis as right when looking up/down
                // For looking straight up (0,1,0) or down (0,-1,0), keep right as (1,0,0)
                right = Float3.UnitX;
            }

            right = Float3.Normalize(right);
            Float3 newUp = Float3.Cross(forward, right);

            // Create rotation matrix components
            Float3x3 m = new Float3x3(right, newUp, forward);
            return FromMatrix(m);
        }

        /// <summary>Normalized Lerp: Linearly interpolates and then normalizes. Faster than Slerp but not constant velocity.</summary>
        public static Quaternion Nlerp(Quaternion q1, Quaternion q2, float t)
        {
            float dot = Dot(q1, q2);
            float w1x = q1.X, w1y = q1.Y, w1z = q1.Z, w1w = q1.W;
            float w2x = q2.X, w2y = q2.Y, w2z = q2.Z, w2w = q2.W;

            if (dot < 0.0) // Ensure shortest path
            {
                w2x = -w2x; w2y = -w2y; w2z = -w2z; w2w = -w2w;
            }

            float resX = w1x + t * (w2x - w1x);
            float resY = w1y + t * (w2y - w1y);
            float resZ = w1z + t * (w2z - w1z);
            float resW = w1w + t * (w2w - w1w);

            return Normalize(new Quaternion(resX, resY, resZ, resW));
        }

        /// <summary>Spherical Linear Interpolation: Interpolates along the great arc on the unit sphere. Constant velocity.</summary>
        public static Quaternion Slerp(Quaternion q1, Quaternion q2, float t)
        {
            float dot = Dot(q1, q2);
            Quaternion q2Adjusted = q2;

            if (dot < 0.0)
            {
                dot = -dot;
                q2Adjusted = new Quaternion(-q2.X, -q2.Y, -q2.Z, -q2.W);
            }

            if (dot > 0.9995) // If quaternions are very close, use Nlerp for stability
            {
                return Nlerp(q1, q2Adjusted, t);
            }

            float angle = Maths.Acos(dot);        // Angle between input quaternions
            float sinAngle = Maths.Sin(angle);    // Sin of angle
            if (Maths.Abs(sinAngle) < float.Epsilon) // Should not happen if dot <= 0.9995f
            {
                return Nlerp(q1, q2Adjusted, t); // Fallback
            }

            float invSinAngle = 1.0f / sinAngle;
            float scale0 = Maths.Sin((1.0f - t) * angle) * invSinAngle;
            float scale1 = Maths.Sin(t * angle) * invSinAngle;

            return new Quaternion(
                (scale0 * q1.X) + (scale1 * q2Adjusted.X),
                (scale0 * q1.Y) + (scale1 * q2Adjusted.Y),
                (scale0 * q1.Z) + (scale1 * q2Adjusted.Z),
                (scale0 * q1.W) + (scale1 * q2Adjusted.W)
            );
        }

        /// <summary>Returns the angle in radians between two unit quaternions.</summary>
        public static float Angle(Quaternion q1, Quaternion q2)
        {
            // Ensure they are unit quaternions or the result might be off
            // For non-unit quaternions, normalizing them first is advisable:
            // q1 = Normalize(q1);
            // q2 = Normalize(q2);
            float dot = Dot(q1, q2);
            // Clamp dot to avoid Acos domain errors due to floating point inaccuracies
            return Maths.Acos(Maths.Min(Maths.Abs(dot), 1.0f)) * 2.0f;
        }

        /// <summary>The "forward" vector of a rotation (0,0,1) rotated by the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Forward(Quaternion q) => q * new Float3(0, 0, 1);

        /// <summary>The "up" vector of a rotation (0,1,0) rotated by the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Up(Quaternion q) => q * new Float3(0, 1, 0);

        /// <summary>The "right" vector of a rotation (1,0,0) rotated by the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Right(Quaternion q) => q * new Float3(1, 0, 0);

        #endregion

        /// <summary>Implicitly converts a Float4 vector to a Quaternion.</summary>
        public static implicit operator Quaternion(Float4 v) => new Quaternion(v);

        /// <summary>Explicitly converts a Quaternion to a Float4 vector.</summary>
        public static explicit operator Float4(Quaternion q) => new Float4(q.X, q.Y, q.Z, q.W);

        public static Quaternion operator -(Quaternion value) => new Quaternion(-value.X, -value.Y, -value.Z, -value.W);

        public static Quaternion operator +(Quaternion lh, float scalar) => new Quaternion(lh.X + scalar, lh.Y + scalar, lh.Z + scalar, lh.W + scalar);
        public static Quaternion operator -(Quaternion lh, float scalar) => new Quaternion(lh.X - scalar, lh.Y - scalar, lh.Z - scalar, lh.W - scalar);
        public static Quaternion operator /(Quaternion lhs, float scalar) => new Quaternion(lhs.X / scalar, lhs.Y / scalar, lhs.Z / scalar, lhs.W / scalar);
        public static Quaternion operator *(Quaternion lhs, float scalar) => new Quaternion(lhs.X * scalar, lhs.Y * scalar, lhs.Z * scalar, lhs.W * scalar);

        public static Quaternion operator +(Quaternion lh, Quaternion rh) => new Quaternion(lh.X + rh.X, lh.Y + rh.Y, lh.Z + rh.Z, lh.W + rh.W);
        public static Quaternion operator -(Quaternion lh, Quaternion rh) => new Quaternion(lh.X - rh.X, lh.Y - rh.Y, lh.Z - rh.Z, lh.W - rh.W);
        public static Quaternion operator /(Quaternion lh, Quaternion rh) => new Quaternion(lh.X / rh.X, lh.Y / rh.Y, lh.Z / rh.Z, lh.W / rh.W);

        /// <summary> Multiplies two quaternions, combining their rotations. </summary>
        /// <remarks>The order is important: a * b means applying rotation b then rotation a.</remarks>
        public static Quaternion operator *(Quaternion lh, Quaternion rh) => new Quaternion(
                lh.W * rh.X + lh.X * rh.W + lh.Y * rh.Z - lh.Z * rh.Y,
                lh.W * rh.Y - lh.X * rh.Z + lh.Y * rh.W + lh.Z * rh.X,
                lh.W * rh.Z + lh.X * rh.Y - lh.Y * rh.X + lh.Z * rh.W,
                lh.W * rh.W - lh.X * rh.X - lh.Y * rh.Y - lh.Z * rh.Z
            );

        /// <summary>Rotates a 3D vector by a quaternion.</summary>
        public static Float3 operator *(Quaternion lh, Float3 vector)
        {
            Float3 qVec = new Float3(lh.X, lh.Y, lh.Z);
            Float3 t = 2.0f * Float3.Cross(qVec, vector);
            return vector + lh.W * t + Float3.Cross(qVec, t);
        }

        #region Equals and GetHashCode

        public static bool operator ==(Quaternion lhs, Quaternion rhs)
        {
            if (lhs.X == rhs.X && lhs.Y == rhs.Y && lhs.Z == rhs.Z && lhs.W == rhs.W)
                return true;
            else
                return false;
        }

        public static bool operator !=(Quaternion lhs, Quaternion rhs)
        {
            return !(lhs == rhs);
        }

        /// <summary>Checks if this quaternion is equal to another quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Quaternion other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;

        /// <summary>Checks if this quaternion is equal to an object.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);

        /// <summary>Gets the hash code for this quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

        #endregion

        /// <summary>Returns a string representation of the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

        /// <summary>Returns a string representation of the quaternion using the specified format.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format) => ToString(format, CultureInfo.CurrentCulture);

        /// <summary>Returns a string representation of the quaternion using the specified format and format provider.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            return string.Format(formatProvider, "Quaternion({0}, {1}, {2}, {3})",
                X.ToString(format, formatProvider),
                Y.ToString(format, formatProvider),
                Z.ToString(format, formatProvider),
                W.ToString(format, formatProvider));
        }
    }
}
