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
            float x2 = q.X + q.X;
            float y2 = q.Y + q.Y;
            float z2 = q.Z + q.Z;
            float xx = q.X * x2;
            float yy = q.Y * y2;
            float zz = q.Z * z2;
            float xy = q.X * y2;
            float xz = q.X * z2;
            float yz = q.Y * z2;
            float wx = q.W * x2;
            float wy = q.W * y2;
            float wz = q.W * z2;

            float m00 = 1.0f - (yy + zz);
            float m01 = xy + wz;
            float m02 = xz - wy;

            float m10 = xy - wz;
            float m11 = 1.0f - (xx + zz);
            float m12 = yz + wx;

            float m20 = xz + wy;
            float m21 = yz - wx;
            float m22 = 1.0f - (xx + yy);

            this = new Float3x3(m00, m01, m02,
                                m10, m11, m12,
                                m20, m21, m22);
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
            var S = Float4x4.CreateScale(scale);
            var R = Float4x4.CreateFromQuaternion(rotation);
            var T = Float4x4.CreateTranslation(translation);

            // Column-vector convention: apply S, then R, then T
            return Maths.Mul(T, Maths.Mul(R, S));
            // equivalently: return Maths.Mul(Maths.Mul(T, R), S);
        }

        /// <summary>
        /// Creates a rotation matrix from the given Quaternion rotation value.
        /// </summary>
        /// <param name="quaternion">The source Quaternion.</param>
        /// <returns>The rotation matrix.</returns>
        public static Float4x4 CreateFromQuaternion(Quaternion quaternion)
        {
            Float4x4 result = default;

            float xx = quaternion.X * quaternion.X;
            float yy = quaternion.Y * quaternion.Y;
            float zz = quaternion.Z * quaternion.Z;
            
            float xy = quaternion.X * quaternion.Y;
            float wz = quaternion.Z * quaternion.W;
            float xz = quaternion.Z * quaternion.X;
            float wy = quaternion.Y * quaternion.W;
            float yz = quaternion.Y * quaternion.Z;
            float wx = quaternion.X * quaternion.W;

            result.c0.X = 1.0f - 2.0f * (yy + zz);
            result.c0.Y = 2.0f * (xy + wz);
            result.c0.Z = 2.0f * (xz - wy);
            result.c0.W = 0.0f;
            result.c1.X = 2.0f * (xy - wz);
            result.c1.Y = 1.0f - 2.0f * (zz + xx);
            result.c1.Z = 2.0f * (yz + wx);
            result.c1.W = 0.0f;
            result.c2.X = 2.0f * (xz + wy);
            result.c2.Y = 2.0f * (yz - wx);
            result.c2.Z = 1.0f - 2.0f * (yy + xx);
            result.c2.W = 0.0f;

            result.c3 = new Float4(0.0f, 0.0f, 0.0f, 1.0f);

            return result;
        }

        /// <summary> Creates a Left-Handed view matrix from an eye position, a forward direction, and an up vector. </summary>
        public static Float4x4 CreateLookTo(Float3 eyePosition, Float3 forwardVector, Float3 upVector)
        {
            // Simply call CreateLookAt using a target point one unit in the forward direction
            return CreateLookAt(eyePosition, eyePosition + forwardVector, upVector);
        }

        /// <summary>Creates a Left-Handed view matrix from an eye position, a forward direction, and an up vector.</summary>
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
            // This implementation is based on the DirectX Math Library XMMatrixOrthographicLH method
            // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

            float range = 1.0f / (farPlane - nearPlane);

            Float4x4 result = default;

            result.c0.X = 2.0f / width;
            result.c0.Y = result.c0.Z = result.c0.W = 0.0f;

            result.c1.Y = 2.0f / height;
            result.c1.X = result.c1.Z = result.c1.W = 0.0f;

            result.c2.Z = range;
            result.c2.X = result.c2.Y = result.c2.W = 0.0f;

            result.c3.X = result.c3.Y = 0.0f;
            result.c3.Z = -range * nearPlane;
            result.c3.W = 1f;

            return result;
        }

        /// <summary>Creates a Left-Handed orthographic projection matrix (Depth [0,1]).</summary>
        public static Float4x4 CreateOrthoOffCenter(float left, float right, float bottom, float top, float nearPlane, float farPlane)
        {
            // This implementation is based on the DirectX Math Library XMMatrixOrthographicOffCenterLH method
            // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

            float reciprocalWidth = 1.0f / (right - left);
            float reciprocalHeight = 1.0f / (top - bottom);
            float range = 1.0f / (farPlane - nearPlane);

            Float4x4 result = default;

            result.c0 = new Float4(reciprocalWidth + reciprocalWidth, 0, 0, 0);
            result.c1 = new Float4(0, reciprocalHeight + reciprocalHeight, 0, 0);
            result.c2 = new Float4(0, 0, range, 0);
            result.c3 = new Float4(
                -(left + right) * reciprocalWidth,
                -(top + bottom) * reciprocalHeight,
                -range * nearPlane,
                1
            );

            return result;
        }

        /// <summary>Creates a Left-Handed perspective projection matrix (Depth [0,1]).</summary>
        public static Float4x4 CreatePerspectiveFov(float verticalFovRadians, float aspectRatio, float nearPlane, float farPlane)
        {
            // This implementation is based on the DirectX Math Library XMMatrixPerspectiveLH method
            // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

            if(verticalFovRadians <= 0f) throw new ArgumentOutOfRangeException(nameof(verticalFovRadians), "Must be greater than zero.");
            if(verticalFovRadians >= Maths.PI) throw new ArgumentOutOfRangeException(nameof(verticalFovRadians), "Must be less than Pi.");

            if (nearPlane <= 0f) throw new ArgumentOutOfRangeException(nameof(nearPlane), "Must be greater than zero.");
            if(farPlane <= 0f) throw new ArgumentOutOfRangeException(nameof(farPlane), "Must be greater than zero.");
            if(nearPlane >= farPlane) throw new ArgumentOutOfRangeException(nameof(nearPlane), "Must be less than farPlane.");

            float height = 1.0f / Maths.Tan(verticalFovRadians * 0.5f);
            float width = height / aspectRatio;
            float range = float.IsPositiveInfinity(farPlane) ? 1.0f : farPlane / (farPlane - nearPlane);

            Float4x4 result = default;

            result.c0 = new Float4(width, 0, 0, 0);
            result.c1 = new Float4(0, height, 0, 0);
            result.c2 = new Float4(0, 0, range, 1.0f);
            result.c3 = new Float4(0, 0, -range * nearPlane, 0);

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
            var S = Double4x4.CreateScale(scale);
            var R = Double4x4.CreateFromQuaternion(rotation);
            var T = Double4x4.CreateTranslation(translation);

            // Column-vector convention: apply S, then R, then T
            return Maths.Mul(T, Maths.Mul(R, S));
            // equivalently: return Maths.Mul(Maths.Mul(T, R), S);
        }

        /// <summary>
        /// Creates a rotation matrix from the given Quaternion rotation value.
        /// </summary>
        /// <param name="quaternion">The source Quaternion.</param>
        /// <returns>The rotation matrix.</returns>
        public static Double4x4 CreateFromQuaternion(Quaternion quaternion)
        {
            Double4x4 result = default;

            double xx = quaternion.X * quaternion.X;
            double yy = quaternion.Y * quaternion.Y;
            double zz = quaternion.Z * quaternion.Z;
            
            double xy = quaternion.X * quaternion.Y;
            double wz = quaternion.Z * quaternion.W;
            double xz = quaternion.Z * quaternion.X;
            double wy = quaternion.Y * quaternion.W;
            double yz = quaternion.Y * quaternion.Z;
            double wx = quaternion.X * quaternion.W;

            result.c0.X = 1.0 - 2.0 * (yy + zz);
            result.c0.Y = 2.0 * (xy + wz);
            result.c0.Z = 2.0 * (xz - wy);
            result.c0.W = 0.0;
            result.c1.X = 2.0 * (xy - wz);
            result.c1.Y = 1.0 - 2.0 * (zz + xx);
            result.c1.Z = 2.0 * (yz + wx);
            result.c1.W = 0.0;
            result.c2.X = 2.0 * (xz + wy);
            result.c2.Y = 2.0 * (yz - wx);
            result.c2.Z = 1.0 - 2.0 * (yy + xx);
            result.c2.W = 0.0;

            result.c3 = new Double4(0.0, 0.0, 0.0, 1.0);

            return result;
        }


        /// <summary> Creates a Left-Handed view matrix from an eye position, a forward direction, and an up vector. </summary>
        public static Double4x4 CreateLookTo(Double3 eyePosition, Double3 forwardVector, Double3 upVector)
        {
            // Simply call CreateLookAt using a target point one unit in the forward direction
            return CreateLookAt(eyePosition, eyePosition + forwardVector, upVector);
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
            // This implementation is based on the DirectX Math Library XMMatrixOrthographicLH method
            // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

            double range = 1.0 / (farPlane - nearPlane);

            Double4x4 result = default;

            result.c0.X = 2.0 / width;
            result.c0.Y = result.c0.Z = result.c0.W = 0.0;

            result.c1.Y = 2.0 / height;
            result.c1.X = result.c1.Z = result.c1.W = 0.0;

            result.c2.Z = range;
            result.c2.X = result.c2.Y = result.c2.W = 0.0;

            result.c3.X = result.c3.Y = 0.0;
            result.c3.Z = -range * nearPlane;
            result.c3.W = 1;

            return result;
        }

        /// <summary>Creates a Left-Handed orthographic projection matrix (Depth [0,1]).</summary>
        public static Double4x4 CreateOrthoOffCenter(double left, double right, double bottom, double top, double nearPlane, double farPlane)
        {
            // This implementation is based on the DirectX Math Library XMMatrixOrthographicOffCenterLH method
            // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

            double reciprocalWidth = 1.0 / (right - left);
            double reciprocalHeight = 1.0 / (top - bottom);
            double range = 1.0 / (farPlane - nearPlane);

            Double4x4 result = default;

            result.c0 = new Double4(reciprocalWidth + reciprocalWidth, 0, 0, 0);
            result.c1 = new Double4(0, reciprocalHeight + reciprocalHeight, 0, 0);
            result.c2 = new Double4(0, 0, range, 0);
            result.c3 = new Double4(
                -(left + right) * reciprocalWidth,
                -(top + bottom) * reciprocalHeight,
                -range * nearPlane,
                1
            );

            return result;
        }

        /// <summary>Creates a Left-Handed perspective projection matrix (Depth [0,1]).</summary>
        public static Double4x4 CreatePerspectiveFov(double verticalFovRadians, double aspectRatio, double nearPlane, double farPlane)
        {
            // This implementation is based on the DirectX Math Library XMMatrixPerspectiveLH method
            // https://github.com/microsoft/DirectXMath/blob/master/Inc/DirectXMathMatrix.inl

            if (verticalFovRadians <= 0f) throw new ArgumentOutOfRangeException(nameof(verticalFovRadians), "Must be greater than zero.");
            if (verticalFovRadians >= Maths.PI) throw new ArgumentOutOfRangeException(nameof(verticalFovRadians), "Must be less than Pi.");

            if (nearPlane <= 0f) throw new ArgumentOutOfRangeException(nameof(nearPlane), "Must be greater than zero.");
            if (farPlane <= 0f) throw new ArgumentOutOfRangeException(nameof(farPlane), "Must be greater than zero.");
            if (nearPlane >= farPlane) throw new ArgumentOutOfRangeException(nameof(nearPlane), "Must be less than farPlane.");

            double height = 1.0 / Maths.Tan(verticalFovRadians * 0.5);
            double width = height / aspectRatio;
            double range = double.IsPositiveInfinity(farPlane) ? 1.0 : farPlane / (farPlane - nearPlane);

            Double4x4 result = default;

            result.c0 = new Double4(width, 0, 0, 0);
            result.c1 = new Double4(0, height, 0, 0);
            result.c2 = new Double4(0, 0, range, 1.0f);
            result.c3 = new Double4(0, 0, -range * nearPlane, 0);

            return result;
        }
    }

    #endregion
}
