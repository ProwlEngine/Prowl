using System;
using System.Globalization;
using System.Text;

namespace Prowl.Vector
{
    /// <summary>A 4x4 matrix of doubles.</summary>
    [System.Serializable]
    public partial struct Double4x4 : System.IEquatable<Double4x4>, IFormattable
    {
        /// <summary>Double4x4 identity transform.</summary>
        public static readonly Double4x4 Identity = new Double4x4(new Double4(1.0, 0.0, 0.0, 0.0), new Double4(0.0, 1.0, 0.0, 0.0), new Double4(0.0, 0.0, 1.0, 0.0), new Double4(0.0, 0.0, 0.0, 1.0));
        /// <summary>Double4x4 zero value.</summary>
        public static readonly Double4x4 Zero = new Double4x4(Double4.Zero, Double4.Zero, Double4.Zero, Double4.Zero);


        /// <summary>Column 0 of the matrix.</summary>
        public Double4 c0;
        /// <summary>Column 1 of the matrix.</summary>
        public Double4 c1;
        /// <summary>Column 2 of the matrix.</summary>
        public Double4 c2;
        /// <summary>Column 3 of the matrix.</summary>
        public Double4 c3;


        #region Properties

        /// <summary>Gets or sets the translation component of the matrix.</summary>
        public Double4 Translation
        {
            get => c3;
            set => c3 = value;
        }

        /// <summary>Returns a reference to the Double4 (column) at a specified index.</summary>
        unsafe public ref Double4 this[int index]
        {
            get
            {
                if ((uint)index >= 4)
                    throw new System.ArgumentOutOfRangeException(nameof(index), $"Column index must be between 0 and 3, but was {index}.");

                fixed (Double4* pC0 = &this.c0)
                {
                    return ref pC0[index];
                }
            }
        }

        /// <summary>Returns the element at row and column indices.</summary>
        public double this[int row, int column]
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

        public Double4x4(Double4 col0, Double4 col1, Double4 col2, Double4 col3)
        {
            this.c0 = col0;
            this.c1 = col1;
            this.c2 = col2;
            this.c3 = col3;
        }

        public Double4x4(double m00, double m01, double m02, double m03, double m10, double m11, double m12, double m13, double m20, double m21, double m22, double m23, double m30, double m31, double m32, double m33)
        {
            this.c0 = new Double4(m00, m10, m20, m30);
            this.c1 = new Double4(m01, m11, m21, m31);
            this.c2 = new Double4(m02, m12, m22, m32);
            this.c3 = new Double4(m03, m13, m23, m33);
        }

        public Double4x4(double v)
        {
            this.c0 = new Double4(v);
            this.c1 = new Double4(v);
            this.c2 = new Double4(v);
            this.c3 = new Double4(v);
        }

        public Double4x4(Float4x4 m)
        {
            this.c0 = new Double4(m.c0);
            this.c1 = new Double4(m.c1);
            this.c2 = new Double4(m.c2);
            this.c3 = new Double4(m.c3);
        }

        /// <summary>Constructs a Double4x4 from a Double3x3 rotation matrix and a Double3 translation vector.</summary>
        public Double4x4(Double3x3 rotation, Double3 translation)
        {
            c0 = new Double4(rotation.c0, 0.0);
            c1 = new Double4(rotation.c1, 0.0);
            c2 = new Double4(rotation.c2, 0.0);
            c3 = new Double4(translation, 1.0);
        }

        /// <summary>Constructs a Double4x4 from a QuaternionD rotation and a Double3 translation vector.</summary>
        public Double4x4(Quaternion rotation, Double3 translation)
        {
            Double3x3 rotMatrix = Double3x3.FromQuaternion(rotation);
            c0 = new Double4(rotMatrix.c0, 0.0);
            c1 = new Double4(rotMatrix.c1, 0.0);
            c2 = new Double4(rotMatrix.c2, 0.0);
            c3 = new Double4(translation, 1.0);
        }

        #endregion


        #region Public Methods

        public Double4 GetRow0() => new Double4(c0.X, c1.X, c2.X, c3.X);
        public void SetRow0(Double4 value)
        {
            c0.X = value.X;
            c1.X = value.Y;
            c2.X = value.Z;
            c3.X = value.W;
        }

