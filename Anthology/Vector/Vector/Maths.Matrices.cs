using System;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    #region Float Matrices

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
            if (Maths.LengthSquared(forward) < Maths.EpsilonF || Maths.LengthSquared(up) < Maths.EpsilonF)
                return Float3x3.Identity;

            Float3 zaxis = Maths.Normalize(forward);
            Float3 xaxis = Maths.Cross(up, zaxis);

            if (Maths.LengthSquared(xaxis) < Maths.EpsilonF) // Collinear (degenerate)
            {
                return Float3x3.Identity;
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
            // Columns: [ right | up | forward | position ]
            Float3 f = Maths.Normalize(targetPosition - eyePosition); // camera forward (+Z in camera space)
            Float3 s = Maths.Normalize(Maths.Cross(upVector, f));     // right  = up × f
            Float3 u = Maths.Cross(f, s);                             // up     = f × s

            Float4x4 m = Float4x4.Identity;

            // Put axes in columns
            m[0, 0] = s.X; m[1, 0] = s.Y; m[2, 0] = s.Z;  // column 0 = right
            m[0, 1] = u.X; m[1, 1] = u.Y; m[2, 1] = u.Z;  // column 1 = up
            m[0, 2] = f.X; m[1, 2] = f.Y; m[2, 2] = f.Z;  // column 2 = forward

            // Translation (position) in last column
            m[0, 3] = eyePosition.X;
            m[1, 3] = eyePosition.Y;
            m[2, 3] = eyePosition.Z;

            // m[3,*] already [0,0,0,1]
            return m;
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

    #endregion

    #region Double Matrices

    public partial struct Double2x2
    {
        /// <summary>
        /// Creates a 2x2 matrix representing a counter-clockwise rotation by an angle in radians.
        /// </summary>
        /// <param name="angle">Rotation angle in radians.</param>
        /// <returns>The 2x2 rotation matrix.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2x2 Rotate(double angle)
        {
            Maths.Sincos(angle, out double s, out double c);
            return new Double2x2(c, -s,   // Row 0
                                 s, c);  // Row 1
        }

        /// <summary>Returns a 2x2 matrix representing a uniform scaling of both axes by s.</summary>
        /// <param name="s">The scaling factor.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2x2 Scale(double s)
        {
            return new Double2x2(s, 0.0,
                                 0.0, s);
        }

        /// <summary>Returns a 2x2 matrix representing a non-uniform axis scaling by x and y.</summary>
        /// <param name="x">The x-axis scaling factor.</param>
        /// <param name="y">The y-axis scaling factor.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2x2 Scale(double x, double y)
        {
            return new Double2x2(x, 0.0,
                                 0.0, y);
        }

        /// <summary>Returns a 2x2 matrix representing a non-uniform axis scaling by the components of the Double2 vector v.</summary>
        /// <param name="v">The Double2 containing the x and y axis scaling factors.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2x2 Scale(Double2 v)
        {
            return Scale(v.X, v.Y);
        }
    }

    public partial struct Double3x3
    {
        /// <summary>
        /// Constructs a Double3x3 from the upper left 3x3 of a Double4x4.
        /// </summary>
        /// <param name="d4x4">Double4x4 to extract a Double3x3 from.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3x3(Double4x4 d4x4)
        {
            c0 = d4x4.c0.XYZ;
            c1 = d4x4.c1.XYZ;
            c2 = d4x4.c2.XYZ;
        }

        /// <summary>Constructs a Double3x3 matrix from a unit quaternion.</summary>
        /// <param name="q">The quaternion rotation.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3x3(Quaternion q)
        {
            // Standard formula
            double xx = q.X * q.X; double yy = q.Y * q.Y; double zz = q.Z * q.Z;
            double xy = q.X * q.Y; double xz = q.X * q.Z; double yz = q.Y * q.Z;
            double wx = q.W * q.X; double wy = q.W * q.Y; double wz = q.W * q.Z;

            double m00 = 1.0 - 2.0 * (yy + zz);
            double m01 = 2.0 * (xy - wz);
            double m02 = 2.0 * (xz + wy);

            double m10 = 2.0 * (xy + wz);
            double m11 = 1.0 - 2.0 * (xx + zz);
            double m12 = 2.0 * (yz - wx);

            double m20 = 2.0 * (xz - wy);
            double m21 = 2.0 * (yz + wx);
            double m22 = 1.0 - 2.0 * (xx + yy);

            this = new Double3x3(m00, m01, m02, m10, m11, m12, m20, m21, m22);
        }

        /// <summary>
        /// Returns a Double3x3 matrix representing a rotation around a unit axis by an angle in radians.
        /// </summary>
        /// <param name="axis">The rotation axis (must be normalized).</param>
        /// <param name="angle">The angle of rotation in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3x3 FromAxisAngle(Double3 axis, double angle)
        {
            Maths.Sincos(angle, out double s, out double c);
            double t = 1.0 - c;

            double x = axis.X;
            double y = axis.Y;
            double z = axis.Z;

            // Assumes axis is normalized
            double m00 = t * x * x + c;
            double m01 = t * x * y - s * z;
            double m02 = t * x * z + s * y;

            double m10 = t * x * y + s * z;
            double m11 = t * y * y + c;
            double m12 = t * y * z - s * x;

            double m20 = t * x * z - s * y;
            double m21 = t * y * z + s * x;
            double m22 = t * z * z + c;

            return new Double3x3(m00, m01, m02, m10, m11, m12, m20, m21, m22);
        }

        /// <summary>Returns a Double3x3 matrix that rotates around the X-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3x3 RotateX(double angle)
        {
            Maths.Sincos(angle, out double s, out double c);
            return new Double3x3(
                1.0, 0.0, 0.0,
                0.0, c, -s,
                0.0, s, c
            );
        }

        /// <summary>Returns a Double3x3 matrix that rotates around the Y-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3x3 RotateY(double angle)
        {
            Maths.Sincos(angle, out double s, out double c);
            return new Double3x3(
                c, 0.0, s,
                0.0, 1.0, 0.0,
               -s, 0.0, c
            );
        }

        /// <summary>Returns a Double3x3 matrix that rotates around the Z-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3x3 RotateZ(double angle)
        {
            Maths.Sincos(angle, out double s, out double c);
            return new Double3x3(
                c, -s, 0.0,
                s, c, 0.0,
                0.0, 0.0, 1.0
            );
        }

        /// <summary>Returns a Double3x3 matrix representing a uniform scaling.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3x3 Scale(double s)
        {
            return new Double3x3(
                s, 0.0, 0.0,
                0.0, s, 0.0,
                0.0, 0.0, s
            );
        }

        /// <summary>Returns a Double3x3 matrix representing a non-uniform axis scaling.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3x3 Scale(double x, double y, double z)
        {
            return new Double3x3(
                x, 0.0, 0.0,
                0.0, y, 0.0,
                0.0, 0.0, z
            );
        }

        /// <summary>Returns a Double3x3 matrix representing a non-uniform axis scaling.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3x3 Scale(Double3 v)
        {
            return Scale(v.X, v.Y, v.Z);
        }

        /// <summary>Creates a 3x3 view rotation matrix. Assumes forward and up are normalized and not collinear.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3x3 CreateLookRotation(Double3 forward, Double3 up)
        {
            Double3 zaxis = Maths.Normalize(forward);
            Double3 xaxis = Maths.Normalize(Maths.Cross(up, zaxis));
            Double3 yaxis = Maths.Cross(zaxis, xaxis); // Already normalized

            return new Double3x3(
                xaxis.X, yaxis.X, zaxis.X,
                xaxis.Y, yaxis.Y, zaxis.Y,
                xaxis.Z, yaxis.Z, zaxis.Z
            );
        }

        /// <summary>Creates a 3x3 view rotation matrix with safety checks.</summary>
        public static Double3x3 CreateLookRotationSafe(Double3 forward, Double3 up)
        {
            if (Maths.LengthSquared(forward) < Maths.Epsilon || Maths.LengthSquared(up) < Maths.Epsilon)
                return Double3x3.Identity;

            Double3 zaxis = Maths.Normalize(forward);
            Double3 xaxis = Maths.Cross(up, zaxis);

            if (Maths.LengthSquared(xaxis) < Maths.Epsilon) // Collinear (degenerate)
            {
                return Double3x3.Identity;
            }
            else
            {
                xaxis = Maths.Normalize(xaxis);
            }

            Double3 yaxis = Maths.Cross(zaxis, xaxis); // Already normalized if xaxis and zaxis are orthonormal

            return new Double3x3(
                xaxis.X, yaxis.X, zaxis.X,
                xaxis.Y, yaxis.Y, zaxis.Y,
                xaxis.Z, yaxis.Z, zaxis.Z
            );
        }
    }

    public partial struct Double4x4
    {
        /// <summary>Constructs a Double4x4 from a Double3x3 rotation matrix and a Double3 translation vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double4x4(Double3x3 rotation, Double3 translation)
        {
            c0 = new Double4(rotation.c0, 0.0);
            c1 = new Double4(rotation.c1, 0.0);
            c2 = new Double4(rotation.c2, 0.0);
            c3 = new Double4(translation, 1.0);
        }

        /// <summary>Constructs a Double4x4 from a QuaternionD rotation and a Double3 translation vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double4x4(Quaternion rotation, Double3 translation)
        {
            Double3x3 rotMatrix = new Double3x3(rotation);
            c0 = new Double4(rotMatrix.c0, 0.0);
            c1 = new Double4(rotMatrix.c1, 0.0);
            c2 = new Double4(rotMatrix.c2, 0.0);
            c3 = new Double4(translation, 1.0);
        }

        /// <summary>Returns a Double4x4 matrix representing a rotation around an axis by an angle.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4x4 FromAxisAngle(Double3 axis, double angle)
        {
            Double3x3 rot3x3 = Double3x3.FromAxisAngle(axis, angle);
            return new Double4x4(rot3x3, Double3.Zero);
        }

        /// <summary>Returns a Double4x4 matrix that rotates around the X-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4x4 RotateX(double angle)
        {
            Double3x3 rot3x3 = Double3x3.RotateX(angle);
            return new Double4x4(rot3x3, Double3.Zero);
        }

        /// <summary>Returns a Double4x4 matrix that rotates around the Y-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4x4 RotateY(double angle)
        {
            Double3x3 rot3x3 = Double3x3.RotateY(angle);
            return new Double4x4(rot3x3, Double3.Zero);
        }

        /// <summary>Returns a Double4x4 matrix that rotates around the Z-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4x4 RotateZ(double angle)
        {
            Double3x3 rot3x3 = Double3x3.RotateZ(angle);
            return new Double4x4(rot3x3, Double3.Zero);
        }

        /// <summary>Returns a Double4x4 uniform scale matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4x4 CreateScale(double s)
        {
            return new Double4x4(
                s, 0.0, 0.0, 0.0,
                0.0, s, 0.0, 0.0,
                0.0, 0.0, s, 0.0,
                0.0, 0.0, 0.0, 1.0
            );
        }

        /// <summary>Returns a Double4x4 non-uniform scale matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4x4 CreateScale(double x, double y, double z)
        {
            return new Double4x4(
                x, 0.0, 0.0, 0.0,
                0.0, y, 0.0, 0.0,
                0.0, 0.0, z, 0.0,
                0.0, 0.0, 0.0, 1.0
            );
        }

        /// <summary>Returns a Double4x4 non-uniform scale matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4x4 CreateScale(Double3 scales)
        {
            return CreateScale(scales.X, scales.Y, scales.Z);
        }

        /// <summary>Returns a Double4x4 translation matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4x4 CreateTranslation(Double3 vector)
        {
            return new Double4x4(
                1.0, 0.0, 0.0, vector.X,
                0.0, 1.0, 0.0, vector.Y,
                0.0, 0.0, 1.0, vector.Z,
                0.0, 0.0, 0.0, 1.0
            );
        }

        /// <summary>
        /// Creates a translation, rotation, and scale (TRS) matrix.
        /// Operations are applied in order: scale, then rotate, then translate.
        /// </summary>
        public static Double4x4 CreateTRS(Double3 translation, Quaternion rotation, Double3 scale)
        {
            Double3x3 r = new Double3x3(rotation);
            return new Double4x4(
                new Double4(r.c0.X * scale.X, r.c0.Y * scale.X, r.c0.Z * scale.X, 0.0),
                new Double4(r.c1.X * scale.Y, r.c1.Y * scale.Y, r.c1.Z * scale.Y, 0.0),
                new Double4(r.c2.X * scale.Z, r.c2.Y * scale.Z, r.c2.Z * scale.Z, 0.0),
                new Double4(translation.X, translation.Y, translation.Z, 1.0)
            );
        }

        /// <summary>Creates a Left-Handed view matrix.</summary>
        public static Double4x4 CreateLookAt(Double3 eyePosition, Double3 targetPosition, Double3 upVector)
        {
            // Columns: [ right | up | forward | position ]
            Double3 f = Maths.Normalize(targetPosition - eyePosition); // camera forward (+Z in camera space)
            Double3 s = Maths.Normalize(Maths.Cross(upVector, f));     // right  = up × f
            Double3 u = Maths.Cross(f, s);                             // up     = f × s

            Double4x4 m = Double4x4.Identity;

            // Put axes in columns
            m[0, 0] = s.X; m[1, 0] = s.Y; m[2, 0] = s.Z;  // column 0 = right
            m[0, 1] = u.X; m[1, 1] = u.Y; m[2, 1] = u.Z;  // column 1 = up
            m[0, 2] = f.X; m[1, 2] = f.Y; m[2, 2] = f.Z;  // column 2 = forward

            // Translation (position) in last column
            m[0, 3] = eyePosition.X;
            m[1, 3] = eyePosition.Y;
            m[2, 3] = eyePosition.Z;

            // m[3,*] already [0,0,0,1]
            return m;
        }


        /// <summary>Creates a Left-Handed orthographic projection matrix (Depth [0,1]).</summary>
        public static Double4x4 CreateOrtho(double width, double height, double nearPlane, double farPlane)
        {
            double halfWidth = width * 0.5;
            double halfHeight = height * 0.5;
            return CreateOrthoOffCenter(-halfWidth, halfWidth, -halfHeight, halfHeight, nearPlane, farPlane);
        }

        /// <summary>Creates a Left-Handed orthographic projection matrix (Depth [0,1]).</summary>
        public static Double4x4 CreateOrthoOffCenter(double left, double right, double bottom, double top, double nearPlane, double farPlane)
        {
            double r_l = right - left;
            double t_b = top - bottom;
            double f_n = farPlane - nearPlane;

            if (r_l == 0 || t_b == 0 || f_n == 0) return Double4x4.Identity;

            Double4x4 result = Double4x4.Identity;
            result.c0.X = 2.0 / r_l;
            result.c1.Y = 2.0 / t_b;
            // DirectX style (maps Z to [0,1])
            result.c2.Z = 1.0 / f_n;

            result.c3.X = -(right + left) / r_l;
            result.c3.Y = -(top + bottom) / t_b;
            result.c3.Z = -nearPlane / f_n;
            // result.c3.W = 1.0; // Already set by Identity
            return result;
        }

        /// <summary>Creates a Left-Handed perspective projection matrix (Depth [0,1]).</summary>
        public static Double4x4 CreatePerspectiveFov(double verticalFovRadians, double aspectRatio, double nearPlane, double farPlane)
        {
            // Ensure parameters are valid
            if (nearPlane <= 0.0 || farPlane <= 0.0 || aspectRatio <= 0.0 || verticalFovRadians <= 0.0 || verticalFovRadians >= Maths.PI || nearPlane >= farPlane)
            {
                return Double4x4.Identity; // Or throw
            }

            double yScale = 1.0 / Maths.Tan(verticalFovRadians * 0.5);
            double xScale = yScale / aspectRatio;

            Double4x4 result = Double4x4.Zero;
            result.c0.X = xScale;
            result.c1.Y = yScale;
            // DirectX style (maps Z to [0,1])
            result.c2.Z = farPlane / (farPlane - nearPlane);
            result.c2.W = 1.0; // Puts Z_eye into W_clip
            result.c3.Z = -nearPlane * farPlane / (farPlane - nearPlane);
            // result.c3.W is 0.0 from Zero init
            return result;
        }
    }

    #endregion

    public static partial class Maths
    {
        #region Matrix To Euler

        // These lookup tables and the GetEulerOrderInfo method are a direct port of Ken Shoemake's "Euler Angle Conversion"
        // from the "Graphics Gems IV" book. They are used to decode the EulerOrder enum into the parameters
        // needed for the conversion algorithms.
        private static readonly int[] _eulerNext = { 1, 2, 0, 1 };
        private static readonly int[] _eulerParity = { 0, 1, 0, 1 }; // EulParOdd = 1, EulParEven = 0

        #region Float

        /// <summary>
        /// Decodes the Euler order into its constituent properties for conversion algorithms.
        /// </summary>
        private static void GetEulerOrderInfo(EulerOrder order, out int i, out int j, out int k, out int h, out int parity, out int repeated, out int frame)
        {
            int o = (int)order;
            frame = o & 1;              // 0 for static, 1 for rotating
            repeated = (o >> 1) & 1;    // 0 for false, 1 for true
            parity = (o >> 2) & 1;      // 0 for even, 1 for odd
            o >>= 3;
            i = o & 3;
            j = _eulerNext[i + parity];
            k = _eulerNext[i + 1 - parity];
            h = parity ^ 1; // Unused in this C# port but part of the original algorithm
        }

        /// <summary>
        /// Converts a 3x3 rotation matrix to a set of Euler angles (in radians).
        /// </summary>
        /// <param name="m">The rotation matrix to convert.</param>
        /// <param name="order">The desired order of Euler angles.</param>
        /// <returns>A Float3 vector containing the Euler angles (x, y, z) in radians.</returns>
        public static Float3 ToEuler(Float3x3 m, EulerOrder order)
        {
            GetEulerOrderInfo(order, out int i, out int j, out int k, out int h, out int n, out int s, out int f);

            Float3 ea = Float3.Zero;
            if (s == 1) // Repeated axis order (e.g., XYX)
            {
                float sy = Sqrt(m[i, j] * m[i, j] + m[i, k] * m[i, k]);
                if (sy > 16f * EpsilonF)
                {
                    ea.X = Atan2(m[i, j], m[i, k]);
                    ea.Y = Atan2(sy, m[i, i]);
                    ea.Z = Atan2(m[j, i], -m[k, i]);
                }
                else
                {
                    ea.X = Atan2(-m[j, k], m[j, j]);
                    ea.Y = Atan2(sy, m[i, i]);
                    ea.Z = 0;
                }
            }
            else // Non-repeated axis order (e.g., XYZ)
            {
                float cy = Sqrt(m[i, i] * m[i, i] + m[j, i] * m[j, i]);
                if (cy > 16f * EpsilonF)
                {
                    ea.X = Atan2(m[k, j], m[k, k]);
                    ea.Y = Atan2(-m[k, i], cy);
                    ea.Z = Atan2(m[j, i], m[i, i]);
                }
                else
                {
                    ea.X = Atan2(-m[j, k], m[j, j]);
                    ea.Y = Atan2(-m[k, i], cy);

                    ea.Z = 0;
                }
            }

            if (n == 1) ea = -ea; // Odd parity
            if (f == 1) { float t = ea.X; ea.X = ea.Z; ea.Z = t; } // Rotating frame

            return ea;
        }

        /// <summary>
        /// Converts a 4x4 matrix to a set of Euler angles (in radians).
        /// </summary>
        public static Float3 ToEuler(Float4x4 m, EulerOrder order) => ToEuler(new Float3x3(m), order);

        /// <summary>
        /// Converts a 3x3 rotation matrix to a set of Euler angles (in degrees).
        /// </summary>
        public static Float3 ToEulerDegrees(Float3x3 m, EulerOrder order) => ToDegrees(ToEuler(m, order));

        /// <summary>
        /// Converts a 4x4 matrix to a set of Euler angles (in degrees).
        /// </summary>
        public static Float3 ToEulerDegrees(Float4x4 m, EulerOrder order) => ToDegrees(ToEuler(m, order));

        #endregion

        #region Double



        /// <summary>
        /// Converts a 3x3 rotation matrix to a set of Euler angles (in radians).
        /// </summary>
        /// <param name="m">The rotation matrix to convert.</param>
        /// <param name="order">The desired order of Euler angles.</param>
        /// <returns>A Double3 vector containing the Euler angles (x, y, z) in radians.</returns>
        public static Double3 ToEuler(Double3x3 m, EulerOrder order)
        {
            GetEulerOrderInfo(order, out int i, out int j, out int k, out int h, out int n, out int s, out int f);

            Double3 ea = Double3.Zero;
            if (s == 1) // Repeated axis order (e.g., XYX)
            {
                double sy = Sqrt(m[i, j] * m[i, j] + m[i, k] * m[i, k]);
                if (sy > 16.0 * Epsilon)
                {
                    ea.X = Atan2(m[i, j], m[i, k]);
                    ea.Y = Atan2(sy, m[i, i]);
                    ea.Z = Atan2(m[j, i], -m[k, i]);
                }
                else
                {
                    ea.X = Atan2(-m[j, k], m[j, j]);
                    ea.Y = Atan2(sy, m[i, i]);
                    ea.Z = 0;
                }
            }
            else // Non-repeated axis order (e.g., XYZ)
            {
                double cy = Sqrt(m[i, i] * m[i, i] + m[j, i] * m[j, i]);
                if (cy > 16.0 * Epsilon)
                {
                    ea.X = Atan2(m[k, j], m[k, k]);
                    ea.Y = Atan2(-m[k, i], cy);
                    ea.Z = Atan2(m[j, i], m[i, i]);
                }
                else
                {
                    ea.X = Atan2(-m[j, k], m[j, j]);
                    ea.Y = Atan2(-m[k, i], cy);
                    ea.Z = 0;
                }
            }

            if (n == 1) ea = -ea; // Odd parity
            if (f == 1) { double t = ea.X; ea.X = ea.Z; ea.Z = t; } // Rotating frame

            return ea;
        }

        /// <summary>
        /// Converts a 4x4 matrix to a set of Euler angles (in radians).
        /// </summary>
        public static Double3 ToEuler(Double4x4 m, EulerOrder order) => ToEuler(new Double3x3(m), order);

        /// <summary>
        /// Converts a 3x3 rotation matrix to a set of Euler angles (in degrees).
        /// </summary>
        public static Double3 ToEulerDegrees(Double3x3 m, EulerOrder order) => ToDegrees(ToEuler(m, order));

        /// <summary>
        /// Converts a 4x4 matrix to a set of Euler angles (in degrees).
        /// </summary>
        public static Double3 ToEulerDegrees(Double4x4 m, EulerOrder order) => ToDegrees(ToEuler(m, order));

        #endregion

        #endregion
    }
}
