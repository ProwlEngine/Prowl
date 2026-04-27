using System;
using System.Globalization;
using System.Text;

namespace Prowl.Vector
{
    /// <summary>A 4x4 matrix of floats.</summary>
    [System.Serializable]
    public partial struct Float4x4 : System.IEquatable<Float4x4>, IFormattable
    {
        /// <summary>Float4x4 identity transform.</summary>
        public static readonly Float4x4 Identity = new Float4x4(new Float4(1f, 0f, 0f, 0f), new Float4(0f, 1f, 0f, 0f), new Float4(0f, 0f, 1f, 0f), new Float4(0f, 0f, 0f, 1f));
        /// <summary>Float4x4 zero value.</summary>
        public static readonly Float4x4 Zero = new Float4x4(Float4.Zero, Float4.Zero, Float4.Zero, Float4.Zero);


        /// <summary>Column 0 of the matrix.</summary>
        public Float4 c0;
        /// <summary>Column 1 of the matrix.</summary>
        public Float4 c1;
        /// <summary>Column 2 of the matrix.</summary>
        public Float4 c2;
        /// <summary>Column 3 of the matrix.</summary>
        public Float4 c3;



        #region Properties
        /// <summary>Gets or sets the translation component of the matrix.</summary>
        public Float4 Translation
        {
            get => c3;
            set => c3 = value;
        }

        /// <summary>Returns a reference to the Float4 (column) at a specified index.</summary>
        unsafe public ref Float4 this[int index]
        {
            get
            {
                if ((uint)index >= 4)
                    throw new System.ArgumentOutOfRangeException(nameof(index), $"Column index must be between 0 and 3, but was {index}.");

                fixed (Float4* pC0 = &this.c0)
                {
                    return ref pC0[index];
                }
            }
        }

        /// <summary>Returns the element at row and column indices.</summary>
        public float this[int row, int column]
        {
            get
            {
                if ((uint)column >= 4)
                    throw new System.ArgumentOutOfRangeException(nameof(column));
                return this[column][row];
            }
            set
            {
                if ((uint)column >= 4)
                    throw new System.ArgumentOutOfRangeException(nameof(column));
                var temp = this[column];
                temp[row] = value;
                this[column] = temp;
            }
        }

        #endregion


        #region Constructors
        public Float4x4(Float4 col0, Float4 col1, Float4 col2, Float4 col3)
        {
            this.c0 = col0;
            this.c1 = col1;
            this.c2 = col2;
            this.c3 = col3;
        }

        public Float4x4(float m00, float m01, float m02, float m03, float m10, float m11, float m12, float m13, float m20, float m21, float m22, float m23, float m30, float m31, float m32, float m33)
        {
            this.c0 = new Float4(m00, m10, m20, m30);
            this.c1 = new Float4(m01, m11, m21, m31);
            this.c2 = new Float4(m02, m12, m22, m32);
            this.c3 = new Float4(m03, m13, m23, m33);
        }

        public Float4x4(float v)
        {
            this.c0 = new Float4(v);
            this.c1 = new Float4(v);
            this.c2 = new Float4(v);
            this.c3 = new Float4(v);
        }

        public Float4x4(Double4x4 m)
        {
            this.c0 = new Float4(m.c0);
            this.c1 = new Float4(m.c1);
            this.c2 = new Float4(m.c2);
            this.c3 = new Float4(m.c3);
        }

        /// <summary>Constructs a Float4x4 from a Float3x3 rotation matrix and a Float3 translation vector.</summary>
        public Float4x4(Float3x3 rotation, Float3 translation)
        {
            c0 = new Float4(rotation.c0, 0.0f);
            c1 = new Float4(rotation.c1, 0.0f);
            c2 = new Float4(rotation.c2, 0.0f);
            c3 = new Float4(translation, 1.0f);
        }

        /// <summary>Constructs a Float4x4 from a Quaternion rotation and a Float3 translation vector.</summary>
        public Float4x4(Quaternion rotation, Float3 translation)
        {
            Float3x3 rotMatrix = Float3x3.FromQuaternion(rotation);
            c0 = new Float4(rotMatrix.c0, 0.0f);
            c1 = new Float4(rotMatrix.c1, 0.0f);
            c2 = new Float4(rotMatrix.c2, 0.0f);
            c3 = new Float4(translation, 1.0f);
        }

        #endregion


        #region Public Methods

