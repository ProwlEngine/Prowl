using System;
using System.Globalization;
using System.Text;

namespace Prowl.Vector
{
    /// <summary>A 3x3 matrix of floats.</summary>
    [System.Serializable]
    public partial struct Float3x3 : System.IEquatable<Float3x3>, IFormattable
    {
        /// <summary>Float3x3 identity transform.</summary>
        public static readonly Float3x3 Identity = new Float3x3(new Float3(1f, 0f, 0f), new Float3(0f, 1f, 0f), new Float3(0f, 0f, 1f));
        /// <summary>Float3x3 zero value.</summary>
        public static readonly Float3x3 Zero = new Float3x3(Float3.Zero, Float3.Zero, Float3.Zero);


        /// <summary>Column 0 of the matrix.</summary>
        public Float3 c0;
        /// <summary>Column 1 of the matrix.</summary>
        public Float3 c1;
        /// <summary>Column 2 of the matrix.</summary>
        public Float3 c2;



        #region Properties

        /// <summary>Gets or sets the translation component of the matrix.</summary>
        public Float3 Translation
        {
            get => c2;
            set => c2 = value;
        }

        /// <summary>Returns a reference to the Float3 (column) at a specified index.</summary>
        unsafe public ref Float3 this[int index]
        {
            get
            {
                if ((uint)index >= 3)
                    throw new System.ArgumentOutOfRangeException(nameof(index), $"Column index must be between 0 and 2, but was {index}.");

                fixed (Float3* pC0 = &this.c0)
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
                if ((uint)column >= 3)
                    throw new System.ArgumentOutOfRangeException(nameof(column));
                return this[column][row];
            }
            set
            {
                if ((uint)column >= 3)
                    throw new System.ArgumentOutOfRangeException(nameof(column));
                var temp = this[column];
                temp[row] = value;
                this[column] = temp;
            }
        }

        #endregion


        #region Constructors

        public Float3x3(Float3 col0, Float3 col1, Float3 col2)
        {
            this.c0 = col0;
            this.c1 = col1;
            this.c2 = col2;
        }

        public Float3x3(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22)
        {
            this.c0 = new Float3(m00, m10, m20);
            this.c1 = new Float3(m01, m11, m21);
            this.c2 = new Float3(m02, m12, m22);
        }

        public Float3x3(float v)
        {
            this.c0 = new Float3(v);
            this.c1 = new Float3(v);
            this.c2 = new Float3(v);
        }

        public Float3x3(Double3x3 m)
        {
            this.c0 = new Float3(m.c0);
            this.c1 = new Float3(m.c1);
            this.c2 = new Float3(m.c2);
        }

        /// <summary>
        /// Constructs a Float3x3 from the upper left 3x3 of a Float4x4.
        /// </summary>
        /// <param name="f4x4">Float4x4 to extract a Float3x3 from.</param>
        public Float3x3(Float4x4 f4x4)
        {
            c0 = f4x4.c0.XYZ;
            c1 = f4x4.c1.XYZ;
            c2 = f4x4.c2.XYZ;
        }

        #endregion


        #region Public Methods
        public Float3 GetRow0() => new Float3(c0.X, c1.X, c2.X);
        public void SetRow0(Float3 value)
        {
            c0.X = value.X;
            c1.X = value.Y;
            c2.X = value.Z;
        }

        public Float3 GetRow1() => new Float3(c0.Y, c1.Y, c2.Y);
        public void SetRow1(Float3 value)
        {
            c0.Y = value.X;
            c1.Y = value.Y;
            c2.Y = value.Z;
        }

        public Float3 GetRow2() => new Float3(c0.Z, c1.Z, c2.Z);
        public void SetRow2(Float3 value)
        {
            c0.Z = value.X;
            c1.Z = value.Y;
            c2.Z = value.Z;
        }

        /// <summary>Calculates the inverse of this matrix.</summary>
        /// <returns>The inverted matrix, or a matrix filled with NaN if inversion fails.</returns>
        public Float3x3 Invert()
        {
            Invert(this, out Float3x3 result);
            return result;
        }

        #endregion

        #region Static Methods

