using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    public static partial class Maths
    {
        /// <summary>
        /// Creates a quaternion from a 3x3 rotation matrix.
        /// The matrix must be orthonormal (a pure rotation matrix).
        /// </summary>
        public static Quaternion FromMatrix(Float3x3 m)
        {
            float trace = m.c0.X + m.c1.Y + m.c2.Z;
            float x, y, z, w;

            if (trace > 0.0f)
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

        /// <summary>
        /// Creates a quaternion from a 4x4 rotation matrix.
        /// The matrix must be orthonormal in its upper-left 3x3 part.
        /// </summary>
        public static Quaternion FromMatrix(Float4x4 m)
        {
            // Extract the 3x3 rotation part
            Float3x3 m3x3 = new Float3x3(m.c0.XYZ, m.c1.XYZ, m.c2.XYZ);
            return FromMatrix(m3x3);
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
            if (lengthSq == 0.0f) // Should not happen with valid rotations
                return Quaternion.Identity; // Or throw
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
            return Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
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
            if (len == 0.0f) // Or len <= Epsilon
                return Quaternion.Identity; // Or throw
            float invLen = 1.0f / len;
            return new Quaternion(q.X * invLen, q.Y * invLen, q.Z * invLen, q.W * invLen);
        }

        /// <summary>Returns a safe normalized version of the quaternion. Returns identity if normalization fails.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion NormalizeSafe(Quaternion q)
        {
            float lenSq = LengthSquared(q);
            if (lenSq < EpsilonF)
                return Quaternion.Identity;
            float invLen = Rsqrt(lenSq);
            return new Quaternion(q.X * invLen, q.Y * invLen, q.Z * invLen, q.W * invLen);
        }

        /// <summary>Returns a safe normalized version of the quaternion. Returns defaultValue if normalization fails.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion NormalizeSafe(Quaternion q, Quaternion defaultValue)
        {
            float lenSq = LengthSquared(q);
            if (lenSq < EpsilonF)
                return defaultValue;
            float invLen = Rsqrt(lenSq);
            return new Quaternion(q.X * invLen, q.Y * invLen, q.Z * invLen, q.W * invLen);
        }

        /// <summary>Multiplies two quaternions (concatenates rotations).</summary>
        /// <remarks>The order is important: a * b means applying rotation b then rotation a.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Mul(Quaternion a, Quaternion b)
        {
            return new Quaternion(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z
            );
        }

        /// <summary>Rotates a 3D vector by a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Mul(Quaternion q, Float3 v)
        {
            Float3 qVec = new Float3(q.X, q.Y, q.Z);
            Float3 t = 2.0f * Cross(qVec, v);
            return v + q.W * t + Cross(qVec, t);
        }

        /// <summary>Rotates a 3D vector by a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Mul(Quaternion q, Double3 v)
        {
            Double3 qVec = new Double3(q.X, q.Y, q.Z);
            Double3 t = 2.0 * Cross(qVec, v);
            return v + q.W * t + Cross(qVec, t);
        }

        /// <summary>Alias for Mul(Quaternion, Float3) for clarity when rotating vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Rotate(Quaternion q, Float3 v) => Mul(q, v);


        /// <summary>Creates a quaternion representing a rotation around an axis by an angle.</summary>
        /// <param name="axis">The axis of rotation (must be normalized).</param>
        /// <param name="angle">The angle of rotation in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion AxisAngle(Float3 axis, float angle)
        {
            float halfAngle = angle * 0.5f;
            Sincos(halfAngle, out float s, out float c);
            return new Quaternion(axis.X * s, axis.Y * s, axis.Z * s, c);
        }

        /// <summary>Creates a quaternion for a rotation around the X axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion RotateX(float angle)
        {
            Sincos(angle * 0.5f, out float s, out float c);
            return new Quaternion(s, 0.0f, 0.0f, c);
        }

        /// <summary>Creates a quaternion for a rotation around the Y axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion RotateY(float angle)
        {
            Sincos(angle * 0.5f, out float s, out float c);
            return new Quaternion(0.0f, s, 0.0f, c);
        }

        /// <summary>Creates a quaternion for a rotation around the Z axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion RotateZ(float angle)
        {
            Sincos(angle * 0.5f, out float s, out float c);
            return new Quaternion(0.0f, 0.0f, s, c);
        }

        /// <summary>Creates a quaternion looking from a 'forward' direction with an 'up' vector.</summary>
        public static Quaternion LookRotation(Float3 forward, Float3 up)
        {
            // Ensure forward vector is normalized
            forward = Normalize(forward);

            Float3 right = Cross(up, forward);

            // Handle degenerate case when forward and up are parallel
            if (LengthSquared(right) < EpsilonF)
            {
                // use X-axis as right when looking up/down
                // For looking straight up (0,1,0) or down (0,-1,0), keep right as (1,0,0)
                right = Float3.UnitX;
            }

            right = Normalize(right);
            Float3 newUp = Cross(forward, right);

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

            if (dot < 0.0f) // Ensure shortest path
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

            if (dot < 0.0f)
            {
                dot = -dot;
                q2Adjusted = new Quaternion(-q2.X, -q2.Y, -q2.Z, -q2.W);
            }

            if (dot > 0.9995f) // If quaternions are very close, use Nlerp for stability
            {
                return Nlerp(q1, q2Adjusted, t);
            }

            float angle = Acos(dot);        // Angle between input quaternions
            float sinAngle = Sin(angle);    // Sin of angle
            if (Abs(sinAngle) < Epsilon) // Should not happen if dot <= 0.9995f
            {
                return Nlerp(q1, q2Adjusted, t); // Fallback
            }

            float invSinAngle = 1.0f / sinAngle;
            float scale0 = Sin((1.0f - t) * angle) * invSinAngle;
            float scale1 = Sin(t * angle) * invSinAngle;

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
            return Acos(Min(Abs(dot), 1.0f)) * 2.0f;
        }

        /// <summary>The "forward" vector of a rotation (0,0,1) rotated by the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Forward(Quaternion q) => Mul(q, new Float3(0, 0, 1));

        /// <summary>The "up" vector of a rotation (0,1,0) rotated by the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Up(Quaternion q) => Mul(q, new Float3(0, 1, 0));

        /// <summary>The "right" vector of a rotation (1,0,0) rotated by the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Right(Quaternion q) => Mul(q, new Float3(1, 0, 0));
    }
}