        public Float4 GetRow0() => new Float4(c0.X, c1.X, c2.X, c3.X);
        public void SetRow0(Float4 value)
        {
            c0.X = value.X;
            c1.X = value.Y;
            c2.X = value.Z;
            c3.X = value.W;
        }

        public Float4 GetRow1() => new Float4(c0.Y, c1.Y, c2.Y, c3.Y);
        public void SetRow1(Float4 value)
        {
            c0.Y = value.X;
            c1.Y = value.Y;
            c2.Y = value.Z;
            c3.Y = value.W;
        }

        public Float4 GetRow2() => new Float4(c0.Z, c1.Z, c2.Z, c3.Z);
        public void SetRow2(Float4 value)
        {
            c0.Z = value.X;
            c1.Z = value.Y;
            c2.Z = value.Z;
            c3.Z = value.W;
        }

        public Float4 GetRow3() => new Float4(c0.W, c1.W, c2.W, c3.W);
        public void SetRow3(Float4 value)
        {
            c0.W = value.X;
            c1.W = value.Y;
            c2.W = value.Z;
            c3.W = value.W;
        }

        /// <summary>Calculates the inverse of this matrix.</summary>
        /// <returns>The inverted matrix, or a matrix filled with NaN if inversion fails.</returns>
        public Float4x4 Invert()
        {
            Invert(this, out Float4x4 result);
            return result;
        }

        #endregion


        #region Static Methods

        /// <summary>Returns a Float4x4 matrix representing a rotation around an axis by an angle.</summary>
        public static Float4x4 FromAxisAngle(Float3 axis, float angle)
        {
            Float3x3 rot3x3 = Float3x3.FromAxisAngle(axis, angle);
            return new Float4x4(rot3x3, Float3.Zero);
        }

        /// <summary>Returns a Float4x4 matrix that rotates around the X-axis.</summary>
        public static Float4x4 RotateX(float angle)
        {
            Float3x3 rot3x3 = Float3x3.RotateX(angle);
            return new Float4x4(rot3x3, Float3.Zero);
        }

        /// <summary>Returns a Float4x4 matrix that rotates around the Y-axis.</summary>
        public static Float4x4 RotateY(float angle)
        {
            Float3x3 rot3x3 = Float3x3.RotateY(angle);
            return new Float4x4(rot3x3, Float3.Zero);
        }

        /// <summary>Returns a Float4x4 matrix that rotates around the Z-axis.</summary>
        public static Float4x4 RotateZ(float angle)
        {
            Float3x3 rot3x3 = Float3x3.RotateZ(angle);
            return new Float4x4(rot3x3, Float3.Zero);
        }

        /// <summary>Returns a Float4x4 uniform scale matrix.</summary>
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
        public static Float4x4 CreateScale(Float3 scales)
        {
            return CreateScale(scales.X, scales.Y, scales.Z);
        }

        /// <summary>Returns a Float4x4 translation matrix.</summary>
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
            return T * (R * S); // Maths.Mul(T, Maths.Mul(R, S))
        }