        /// <summary>Constructs a Float3x3 matrix from a unit quaternion.</summary>
        /// <param name="q">The quaternion rotation.</param>
        public static Float3x3 FromQuaternion(Quaternion q)
        {
            float x2 = (float)(q.X + q.X);
            float y2 = (float)(q.Y + q.Y);
            float z2 = (float)(q.Z + q.Z);
            float xx = (float)q.X * x2;
            float yy = (float)q.Y * y2;
            float zz = (float)q.Z * z2;
            float xy = (float)q.X * y2;
            float xz = (float)q.X * z2;
            float yz = (float)q.Y * z2;
            float wx = (float)q.W * x2;
            float wy = (float)q.W * y2;
            float wz = (float)q.W * z2;

            float m00 = 1.0f - (yy + zz);
            float m01 = xy + wz;
            float m02 = xz - wy;

            float m10 = xy - wz;
            float m11 = 1.0f - (xx + zz);
            float m12 = yz + wx;

            float m20 = xz + wy;
            float m21 = yz - wx;
            float m22 = 1.0f - (xx + yy);

            return new Float3x3(m00, m01, m02,
                                m10, m11, m12,
                                m20, m21, m22);
        }

        /// <summary>
        /// Returns a Float3x3 matrix representing a rotation around a unit axis by an angle in radians.
        /// </summary>
        /// <param name="axis">The rotation axis (must be normalized).</param>
        /// <param name="angle">The angle of rotation in radians.</param>
        public static Float3x3 FromAxisAngle(Float3 axis, float angle)
        {
            var s = Maths.Sin(angle);
            var c = Maths.Cos(angle);

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
        public static Float3x3 RotateX(float angle)
        {
            var s = Maths.Sin(angle);
            var c = Maths.Cos(angle);

            return new Float3x3(
                1.0f, 0.0f, 0.0f,
                0.0f, c, -s,
                0.0f, s, c
            );
        }

        /// <summary>Returns a Float3x3 matrix that rotates around the Y-axis.</summary>
        public static Float3x3 RotateY(float angle)
        {
            var s = Maths.Sin(angle);
            var c = Maths.Cos(angle);

            return new Float3x3(
                c, 0.0f, s,
                0.0f, 1.0f, 0.0f,
               -s, 0.0f, c
            );
        }

        /// <summary>Returns a Float3x3 matrix that rotates around the Z-axis.</summary>
        public static Float3x3 RotateZ(float angle)
        {
            var s = Maths.Sin(angle);
            var c = Maths.Cos(angle);

            return new Float3x3(
                c, -s, 0.0f,
                s, c, 0.0f,
                0.0f, 0.0f, 1.0f
            );
        }

        /// <summary>Returns a Float3x3 matrix representing a uniform scaling.</summary>
        public static Float3x3 Scale(float s)
        {
            return new Float3x3(
                s, 0.0f, 0.0f,
                0.0f, s, 0.0f,
                0.0f, 0.0f, s
            );
        }

        /// <summary>Returns a Float3x3 matrix representing a non-uniform axis scaling.</summary>
        public static Float3x3 Scale(float x, float y, float z)
        {
            return new Float3x3(
                x, 0.0f, 0.0f,
                0.0f, y, 0.0f,
                0.0f, 0.0f, z
            );
        }

        /// <summary>Returns a Float3x3 matrix representing a non-uniform axis scaling.</summary>
        public static Float3x3 Scale(Float3 v)
        {
            return Scale(v.X, v.Y, v.Z);
        }

        /// <summary>Creates a 3x3 view rotation matrix. Assumes forward and up are normalized and not collinear.</summary>
        public static Float3x3 CreateLookRotation(Float3 forward, Float3 up)
        {
            if (Float3.LengthSquared(forward) < Maths.Epsilon || Float3.LengthSquared(up) < Maths.Epsilon)
                return Float3x3.Identity;

            Float3 zaxis = Float3.Normalize(forward);
            Float3 xaxis = Float3.Cross(up, zaxis);

            if (Float3.LengthSquared(xaxis) < Maths.Epsilon) // Collinear (degenerate)
            {
                return Float3x3.Identity;
            }
            else
            {
                xaxis = Float3.Normalize(xaxis);
            }

            Float3 yaxis = Float3.Cross(zaxis, xaxis); // Already normalized if xaxis and zaxis are orthonormal

            return new Float3x3(
                xaxis.X, yaxis.X, zaxis.X,
                xaxis.Y, yaxis.Y, zaxis.Y,
                xaxis.Z, yaxis.Z, zaxis.Z
            );
        }

        /// <summary>Returns the transpose of a Float3x3 matrix.</summary>
        /// <param name="m">The matrix to transpose.</param>
        /// <returns>The transposed matrix (Float3x3).</returns>
        public static Float3x3 Transpose(Float3x3 m) => new Float3x3(
                new Float3(m.c0.X, m.c1.X, m.c2.X),
                new Float3(m.c0.Y, m.c1.Y, m.c2.Y),
                new Float3(m.c0.Z, m.c1.Z, m.c2.Z)
            );

        /// <summary>Transforms a 3D normal vector using the inverse transpose of a 3x3 matrix.</summary>
        /// <param name="normal">The 3D normal vector to transform.</param>
        /// <param name="matrix">The 3x3 transformation matrix.</param>
        /// <returns>The transformed and normalized normal vector.</returns>
        public static Float3 TransformNormal(Float3 normal, Float3x3 matrix)
        {
            // For normals, we need to use the inverse transpose of the matrix
            if (!Invert(matrix, out Float3x3 inverse))
            {
                // Matrix is singular, return the original normal
                return normal;
            }
            Float3x3 invTranspose = Transpose(inverse);
            Float3 transformed = invTranspose * normal;
            return Float3.Normalize(transformed);
        }

        // TransformPoint functions
        /// <summary>Transforms a 2D point using a 3x3 matrix (treating point as homogeneous with w=1).</summary>
        /// <param name="point">The 2D point to transform.</param>
        /// <param name="matrix">The 3x3 transformation matrix.</param>
        /// <returns>The transformed 2D point with perspective divide applied.</returns>
        public static Float2 TransformPoint(Float2 point, Float3x3 matrix)
        {
            // Treat point as homogeneous coordinates (x, y, 1)
            Float3 homogeneous = new Float3(point.X, point.Y, 1.0f);
            Float3 transformed = matrix * homogeneous;

            // Perform perspective divide
            if (Maths.Abs(transformed.Z) > float.Epsilon)
                return new Float2(transformed.X / transformed.Z, transformed.Y / transformed.Z);
            else
                return new Float2(transformed.X, transformed.Y);
        }

        /// <summary>Transforms a 2D normal vector using the upper-left 2x2 portion of a 3x3 matrix.</summary>
        /// <param name="normal">The 2D normal vector to transform.</param>
        /// <param name="matrix">The 3x3 transformation matrix.</param>
        /// <returns>The transformed and normalized normal vector.</returns>
        public static Float2 TransformNormal(Float2 normal, Float3x3 matrix)
        {
            // Extract the upper-left 2x2 portion for rotation/scale
            Float2x2 upperLeft = new Float2x2(
                matrix.c0.X, matrix.c0.Y,
                matrix.c1.X, matrix.c1.Y
            );
            return Float2x2.TransformNormal(normal, upperLeft);
        }

        /// <summary>Attempts to calculate the inverse of the given matrix. If successful, result will contain the inverted matrix.</summary>
        /// <param name="matrix">The source matrix to invert.</param>
        /// <param name="result">If successful, contains the inverted matrix.</param>
        /// <returns>True if the source matrix could be inverted; False otherwise.</returns>
        public static bool Invert(Float3x3 matrix, out Float3x3 result)
        {
            var m00 = matrix.c0.X; var m01 = matrix.c1.X; var m02 = matrix.c2.X;
            var m10 = matrix.c0.Y; var m11 = matrix.c1.Y; var m12 = matrix.c2.Y;
            var m20 = matrix.c0.Z; var m21 = matrix.c1.Z; var m22 = matrix.c2.Z;

            // Calculate determinant
            var det = m00 * (m11 * m22 - m12 * m21) -
                      m01 * (m10 * m22 - m12 * m20) +
                      m02 * (m10 * m21 - m11 * m20);

            if (MathF.Abs(det) < float.Epsilon)
            {
                result = new Float3x3(
                    new Float3(float.NaN, float.NaN, float.NaN),
                    new Float3(float.NaN, float.NaN, float.NaN),
                    new Float3(float.NaN, float.NaN, float.NaN)
                );
                return false;
            }

            var invDet = 1f / det;

            // Calculate cofactors and transpose (adjugate matrix)
            result = new Float3x3(
                new Float3(
                    (m11 * m22 - m12 * m21) * invDet,
                    -(m10 * m22 - m12 * m20) * invDet,
                    (m10 * m21 - m11 * m20) * invDet
                ),
                new Float3(
                    -(m01 * m22 - m02 * m21) * invDet,
                    (m00 * m22 - m02 * m20) * invDet,
                    -(m00 * m21 - m01 * m20) * invDet
                ),
                new Float3(
                    (m01 * m12 - m02 * m11) * invDet,
                    -(m00 * m12 - m02 * m10) * invDet,
                    (m00 * m11 - m01 * m10) * invDet
                )
            );
            return true;
        }

        /// <summary>Calculates the determinant of a Float3x3 matrix.</summary>
        /// <param name="m">The matrix to calculate the determinant of.</param>
        /// <returns>The determinant of the matrix.</returns>
        public static float Determinant(Float3x3 m) =>
            // Using the scalar triple product formula: a · (b x c)
            m.c0.X * (m.c1.Y * m.c2.Z - m.c1.Z * m.c2.Y) -
                   m.c0.Y * (m.c1.X * m.c2.Z - m.c1.Z * m.c2.X) +
                   m.c0.Z * (m.c1.X * m.c2.Y - m.c1.Y * m.c2.X);

        #endregion

        #region Operators

        /// <summary>
        /// Returns the result of a matrix-matrix multiplication.
        /// </summary>
        /// <returns>Order matters, so the result of A * B is that B is applied first, then A.</returns>
        public static Float3x3 operator *(Float3x3 a, Float3x3 b) => new Float3x3(
                a.c0 * b.c0.X + a.c1 * b.c0.Y + a.c2 * b.c0.Z,
                a.c0 * b.c1.X + a.c1 * b.c1.Y + a.c2 * b.c1.Z,
                a.c0 * b.c2.X + a.c1 * b.c2.Y + a.c2 * b.c2.Z
            );

        /// <summary>Returns the result of a matrix-vector multiplication.</summary>
        /// <param name="m">The matrix.</param>
        /// <param name="v">The vector.</param>
        /// <returns>The result of m * v.</returns>
        public static Float3 operator *(Float3x3 m, Float3 v) => m.c0 * v.X +
                m.c1 * v.Y +
                m.c2 * v.Z;

        #endregion


        #region Casting

        public static explicit operator Float3x3(Double3x3 m)
        {
            return new Float3x3((Float3)m.c0, (Float3)m.c1, (Float3)m.c2);
        }

        #endregion


        #region Equals and GetHashCode
        public static bool operator ==(Float3x3 lhs, Float3x3 rhs) => lhs.c0 == rhs.c0 && lhs.c1 == rhs.c1 && lhs.c2 == rhs.c2;
        public static bool operator !=(Float3x3 lhs, Float3x3 rhs) => !(lhs == rhs);
        public bool Equals(Float3x3 rhs) => this.c0.Equals(rhs.c0) && this.c1.Equals(rhs.c1) && this.c2.Equals(rhs.c2);
        public override bool Equals(object? o) => o is Float3x3 converted && Equals(converted);
        public override int GetHashCode()
        {
            unchecked // Overflow is fine, just wrap
            {
                int hash = 17;
                hash = hash * 23 + c0.GetHashCode();
                hash = hash * 23 + c1.GetHashCode();
                hash = hash * 23 + c2.GetHashCode();
                return hash;
            }
        }
        #endregion


        public float[] ToArray()
        {
            float[] array = new float[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    array[i * 3 + j] = this[i, j];
            return array;
        }

        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Float3x3(");
            sb.Append(this.c0.X.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c1.X.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c2.X.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append("  ");
            sb.Append(this.c0.Y.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c1.Y.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c2.Y.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append("  ");
            sb.Append(this.c0.Z.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c1.Z.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c2.Z.ToString(format, formatProvider));
            sb.Append(")");
            return sb.ToString();
        }

    }
}