        public Double4 GetRow1() => new Double4(c0.Y, c1.Y, c2.Y, c3.Y);
        public void SetRow1(Double4 value)
        {
            c0.Y = value.X;
            c1.Y = value.Y;
            c2.Y = value.Z;
            c3.Y = value.W;
        }

        public Double4 GetRow2() => new Double4(c0.Z, c1.Z, c2.Z, c3.Z);
        public void SetRow2(Double4 value)
        {
            c0.Z = value.X;
            c1.Z = value.Y;
            c2.Z = value.Z;
            c3.Z = value.W;
        }

        public Double4 GetRow3() => new Double4(c0.W, c1.W, c2.W, c3.W);
        public void SetRow3(Double4 value)
        {
            c0.W = value.X;
            c1.W = value.Y;
            c2.W = value.Z;
            c3.W = value.W;
        }

        /// <summary>Calculates the inverse of this matrix.</summary>
        /// <returns>The inverted matrix, or a matrix filled with NaN if inversion fails.</returns>
        public Double4x4 Invert()
        {
            Invert(this, out Double4x4 result);
            return result;
        }

        #endregion


        #region Static Methods

        /// <summary>Returns a Double4x4 matrix representing a rotation around an axis by an angle.</summary>
        public static Double4x4 FromAxisAngle(Double3 axis, double angle)
        {
            Double3x3 rot3x3 = Double3x3.FromAxisAngle(axis, angle);
            return new Double4x4(rot3x3, Double3.Zero);
        }

        /// <summary>Returns a Double4x4 matrix that rotates around the X-axis.</summary>
        public static Double4x4 RotateX(double angle)
        {
            Double3x3 rot3x3 = Double3x3.RotateX(angle);
            return new Double4x4(rot3x3, Double3.Zero);
        }

        /// <summary>Returns a Double4x4 matrix that rotates around the Y-axis.</summary>
        public static Double4x4 RotateY(double angle)
        {
            Double3x3 rot3x3 = Double3x3.RotateY(angle);
            return new Double4x4(rot3x3, Double3.Zero);
        }

        /// <summary>Returns a Double4x4 matrix that rotates around the Z-axis.</summary>
        public static Double4x4 RotateZ(double angle)
        {
            Double3x3 rot3x3 = Double3x3.RotateZ(angle);
            return new Double4x4(rot3x3, Double3.Zero);
        }

        /// <summary>Returns a Double4x4 uniform scale matrix.</summary>
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
        public static Double4x4 CreateScale(Double3 scales)
        {
            return CreateScale(scales.X, scales.Y, scales.Z);
        }

        /// <summary>Returns a Double4x4 translation matrix.</summary>
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
            return T * (R * S); // Maths.Mul(T, Maths.Mul(R, S))
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
            Double3 f = Double3.Normalize(forwardVector);                 // camera forward
            Double3 s = Double3.Normalize(Double3.Cross(upVector, f));      // right  = up × f
            Double3 u = Double3.Cross(f, s);                              // up     = f × s

            Double4x4 m = Double4x4.Identity;

            m.c0 = new Double4(s.X, u.X, f.X, 0.0);
            m.c1 = new Double4(s.Y, u.Y, f.Y, 0.0);
            m.c2 = new Double4(s.Z, u.Z, f.Z, 0.0);

            m.c3.X = -Double3.Dot(s, eyePosition);
            m.c3.Y = -Double3.Dot(u, eyePosition);
            m.c3.Z = -Double3.Dot(f, eyePosition);
            m.c3.W = 1.0;

            return m;
        }