        /// <summary>
        /// Creates a rotation matrix from the given Quaternion rotation value.
        /// </summary>
        /// <param name="quaternion">The source Quaternion.</param>
        /// <returns>The rotation matrix.</returns>
        public static Float4x4 CreateFromQuaternion(Quaternion quaternion)
        {
            Float4x4 result = default;

            float xx = (float)(quaternion.X * quaternion.X);
            float yy = (float)(quaternion.Y * quaternion.Y);
            float zz = (float)(quaternion.Z * quaternion.Z);

            float xy = (float)(quaternion.X * quaternion.Y);
            float wz = (float)(quaternion.Z * quaternion.W);
            float xz = (float)(quaternion.Z * quaternion.X);
            float wy = (float)(quaternion.Y * quaternion.W);
            float yz = (float)(quaternion.Y * quaternion.Z);
            float wx = (float)(quaternion.X * quaternion.W);

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
            // Calculate camera basis vectors
            Float3 f = Float3.Normalize(targetPosition - eyePosition); // camera forward (+Z in camera space)
            Float3 s = Float3.Normalize(Float3.Cross(upVector, f));     // right  = up × f
            Float3 u = Float3.Cross(f, s);                             // up     = f × s

            // A view matrix transforms from world space to camera space.
            // We need the inverse of the camera-to-world transform.
            // For an orthonormal rotation matrix, the inverse is the transpose.
            // The inverse translation is: -R^T * eyePosition

            Float4x4 m = Float4x4.Identity;

            // Put axes in ROWS (transpose of rotation)
            m[0, 0] = s.X; m[0, 1] = s.Y; m[0, 2] = s.Z;  // row 0 = right
            m[1, 0] = u.X; m[1, 1] = u.Y; m[1, 2] = u.Z;  // row 1 = up
            m[2, 0] = f.X; m[2, 1] = f.Y; m[2, 2] = f.Z;  // row 2 = forward

            // Translation: -R^T * eyePosition = -(dot products with eye position)
            m[0, 3] = -Float3.Dot(s, eyePosition);
            m[1, 3] = -Float3.Dot(u, eyePosition);
            m[2, 3] = -Float3.Dot(f, eyePosition);

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

            if (verticalFovRadians <= 0f) throw new ArgumentOutOfRangeException(nameof(verticalFovRadians), "Must be greater than zero.");
            if (verticalFovRadians >= Maths.PI) throw new ArgumentOutOfRangeException(nameof(verticalFovRadians), "Must be less than Pi.");

            if (nearPlane <= 0f) throw new ArgumentOutOfRangeException(nameof(nearPlane), "Must be greater than zero.");
            if (farPlane <= 0f) throw new ArgumentOutOfRangeException(nameof(farPlane), "Must be greater than zero.");
            if (nearPlane >= farPlane) throw new ArgumentOutOfRangeException(nameof(nearPlane), "Must be less than farPlane.");

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

        /// <summary>Returns the transpose of a Float4x4 matrix.</summary>
        /// <param name="m">The matrix to transpose.</param>
        /// <returns>The transposed matrix (Float4x4).</returns>
        public static Float4x4 Transpose(Float4x4 m) => new Float4x4(
                new Float4(m.c0.X, m.c1.X, m.c2.X, m.c3.X),
                new Float4(m.c0.Y, m.c1.Y, m.c2.Y, m.c3.Y),
                new Float4(m.c0.Z, m.c1.Z, m.c2.Z, m.c3.Z),
                new Float4(m.c0.W, m.c1.W, m.c2.W, m.c3.W)
            );

        /// <summary>Transforms a 3D point using a 4x4 matrix (treating point as homogeneous with w=1).</summary>
        /// <param name="point">The 3D point to transform.</param>
        /// <param name="matrix">The 4x4 transformation matrix.</param>
        /// <returns>The transformed 3D point with perspective divide applied.</returns>
        public static Float3 TransformPoint(Float3 point, Float4x4 matrix)
        {
            // Treat point as homogeneous coordinates (x, y, z, 1)
            Float4 homogeneous = new Float4(point.X, point.Y, point.Z, 1.0f);
            Float4 transformed = matrix * homogeneous;

            // Perform perspective divide
            if (Maths.Abs(transformed.W) > float.Epsilon)
                return new Float3(transformed.X / transformed.W, transformed.Y / transformed.W, transformed.Z / transformed.W);
            else
                return new Float3(transformed.X, transformed.Y, transformed.Z);
        }

        /// <summary>Transforms a 4D point using a 4x4 matrix (direct multiplication).</summary>
        /// <param name="point">The 4D point to transform.</param>
        /// <param name="matrix">The 4x4 transformation matrix.</param>
        /// <returns>The transformed 4D point.</returns>
        public static Float4 TransformPoint(Float4 point, Float4x4 matrix) => matrix * point;

        /// <summary>Transforms a 3D normal vector using the upper-left 3x3 portion of a 4x4 matrix.</summary>
        /// <param name="normal">The 3D normal vector to transform.</param>
        /// <param name="matrix">The 4x4 transformation matrix.</param>
        /// <returns>The transformed and normalized normal vector.</returns>
        public static Float3 TransformNormal(Float3 normal, Float4x4 matrix)
        {
            // Extract the upper-left 3x3 portion for rotation/scale
            Float3x3 upperLeft = new Float3x3(matrix);
            return Float3x3.TransformNormal(normal, upperLeft);
        }

        /// <summary>Attempts to calculate the inverse of the given matrix. If successful, result will contain the inverted matrix.</summary>
        /// <param name="matrix">The source matrix to invert.</param>
        /// <param name="result">If successful, contains the inverted matrix.</param>
        /// <returns>True if the source matrix could be inverted; False otherwise.</returns>
        public static bool Invert(Float4x4 matrix, out Float4x4 result)
        {
            var a = matrix.c0.X; var b = matrix.c1.X; var c = matrix.c2.X; var d = matrix.c3.X;
            var e = matrix.c0.Y; var f = matrix.c1.Y; var g = matrix.c2.Y; var h = matrix.c3.Y;
            var i = matrix.c0.Z; var j = matrix.c1.Z; var k = matrix.c2.Z; var l = matrix.c3.Z;
            var m = matrix.c0.W; var n = matrix.c1.W; var o = matrix.c2.W; var p = matrix.c3.W;

            var kp_lo = k * p - l * o;
            var jp_ln = j * p - l * n;
            var jo_kn = j * o - k * n;
            var ip_lm = i * p - l * m;
            var io_km = i * o - k * m;
            var in_jm = i * n - j * m;

            var a11 = +(f * kp_lo - g * jp_ln + h * jo_kn);
            var a12 = -(e * kp_lo - g * ip_lm + h * io_km);
            var a13 = +(e * jp_ln - f * ip_lm + h * in_jm);
            var a14 = -(e * jo_kn - f * io_km + g * in_jm);

            var det = a * a11 + b * a12 + c * a13 + d * a14;

            if (MathF.Abs(det) < float.Epsilon)
            {
                result = new Float4x4(
                    new Float4(float.NaN, float.NaN, float.NaN, float.NaN),
                    new Float4(float.NaN, float.NaN, float.NaN, float.NaN),
                    new Float4(float.NaN, float.NaN, float.NaN, float.NaN),
                    new Float4(float.NaN, float.NaN, float.NaN, float.NaN)
                );
                return false;
            }

            var invDet = 1f / det;

            var gp_ho = g * p - h * o;
            var fp_hn = f * p - h * n;
            var fo_gn = f * o - g * n;
            var ep_hm = e * p - h * m;
            var eo_gm = e * o - g * m;
            var en_fm = e * n - f * m;

            var gl_hk = g * l - h * k;
            var fl_hj = f * l - h * j;
            var fk_gj = f * k - g * j;
            var el_hi = e * l - h * i;
            var ek_gi = e * k - g * i;
            var ej_fi = e * j - f * i;

            result = new Float4x4(
                new Float4(
                    a11 * invDet,
                    a12 * invDet,
                    a13 * invDet,
                    a14 * invDet
                ),
                new Float4(
                    -(b * kp_lo - c * jp_ln + d * jo_kn) * invDet,
                    +(a * kp_lo - c * ip_lm + d * io_km) * invDet,
                    -(a * jp_ln - b * ip_lm + d * in_jm) * invDet,
                    +(a * jo_kn - b * io_km + c * in_jm) * invDet
                ),
                new Float4(
                    +(b * gp_ho - c * fp_hn + d * fo_gn) * invDet,
                    -(a * gp_ho - c * ep_hm + d * eo_gm) * invDet,
                    +(a * fp_hn - b * ep_hm + d * en_fm) * invDet,
                    -(a * fo_gn - b * eo_gm + c * en_fm) * invDet
                ),
                new Float4(
                    -(b * gl_hk - c * fl_hj + d * fk_gj) * invDet,
                    +(a * gl_hk - c * el_hi + d * ek_gi) * invDet,
                    -(a * fl_hj - b * el_hi + d * ej_fi) * invDet,
                    +(a * fk_gj - b * ek_gi + c * ej_fi) * invDet
                )
            );
            return true;
        }

        /// <summary>Calculates the determinant of a Float4x4 matrix.</summary>
        /// <param name="m">The matrix to calculate the determinant
        public static float Determinant(Float4x4 m)
        {
            // Components are laid out in column-major order, but the formula is often shown in row-major.
            // We'll use component names a,b,c... for clarity, mapping from the column vectors.
            float a = m.c0.X, b = m.c1.X, c = m.c2.X, d = m.c3.X;
            float e = m.c0.Y, f = m.c1.Y, g = m.c2.Y, h = m.c3.Y;
            float i = m.c0.Z, j = m.c1.Z, k = m.c2.Z, l = m.c3.Z;
            float mm = m.c0.W, n = m.c1.W, o = m.c2.W, p = m.c3.W;

            // Pre-calculate 2x2 determinants for cofactors
            float kp_lo = k * p - l * o;
            float jp_ln = j * p - l * n;
            float jo_kn = j * o - k * n;
            float ip_lm = i * p - l * mm;
            float io_km = i * o - k * mm;
            float in_jm = i * n - j * mm;

            // Cofactor expansion across the first row
            return a * (f * kp_lo - g * jp_ln + h * jo_kn) -
                   b * (e * kp_lo - g * ip_lm + h * io_km) +
                   c * (e * jp_ln - f * ip_lm + h * in_jm) -
                   d * (e * jo_kn - f * io_km + g * in_jm);
        }

        #endregion


        #region Operators

        /// <summary>
        /// Returns the result of a matrix-matrix multiplication.
        /// </summary>
        /// <returns>Order matters, so the result of A * B is that B is applied first, then A.</returns>
        public static Float4x4 operator *(Float4x4 a, Float4x4 b) => new Float4x4(
                a.c0 * b.c0.X + a.c1 * b.c0.Y + a.c2 * b.c0.Z + a.c3 * b.c0.W,
                a.c0 * b.c1.X + a.c1 * b.c1.Y + a.c2 * b.c1.Z + a.c3 * b.c1.W,
                a.c0 * b.c2.X + a.c1 * b.c2.Y + a.c2 * b.c2.Z + a.c3 * b.c2.W,
                a.c0 * b.c3.X + a.c1 * b.c3.Y + a.c2 * b.c3.Z + a.c3 * b.c3.W
            );

        /// <summary>Returns the result of a matrix-vector multiplication.</summary>
        /// <param name="m">The matrix.</param>
        /// <param name="v">The vector.</param>
        /// <returns>The result of m * v.</returns>
        public static Float4 operator *(Float4x4 m, Float4 v) => m.c0 * v.X +
                m.c1 * v.Y +
                m.c2 * v.Z +
                m.c3 * v.W;

        #endregion


        #region Casting

        // System.Numerics Casts
        public static implicit operator System.Numerics.Matrix4x4(Float4x4 m)
        {
            return new System.Numerics.Matrix4x4(
                m.c0.X, m.c1.X, m.c2.X, m.c3.X,
                m.c0.Y, m.c1.Y, m.c2.Y, m.c3.Y,
                m.c0.Z, m.c1.Z, m.c2.Z, m.c3.Z,
                m.c0.W, m.c1.W, m.c2.W, m.c3.W);
        }

        public static implicit operator Float4x4(System.Numerics.Matrix4x4 m)
        {
            return new Float4x4(
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44);
        }


        public static explicit operator Float4x4(Double4x4 m)
        {
            return new Float4x4((Float4)m.c0, (Float4)m.c1, (Float4)m.c2, (Float4)m.c3);
        }

        #endregion


        #region Equals and GetHashCode
        public static bool operator ==(Float4x4 lhs, Float4x4 rhs) => lhs.c0 == rhs.c0 && lhs.c1 == rhs.c1 && lhs.c2 == rhs.c2 && lhs.c3 == rhs.c3;
        public static bool operator !=(Float4x4 lhs, Float4x4 rhs) => !(lhs == rhs);
        public bool Equals(Float4x4 rhs) => this.c0.Equals(rhs.c0) && this.c1.Equals(rhs.c1) && this.c2.Equals(rhs.c2) && this.c3.Equals(rhs.c3);
        public override bool Equals(object? o) => o is Float4x4 converted && Equals(converted);
        public override int GetHashCode()
        {
            unchecked // Overflow is fine, just wrap
            {
                int hash = 17;
                hash = hash * 23 + c0.GetHashCode();
                hash = hash * 23 + c1.GetHashCode();
                hash = hash * 23 + c2.GetHashCode();
                hash = hash * 23 + c3.GetHashCode();
                return hash;
            }
        }
        #endregion



        /// <summary>Returns an array of components.</summary>

        public float[] ToArray()
        {
            float[] array = new float[16];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    array[i * 4 + j] = this[i, j];
            return array;
        }

        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Float4x4(");
            sb.Append(this.c0.X.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c1.X.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c2.X.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c3.X.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append("  ");
            sb.Append(this.c0.Y.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c1.Y.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c2.Y.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c3.Y.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append("  ");
            sb.Append(this.c0.Z.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c1.Z.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c2.Z.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c3.Z.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append("  ");
            sb.Append(this.c0.W.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c1.W.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c2.W.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c3.W.ToString(format, formatProvider));
            sb.Append(")");
            return sb.ToString();
        }

    }
}
