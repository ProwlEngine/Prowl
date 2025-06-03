using System;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    public partial struct Float2x2
    {
        /// <summary>
        /// Creates a 2x2 matrix representing a counter-clockwise rotation by an angle in radians.
        /// </summary>
        /// <param name="angle">Rotation angle in radians.</param>
        /// <returns>The 2x2 rotation matrix.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2x2 Rotate(float angle)
        {
            Maths.Sincos(angle, out float s, out float c);
            return new Float2x2(c, -s,   // Row 0
                                s, c);  // Row 1
        }

        /// <summary>Returns a 2x2 matrix representing a uniform scaling of both axes by s.</summary>
        /// <param name="s">The scaling factor.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2x2 Scale(float s)
        {
            return new Float2x2(s, 0.0f,
                                0.0f, s);
        }

        /// <summary>Returns a 2x2 matrix representing a non-uniform axis scaling by x and y.</summary>
        /// <param name="x">The x-axis scaling factor.</param>
        /// <param name="y">The y-axis scaling factor.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2x2 Scale(float x, float y)
        {
            return new Float2x2(x, 0.0f,
                                0.0f, y);
        }

        /// <summary>Returns a 2x2 matrix representing a non-uniform axis scaling by the components of the Float2 vector v.</summary>
        /// <param name="v">The Float2 containing the x and y axis scaling factors.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2x2 Scale(Float2 v)
        {
            return Scale(v.X, v.Y);
        }
    }

    public partial struct Float3x3
    {
        /// <summary>
        /// Constructs a Float3x3 from the upper left 3x3 of a Float4x4.
        /// </summary>
        /// <param name="f4x4">Float4x4 to extract a Float3x3 from.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3x3(Float4x4 f4x4)
        {
            c0 = f4x4.c0.XYZ;
            c1 = f4x4.c1.XYZ;
            c2 = f4x4.c2.XYZ;
        }

        /// <summary>Constructs a Float3x3 matrix from a unit quaternion.</summary>
        /// <param name="q">The quaternion rotation.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3x3(Quaternion q)
        {
            // Standard formula
            float xx = q.X * q.X; float yy = q.Y * q.Y; float zz = q.Z * q.Z;
            float xy = q.X * q.Y; float xz = q.X * q.Z; float yz = q.Y * q.Z;
            float wx = q.W * q.X; float wy = q.W * q.Y; float wz = q.W * q.Z;

            float m00 = 1.0f - 2.0f * (yy + zz);
            float m01 = 2.0f * (xy - wz);
            float m02 = 2.0f * (xz + wy);

            float m10 = 2.0f * (xy + wz);
            float m11 = 1.0f - 2.0f * (xx + zz);
            float m12 = 2.0f * (yz - wx);

            float m20 = 2.0f * (xz - wy);
            float m21 = 2.0f * (yz + wx);
            float m22 = 1.0f - 2.0f * (xx + yy);

            this = new Float3x3(m00, m01, m02, m10, m11, m12, m20, m21, m22);
        }

        /// <summary>
        /// Returns a Float3x3 matrix representing a rotation around a unit axis by an angle in radians.
        /// </summary>
        /// <param name="axis">The rotation axis (must be normalized).</param>
        /// <param name="angle">The angle of rotation in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3x3 FromAxisAngle(Float3 axis, float angle)
        {
            Maths.Sincos(angle, out float s, out float c);
            float t = 1.0f - c;

            float x = axis.X;
            float y = axis.Y;
            float z = axis.Z;

            // Assumes axis is normalized
            float m00 = t * x * x + c;
            float m01 = t * x * y - s * z;
            float m02 = t * x * z + s * y;

            float m10 = t * x * y + s * z;
            float m11 = t * y * y + c;
            float m12 = t * y * z - s * x;

            float m20 = t * x * z - s * y;
            float m21 = t * y * z + s * x;
            float m22 = t * z * z + c;

            return new Float3x3(m00, m01, m02, m10, m11, m12, m20, m21, m22);
        }

        /// <summary>Returns a Float3x3 matrix that rotates around the X-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3x3 RotateX(float angle)
        {
            Maths.Sincos(angle, out float s, out float c);
            return new Float3x3(
                1.0f, 0.0f, 0.0f,
                0.0f, c, -s,
                0.0f, s, c
            );
        }

        /// <summary>Returns a Float3x3 matrix that rotates around the Y-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3x3 RotateY(float angle)
        {
            Maths.Sincos(angle, out float s, out float c);
            return new Float3x3(
                c, 0.0f, s,
                0.0f, 1.0f, 0.0f,
               -s, 0.0f, c
            );
        }

        /// <summary>Returns a Float3x3 matrix that rotates around the Z-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3x3 RotateZ(float angle)
        {
            Maths.Sincos(angle, out float s, out float c);
            return new Float3x3(
                c, -s, 0.0f,
                s, c, 0.0f,
                0.0f, 0.0f, 1.0f
            );
        }

        /// <summary>Returns a Float3x3 matrix representing a uniform scaling.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3x3 Scale(float s)
        {
            return new Float3x3(
                s, 0.0f, 0.0f,
                0.0f, s, 0.0f,
                0.0f, 0.0f, s
            );
        }

        /// <summary>Returns a Float3x3 matrix representing a non-uniform axis scaling.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3x3 Scale(float x, float y, float z)
        {
            return new Float3x3(
                x, 0.0f, 0.0f,
                0.0f, y, 0.0f,
                0.0f, 0.0f, z
            );
        }

        /// <summary>Returns a Float3x3 matrix representing a non-uniform axis scaling.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3x3 Scale(Float3 v)
        {
            return Scale(v.X, v.Y, v.Z);
        }

        /// <summary>Creates a 3x3 view rotation matrix. Assumes forward and up are normalized and not collinear.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3x3 CreateLookRotation(Float3 forward, Float3 up)
        {
            Float3 zaxis = Maths.Normalize(forward);
            Float3 xaxis = Maths.Normalize(Maths.Cross(up, zaxis));
            Float3 yaxis = Maths.Cross(zaxis, xaxis); // Already normalized

            return new Float3x3(
                xaxis.X, yaxis.X, zaxis.X,
                xaxis.Y, yaxis.Y, zaxis.Y,
                xaxis.Z, yaxis.Z, zaxis.Z
            );
        }

        /// <summary>Creates a 3x3 view rotation matrix with safety checks.</summary>
        public static Float3x3 CreateLookRotationSafe(Float3 forward, Float3 up)
        {
            if (Maths.LengthSquared(forward) < Maths.Epsilon * Maths.Epsilon || Maths.LengthSquared(up) < Maths.Epsilon * Maths.Epsilon)
                return Float3x3.Identity;

            Float3 zaxis = Maths.Normalize(forward);
            Float3 xaxis = Maths.Cross(up, zaxis);

            if (Maths.LengthSquared(xaxis) < Maths.Epsilon * Maths.Epsilon) // Collinear (degenerate)
            {
                // If forward is (0,1,0) or (0,-1,0), an alternative right vector is needed.
                // This case needs careful handling, e.g., if forward aligns with initial up.
                // A common fallback: if z-axis is (0,1,0), use (1,0,0) as right. If (0,-1,0), use (-1,0,0).
                // For simplicity here, let's try to pick a non-collinear "right" based on z-axis.
                Float3 alternativeRight = (Maths.Abs(zaxis.X) > 0.9f) ? Float3.UnitY : Float3.UnitX;
                xaxis = Maths.Normalize(Maths.Cross(alternativeRight, zaxis));
                // If still an issue (forward aligns perfectly with alternativeRight), then it's very degenerate.
                // For now, this improves robustness slightly.
                if (Maths.LengthSquared(xaxis) < Maths.Epsilon * Maths.Epsilon) return Float3x3.Identity;
            }
            else
            {
                xaxis = Maths.Normalize(xaxis);
            }

            Float3 yaxis = Maths.Cross(zaxis, xaxis); // Already normalized if xaxis and zaxis are orthonormal

            return new Float3x3(
                xaxis.X, yaxis.X, zaxis.X,
                xaxis.Y, yaxis.Y, zaxis.Y,
                xaxis.Z, yaxis.Z, zaxis.Z
            );
        }
    }

    public partial struct Float4x4
    {
        /// <summary>Constructs a Float4x4 from a Float3x3 rotation matrix and a Float3 translation vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float4x4(Float3x3 rotation, Float3 translation)
        {
            c0 = new Float4(rotation.c0, 0.0f);
            c1 = new Float4(rotation.c1, 0.0f);
            c2 = new Float4(rotation.c2, 0.0f);
            c3 = new Float4(translation, 1.0f);
        }

        /// <summary>Constructs a Float4x4 from a Quaternion rotation and a Float3 translation vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float4x4(Quaternion rotation, Float3 translation)
        {
            Float3x3 rotMatrix = new Float3x3(rotation);
            c0 = new Float4(rotMatrix.c0, 0.0f);
            c1 = new Float4(rotMatrix.c1, 0.0f);
            c2 = new Float4(rotMatrix.c2, 0.0f);
            c3 = new Float4(translation, 1.0f);
        }

        /// <summary>Returns a Float4x4 matrix representing a rotation around an axis by an angle.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4x4 FromAxisAngle(Float3 axis, float angle)
        {
            Float3x3 rot3x3 = Float3x3.FromAxisAngle(axis, angle);
            return new Float4x4(rot3x3, Float3.Zero);
        }

        /// <summary>Returns a Float4x4 matrix that rotates around the X-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4x4 RotateX(float angle)
        {
            Float3x3 rot3x3 = Float3x3.RotateX(angle);
            return new Float4x4(rot3x3, Float3.Zero);
        }

        /// <summary>Returns a Float4x4 matrix that rotates around the Y-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4x4 RotateY(float angle)
        {
            Float3x3 rot3x3 = Float3x3.RotateY(angle);
            return new Float4x4(rot3x3, Float3.Zero);
        }

        /// <summary>Returns a Float4x4 matrix that rotates around the Z-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4x4 RotateZ(float angle)
        {
            Float3x3 rot3x3 = Float3x3.RotateZ(angle);
            return new Float4x4(rot3x3, Float3.Zero);
        }

        /// <summary>Returns a Float4x4 uniform scale matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4x4 CreateScale(float s)
        {
            return new Float4x4(
                s, 0.0f, 0.0f, 0.0f,
                0.0f, s, 0.0f, 0.0f,
                0.0f, 0.0f, s, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f
            );
        }

        /// <summary>Returns a Float4x4 non-uniform scale matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4x4 CreateScale(float x, float y, float z)
        {
            return new Float4x4(
                x, 0.0f, 0.0f, 0.0f,
                0.0f, y, 0.0f, 0.0f,
                0.0f, 0.0f, z, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f
            );
        }

        /// <summary>Returns a Float4x4 non-uniform scale matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4x4 CreateScale(Float3 scales)
        {
            return CreateScale(scales.X, scales.Y, scales.Z);
        }

        /// <summary>Returns a Float4x4 translation matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4x4 CreateTranslation(Float3 vector)
        {
            return new Float4x4(
                1.0f, 0.0f, 0.0f, vector.X,
                0.0f, 1.0f, 0.0f, vector.Y,
                0.0f, 0.0f, 1.0f, vector.Z,
                0.0f, 0.0f, 0.0f, 1.0f
            );
        }

        /// <summary>
        /// Creates a translation, rotation, and scale (TRS) matrix.
        /// Operations are applied in order: scale, then rotate, then translate.
        /// </summary>
        public static Float4x4 CreateTRS(Float3 translation, Quaternion rotation, Float3 scale)
        {
            Float3x3 r = new Float3x3(rotation);
            return new Float4x4(
                new Float4(r.c0.X * scale.X, r.c0.Y * scale.X, r.c0.Z * scale.X, 0.0f),
                new Float4(r.c1.X * scale.Y, r.c1.Y * scale.Y, r.c1.Z * scale.Y, 0.0f),
                new Float4(r.c2.X * scale.Z, r.c2.Y * scale.Z, r.c2.Z * scale.Z, 0.0f),
                new Float4(translation.X, translation.Y, translation.Z, 1.0f)
            );
        }

        /// <summary>Creates a Left-Handed view matrix.</summary>
        public static Float4x4 CreateLookAt(Float3 eyePosition, Float3 targetPosition, Float3 upVector)
        {
            Float3 zaxis = Maths.Normalize(targetPosition - eyePosition); // Camera's forward
            Float3 xaxis = Maths.Normalize(Maths.Cross(upVector, zaxis)); // Camera's right
            Float3 yaxis = Maths.Cross(zaxis, xaxis);                     // Camera's up

            Float4 c0 = new Float4(xaxis.X, yaxis.X, zaxis.X, 0.0f);
            Float4 c1 = new Float4(xaxis.Y, yaxis.Y, zaxis.Y, 0.0f);
            Float4 c2 = new Float4(xaxis.Z, yaxis.Z, zaxis.Z, 0.0f);
            Float4 c3 = new Float4(
                -Maths.Dot(xaxis, eyePosition),
                -Maths.Dot(yaxis, eyePosition),
                -Maths.Dot(zaxis, eyePosition),
                1.0f
            );
            return new Float4x4(c0, c1, c2, c3);
        }


        /// <summary>Creates a Left-Handed orthographic projection matrix (Depth [0,1]).</summary>
        public static Float4x4 CreateOrtho(float width, float height, float nearPlane, float farPlane)
        {
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            return CreateOrthoOffCenter(-halfWidth, halfWidth, -halfHeight, halfHeight, nearPlane, farPlane);
        }

        /// <summary>Creates a Left-Handed orthographic projection matrix (Depth [0,1]).</summary>
        public static Float4x4 CreateOrthoOffCenter(float left, float right, float bottom, float top, float nearPlane, float farPlane)
        {
            float r_l = right - left;
            float t_b = top - bottom;
            float f_n = farPlane - nearPlane;

            if (r_l == 0 || t_b == 0 || f_n == 0) return Float4x4.Identity;

            Float4x4 result = Float4x4.Identity;
            result.c0.X = 2.0f / r_l;
            result.c1.Y = 2.0f / t_b;
            // DirectX style (maps Z to [0,1])
            result.c2.Z = 1.0f / f_n;

            result.c3.X = -(right + left) / r_l;
            result.c3.Y = -(top + bottom) / t_b;
            result.c3.Z = -nearPlane / f_n;
            // result.c3.W = 1.0f; // Already set by Identity
            return result;
        }

        /// <summary>Creates a Left-Handed perspective projection matrix (Depth [0,1]).</summary>
        public static Float4x4 CreatePerspectiveFov(float verticalFovRadians, float aspectRatio, float nearPlane, float farPlane)
        {
            // Ensure parameters are valid
            if (nearPlane <= 0.0f || farPlane <= 0.0f || aspectRatio <= 0.0f || verticalFovRadians <= 0.0f || verticalFovRadians >= Maths.PI || nearPlane >= farPlane)
            {
                return Float4x4.Identity; // Or throw
            }

            float yScale = 1.0f / Maths.Tan(verticalFovRadians * 0.5f);
            float xScale = yScale / aspectRatio;

            Float4x4 result = Float4x4.Zero;
            result.c0.X = xScale;
            result.c1.Y = yScale;
            // DirectX style (maps Z to [0,1])
            result.c2.Z = farPlane / (farPlane - nearPlane);
            result.c2.W = 1.0f; // Puts Z_eye into W_clip
            result.c3.Z = -nearPlane * farPlane / (farPlane - nearPlane);
            // result.c3.W is 0.0f from Zero init
            return result;
        }

    }

    public static partial class Maths
    {
    }
}
