using System;
using System.Globalization;
using System.Text;

namespace Prowl.Vector
{
    /// <summary>A 3x3 matrix of doubles.</summary>
    [System.Serializable]
    public partial struct Double3x3 : System.IEquatable<Double3x3>, IFormattable
    {
        /// <summary>Double3x3 identity transform.</summary>
        public static readonly Double3x3 Identity = new Double3x3(new Double3(1.0, 0.0, 0.0), new Double3(0.0, 1.0, 0.0), new Double3(0.0, 0.0, 1.0));
        /// <summary>Double3x3 zero value.</summary>
        public static readonly Double3x3 Zero = new Double3x3(Double3.Zero, Double3.Zero, Double3.Zero);


        /// <summary>Column 0 of the matrix.</summary>
        public Double3 c0;
        /// <summary>Column 1 of the matrix.</summary>
        public Double3 c1;
        /// <summary>Column 2 of the matrix.</summary>
        public Double3 c2;


        #region Properties

        /// <summary>Gets or sets the translation component of the matrix.</summary>
        public Double3 Translation
        {
            get => c2;
            set => c2 = value;
        }


        /// <summary>Returns a reference to the Double3 (column) at a specified index.</summary>
        unsafe public ref Double3 this[int index]
        {
            get
            {
                if ((uint)index >= 3)
                    throw new System.ArgumentOutOfRangeException(nameof(index), $"Column index must be between 0 and 2, but was {index}.");

                fixed (Double3* pC0 = &this.c0)
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

        public Double3x3(Double3 col0, Double3 col1, Double3 col2)
        {
            this.c0 = col0;
            this.c1 = col1;
            this.c2 = col2;
        }

        public Double3x3(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
        {
            this.c0 = new Double3(m00, m10, m20);
            this.c1 = new Double3(m01, m11, m21);
            this.c2 = new Double3(m02, m12, m22);
        }

        public Double3x3(double v)
        {
            this.c0 = new Double3(v);
            this.c1 = new Double3(v);
            this.c2 = new Double3(v);
        }

        public Double3x3(Float3x3 m)
        {
            this.c0 = new Double3(m.c0);
            this.c1 = new Double3(m.c1);
            this.c2 = new Double3(m.c2);
        }

        /// <summary>
        /// Constructs a Double3x3 from the upper left 3x3 of a Double4x4.
        /// </summary>
        /// <param name="d4x4">Double4x4 to extract a Double3x3 from.</param>
        public Double3x3(Double4x4 d4x4)
        {
            c0 = d4x4.c0.XYZ;
            c1 = d4x4.c1.XYZ;
            c2 = d4x4.c2.XYZ;
        }

        #endregion


        #region Public Methods

        public Double3 GetRow0() => new Double3(c0.X, c1.X, c2.X);
        public void SetRow0(Double3 value)
        {
            c0.X = value.X;
            c1.X = value.Y;
            c2.X = value.Z;
        }

        public Double3 GetRow1() => new Double3(c0.Y, c1.Y, c2.Y);
        public void SetRow1(Double3 value)
        {
            c0.Y = value.X;
            c1.Y = value.Y;
            c2.Y = value.Z;
        }

        public Double3 GetRow2() => new Double3(c0.Z, c1.Z, c2.Z);
        public void SetRow2(Double3 value)
        {
            c0.Z = value.X;
            c1.Z = value.Y;
            c2.Z = value.Z;
        }

        /// <summary>Calculates the inverse of this matrix.</summary>
        /// <returns>The inverted matrix, or a matrix filled with NaN if inversion fails.</returns>
        public Double3x3 Invert()
        {
            Invert(this, out Double3x3 result);
            return result;
        }

        #endregion


        #region Static Methods

        /// <summary>Constructs a Double3x3 matrix from a unit quaternion.</summary>
        /// <param name="q">The quaternion rotation.</param>
        public static Double3x3 FromQuaternion(Quaternion q)
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

            return new Double3x3(m00, m01, m02, m10, m11, m12, m20, m21, m22);
        }

        /// <summary>
        /// Returns a Double3x3 matrix representing a rotation around a unit axis by an angle in radians.
        /// </summary>
        /// <param name="axis">The rotation axis (must be normalized).</param>
        /// <param name="angle">The angle of rotation in radians.</param>
        public static Double3x3 FromAxisAngle(Double3 axis, double angle)
        {
            double s = Math.Sin(angle);
            double c = Math.Cos(angle);

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
        public static Double3x3 RotateX(double angle)
        {
            double s = Math.Sin(angle);
            double c = Math.Cos(angle);

            return new Double3x3(
                1.0, 0.0, 0.0,
                0.0, c, -s,
                0.0, s, c
            );
        }

        /// <summary>Returns a Double3x3 matrix that rotates around the Y-axis.</summary>
        public static Double3x3 RotateY(double angle)
        {
            double s = Math.Sin(angle);
            double c = Math.Cos(angle);

            return new Double3x3(
                c, 0.0, s,
                0.0, 1.0, 0.0,
               -s, 0.0, c
            );
        }

        /// <summary>Returns a Double3x3 matrix that rotates around the Z-axis.</summary>
        public static Double3x3 RotateZ(double angle)
        {
            double s = Math.Sin(angle);
            double c = Math.Cos(angle);

            return new Double3x3(
                c, -s, 0.0,
                s, c, 0.0,
                0.0, 0.0, 1.0
            );
        }

        /// <summary>Returns a Double3x3 matrix representing a uniform scaling.</summary>
        public static Double3x3 Scale(double s)
        {
            return new Double3x3(
                s, 0.0, 0.0,
                0.0, s, 0.0,
                0.0, 0.0, s
            );
        }

        /// <summary>Returns a Double3x3 matrix representing a non-uniform axis scaling.</summary>
        public static Double3x3 Scale(double x, double y, double z)
        {
            return new Double3x3(
                x, 0.0, 0.0,
                0.0, y, 0.0,
                0.0, 0.0, z
            );
        }

        /// <summary>Returns a Double3x3 matrix representing a non-uniform axis scaling.</summary>
        public static Double3x3 Scale(Double3 v)
        {
            return Scale(v.X, v.Y, v.Z);
        }

        /// <summary>Creates a 3x3 view rotation matrix. Assumes forward and up are normalized and not collinear.</summary>
        public static Double3x3 CreateLookRotation(Double3 forward, Double3 up)
        {
            if (Double3.LengthSquared(forward) < Maths.Epsilon || Double3.LengthSquared(up) < Maths.Epsilon)
                return Double3x3.Identity;

            Double3 zaxis = Double3.Normalize(forward);
            Double3 xaxis = Double3.Cross(up, zaxis);

            if (Double3.LengthSquared(xaxis) < Maths.Epsilon) // Collinear (degenerate)
            {
                return Double3x3.Identity;
            }
            else
            {
                xaxis = Double3.Normalize(xaxis);
            }

            Double3 yaxis = Double3.Cross(zaxis, xaxis); // Already normalized if xaxis and zaxis are orthonormal

            return new Double3x3(
                xaxis.X, yaxis.X, zaxis.X,
                xaxis.Y, yaxis.Y, zaxis.Y,
                xaxis.Z, yaxis.Z, zaxis.Z
            );
        }

        /// <summary>Returns the transpose of a Double3x3 matrix.</summary>
        /// <param name="m">The matrix to transpose.</param>
        /// <returns>The transposed matrix (Double3x3).</returns>
        public static Double3x3 Transpose(Double3x3 m) => new Double3x3(
                new Double3(m.c0.X, m.c1.X, m.c2.X),
                new Double3(m.c0.Y, m.c1.Y, m.c2.Y),
                new Double3(m.c0.Z, m.c1.Z, m.c2.Z)
            );

        /// <summary>Transforms a 2D point using a 3x3 matrix (treating point as homogeneous with w=1).</summary>
        /// <param name="point">The 2D point to transform.</param>
        /// <param name="matrix">The 3x3 transformation matrix.</param>
        /// <returns>The transformed 2D point with perspective divide applied.</returns>
        public static Double2 TransformPoint(Double2 point, Double3x3 matrix)
        {
            // Treat point as homogeneous coordinates (x, y, 1)
            Double3 homogeneous = new Double3(point.X, point.Y, 1.0);
            Double3 transformed = matrix * homogeneous;

            // Perform perspective divide
            if (Maths.Abs(transformed.Z) > double.Epsilon)
                return new Double2(transformed.X / transformed.Z, transformed.Y / transformed.Z);
            else
                return new Double2(transformed.X, transformed.Y);
        }

        /// <summary>Transforms a 2D normal vector using the upper-left 2x2 portion of a 3x3 matrix.</summary>
        /// <param name="normal">The 2D normal vector to transform.</param>
        /// <param name="matrix">The 3x3 transformation matrix.</param>
        /// <returns>The transformed and normalized normal vector.</returns>
        public static Double2 TransformNormal(Double2 normal, Double3x3 matrix)
        {
            // Extract the upper-left 2x2 portion for rotation/scale
            Double2x2 upperLeft = new Double2x2(
                matrix.c0.X, matrix.c0.Y,
                matrix.c1.X, matrix.c1.Y
            );
            return Double2x2.TransformNormal(normal, upperLeft);
        }

        /// <summary>Transforms a 3D normal vector using the inverse transpose of a 3x3 matrix.</summary>
        /// <param name="normal">The 3D normal vector to transform.</param>
        /// <param name="matrix">The 3x3 transformation matrix.</param>
        /// <returns>The transformed and normalized normal vector.</returns>
        public static Double3 TransformNormal(Double3 normal, Double3x3 matrix)
        {
            // For normals, we need to use the inverse transpose of the matrix
            if (!Invert(matrix, out Double3x3 inverse))
            {
                // Matrix is singular, return the original normal
                return normal;
            }
            Double3x3 invTranspose = Transpose(inverse);
            Double3 transformed = invTranspose * normal;
            return Double3.Normalize(transformed);
        }

        /// <summary>Attempts to calculate the inverse of the given matrix. If successful, result will contain the inverted matrix.</summary>
        /// <param name="matrix">The source matrix to invert.</param>
        /// <param name="result">If successful, contains the inverted matrix.</param>
        /// <returns>True if the source matrix could be inverted; False otherwise.</returns>
        public static bool Invert(Double3x3 matrix, out Double3x3 result)
        {
            var m00 = matrix.c0.X; var m01 = matrix.c1.X; var m02 = matrix.c2.X;
            var m10 = matrix.c0.Y; var m11 = matrix.c1.Y; var m12 = matrix.c2.Y;
            var m20 = matrix.c0.Z; var m21 = matrix.c1.Z; var m22 = matrix.c2.Z;

            // Calculate determinant
            var det = m00 * (m11 * m22 - m12 * m21) -
                      m01 * (m10 * m22 - m12 * m20) +
                      m02 * (m10 * m21 - m11 * m20);

            if (Math.Abs(det) < double.Epsilon)
            {
                result = new Double3x3(
                    new Double3(double.NaN, double.NaN, double.NaN),
                    new Double3(double.NaN, double.NaN, double.NaN),
                    new Double3(double.NaN, double.NaN, double.NaN)
                );
                return false;
            }

            var invDet = 1.0 / det;

            // Calculate cofactors and transpose (adjugate matrix)
            result = new Double3x3(
                new Double3(
                    (m11 * m22 - m12 * m21) * invDet,
                    -(m10 * m22 - m12 * m20) * invDet,
                    (m10 * m21 - m11 * m20) * invDet
                ),
                new Double3(
                    -(m01 * m22 - m02 * m21) * invDet,
                    (m00 * m22 - m02 * m20) * invDet,
                    -(m00 * m21 - m01 * m20) * invDet
                ),
                new Double3(
                    (m01 * m12 - m02 * m11) * invDet,
                    -(m00 * m12 - m02 * m10) * invDet,
                    (m00 * m11 - m01 * m10) * invDet
                )
            );
            return true;
        }

        /// <summary>Calculates the determinant of a Double3x3 matrix.</summary>
        /// <param name="m">The matrix to calculate the determinant of.</param>
        /// <returns>The determinant of the matrix.</returns>
        public static double Determinant(Double3x3 m) =>
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
        public static Double3x3 operator *(Double3x3 a, Double3x3 b) => new Double3x3(
                a.c0 * b.c0.X + a.c1 * b.c0.Y + a.c2 * b.c0.Z,
                a.c0 * b.c1.X + a.c1 * b.c1.Y + a.c2 * b.c1.Z,
                a.c0 * b.c2.X + a.c1 * b.c2.Y + a.c2 * b.c2.Z
            );

        /// <summary>Returns the result of a matrix-vector multiplication.</summary>
        /// <param name="m">The matrix.</param>
        /// <param name="v">The vector.</param>
        /// <returns>The result of m * v.</returns>
        public static Double3 operator *(Double3x3 m, Double3 v) => m.c0 * v.X +
                m.c1 * v.Y +
                m.c2 * v.Z;


        #endregion


        #region Casting

        public static explicit operator Double3x3(Float3x3 m)
        {
            return new Double3x3((Double3)m.c0, (Double3)m.c1, (Double3)m.c2);
        }

        #endregion


        #region Equals and GetHashCode

        public static bool operator ==(Double3x3 lhs, Double3x3 rhs) => lhs.c0 == rhs.c0 && lhs.c1 == rhs.c1 && lhs.c2 == rhs.c2;
        public static bool operator !=(Double3x3 lhs, Double3x3 rhs) => !(lhs == rhs);
        public bool Equals(Double3x3 rhs) => this.c0.Equals(rhs.c0) && this.c1.Equals(rhs.c1) && this.c2.Equals(rhs.c2);
        public override bool Equals(object? o) => o is Double3x3 converted && Equals(converted);
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


        /// <summary>Returns an array of components.</summary>
        public double[] ToArray()
        {
            double[] array = new double[9];
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
            sb.Append("Double3x3(");
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