        /// <summary>Creates a Left-Handed view matrix.</summary>
        public static Double4x4 CreateLookAt(Double3 eyePosition, Double3 targetPosition, Double3 upVector)
        {
            Double3 f = Double3.Normalize(targetPosition - eyePosition);  // camera forward
            Double3 s = Double3.Normalize(Double3.Cross(upVector, f));      // right  = up × f
            Double3 u = Double3.Cross(f, s);                              // up     = f × s

            Double4x4 m = Double4x4.Identity;

            m.c0 = new Double4(s.X, u.X, f.X, 0.0);
            m.c1 = new Double4(s.Y, u.Y, f.Y, 0.0);
            m.c2 = new Double4(s.Z, u.Z, f.Z, 0.0);

            m.c3.X = -Double3.Dot(s, eyePosition);
            m.c3.Y = -Double3.Dot(u, eyePosition);
            m.c3.Z = -Double3.Dot(f, eyePosition);
            m.c3.W = 1.0;

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

        /// <summary>Returns the transpose of a Double4x4 matrix.</summary>
        /// <param name="m">The matrix to transpose.</param>
        /// <returns>The transposed matrix (Double4x4).</returns>
        public static Double4x4 Transpose(Double4x4 m) => new Double4x4(
                new Double4(m.c0.X, m.c1.X, m.c2.X, m.c3.X),
                new Double4(m.c0.Y, m.c1.Y, m.c2.Y, m.c3.Y),
                new Double4(m.c0.Z, m.c1.Z, m.c2.Z, m.c3.Z),
                new Double4(m.c0.W, m.c1.W, m.c2.W, m.c3.W)
            );

        /// <summary>Transforms a 4D point using a 4x4 matrix (direct multiplication).</summary>
        /// <param name="point">The 4D point to transform.</param>
        /// <param name="matrix">The 4x4 transformation matrix.</param>
        /// <returns>The transformed 4D point.</returns>
        public static Double4 TransformPoint(Double4 point, Double4x4 matrix) => matrix * point;

        /// <summary>Transforms a 3D point using a 4x4 matrix (treating point as homogeneous with w=1).</summary>
        /// <param name="point">The 3D point to transform.</param>
        /// <param name="matrix">The 4x4 transformation matrix.</param>
        /// <returns>The transformed 3D point with perspective divide applied.</returns>
        public static Double3 TransformPoint(Double3 point, Double4x4 matrix)
        {
            // Treat point as homogeneous coordinates (x, y, z, 1)
            Double4 homogeneous = new Double4(point.X, point.Y, point.Z, 1.0);
            Double4 transformed = matrix * homogeneous;

            // Perform perspective divide
            if (Maths.Abs(transformed.W) > double.Epsilon)
                return new Double3(transformed.X / transformed.W, transformed.Y / transformed.W, transformed.Z / transformed.W);
            else
                return new Double3(transformed.X, transformed.Y, transformed.Z);
        }

        /// <summary>Transforms a 3D normal vector using the upper-left 3x3 portion of a 4x4 matrix.</summary>
        /// <param name="normal">The 3D normal vector to transform.</param>
        /// <param name="matrix">The 4x4 transformation matrix.</param>
        /// <returns>The transformed and normalized normal vector.</returns>
        public static Double3 TransformNormal(Double3 normal, Double4x4 matrix)
        {
            // Extract the upper-left 3x3 portion for rotation/scale
            Double3x3 upperLeft = new Double3x3(
                matrix.c0.X, matrix.c0.Y, matrix.c0.Z,
                matrix.c1.X, matrix.c1.Y, matrix.c1.Z,
                matrix.c2.X, matrix.c2.Y, matrix.c2.Z
            );
            return Double3x3.TransformNormal(normal, upperLeft);
        }


        /// <summary>Attempts to calculate the inverse of the given matrix. If successful, result will contain the inverted matrix.</summary>
        /// <param name="matrix">The source matrix to invert.</param>
        /// <param name="result">If successful, contains the inverted matrix.</param>
        /// <returns>True if the source matrix could be inverted; False otherwise.</returns>
        public static bool Invert(Double4x4 matrix, out Double4x4 result)
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

            if (Math.Abs(det) < double.Epsilon)
            {
                result = new Double4x4(
                    new Double4(double.NaN, double.NaN, double.NaN, double.NaN),
                    new Double4(double.NaN, double.NaN, double.NaN, double.NaN),
                    new Double4(double.NaN, double.NaN, double.NaN, double.NaN),
                    new Double4(double.NaN, double.NaN, double.NaN, double.NaN)
                );
                return false;
            }

            var invDet = 1.0 / det;

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

            result = new Double4x4(
                new Double4(
                    a11 * invDet,
                    a12 * invDet,
                    a13 * invDet,
                    a14 * invDet
                ),
                new Double4(
                    -(b * kp_lo - c * jp_ln + d * jo_kn) * invDet,
                    +(a * kp_lo - c * ip_lm + d * io_km) * invDet,
                    -(a * jp_ln - b * ip_lm + d * in_jm) * invDet,
                    +(a * jo_kn - b * io_km + c * in_jm) * invDet
                ),
                new Double4(
                    +(b * gp_ho - c * fp_hn + d * fo_gn) * invDet,
                    -(a * gp_ho - c * ep_hm + d * eo_gm) * invDet,
                    +(a * fp_hn - b * ep_hm + d * en_fm) * invDet,
                    -(a * fo_gn - b * eo_gm + c * en_fm) * invDet
                ),
                new Double4(
                    -(b * gl_hk - c * fl_hj + d * fk_gj) * invDet,
                    +(a * gl_hk - c * el_hi + d * ek_gi) * invDet,
                    -(a * fl_hj - b * el_hi + d * ej_fi) * invDet,
                    +(a * fk_gj - b * ek_gi + c * ej_fi) * invDet
                )
            );
            return true;
        }

        #endregion


        #region Operators

        /// <summary>
        /// Returns the result of a matrix-matrix multiplication.
        /// </summary>
        /// <returns>Order matters, so the result of A * B is that B is applied first, then A.</returns>
        public static Double4x4 operator *(Double4x4 a, Double4x4 b) => new Double4x4(
                a.c0 * b.c0.X + a.c1 * b.c0.Y + a.c2 * b.c0.Z + a.c3 * b.c0.W,
                a.c0 * b.c1.X + a.c1 * b.c1.Y + a.c2 * b.c1.Z + a.c3 * b.c1.W,
                a.c0 * b.c2.X + a.c1 * b.c2.Y + a.c2 * b.c2.Z + a.c3 * b.c2.W,
                a.c0 * b.c3.X + a.c1 * b.c3.Y + a.c2 * b.c3.Z + a.c3 * b.c3.W
            );

        /// <summary>Returns the result of a matrix-vector multiplication.</summary>
        /// <param name="m">The matrix.</param>
        /// <param name="v">The vector.</param>
        /// <returns>The result of m * v.</returns>
        public static Double4 operator *(Double4x4 m, Double4 v) => m.c0 * v.X +
                m.c1 * v.Y +
                m.c2 * v.Z +
                m.c3 * v.W;


        /// <summary>Calculates the determinant of a Double4x4 matrix.</summary>
        /// <param name="m">The matrix to calculate the determinant of.</param>
        /// <returns>The determinant of the matrix.</returns>
        public static double Determinant(Double4x4 m)
        {
            // Components are laid out in column-major order, but the formula is often shown in row-major.
            // We'll use component names a,b,c... for clarity, mapping from the column vectors.
            double a = m.c0.X, b = m.c1.X, c = m.c2.X, d = m.c3.X;
            double e = m.c0.Y, f = m.c1.Y, g = m.c2.Y, h = m.c3.Y;
            double i = m.c0.Z, j = m.c1.Z, k = m.c2.Z, l = m.c3.Z;
            double mm = m.c0.W, n = m.c1.W, o = m.c2.W, p = m.c3.W;

            // Pre-calculate 2x2 determinants for cofactors
            double kp_lo = k * p - l * o;
            double jp_ln = j * p - l * n;
            double jo_kn = j * o - k * n;
            double ip_lm = i * p - l * mm;
            double io_km = i * o - k * mm;
            double in_jm = i * n - j * mm;

            // Cofactor expansion across the first row
            return a * (f * kp_lo - g * jp_ln + h * jo_kn) -
                   b * (e * kp_lo - g * ip_lm + h * io_km) +
                   c * (e * jp_ln - f * ip_lm + h * in_jm) -
                   d * (e * jo_kn - f * io_km + g * in_jm);
        }

        #endregion


        #region Casting

        public static explicit operator Double4x4(Float4x4 m)
        {
            return new Double4x4((Double4)m.c0, (Double4)m.c1, (Double4)m.c2, (Double4)m.c3);
        }

        #endregion


        #region Equals and GetHashCode
        public static bool operator ==(Double4x4 lhs, Double4x4 rhs) => lhs.c0 == rhs.c0 && lhs.c1 == rhs.c1 && lhs.c2 == rhs.c2 && lhs.c3 == rhs.c3;
        public static bool operator !=(Double4x4 lhs, Double4x4 rhs) => !(lhs == rhs);
        public bool Equals(Double4x4 rhs) => this.c0.Equals(rhs.c0) && this.c1.Equals(rhs.c1) && this.c2.Equals(rhs.c2) && this.c3.Equals(rhs.c3);
        public override bool Equals(object? o) => o is Double4x4 converted && Equals(converted);
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
        public double[] ToArray()
        {
            double[] array = new double[16];
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
            sb.Append("Double4x4(");
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
