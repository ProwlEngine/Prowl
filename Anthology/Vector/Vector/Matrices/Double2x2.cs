using System;
using System.Globalization;
using System.Text;

namespace Prowl.Vector
{
    /// <summary>A 2x2 matrix of doubles.</summary>
    [System.Serializable]
    public partial struct Double2x2 : System.IEquatable<Double2x2>, IFormattable
    {
        /// <summary>Double2x2 identity transform.</summary>
        public static readonly Double2x2 Identity = new Double2x2(new Double2(1.0, 0.0), new Double2(0.0, 1.0));

        /// <summary>Double2x2 zero value.</summary>
        public static readonly Double2x2 Zero = new Double2x2(Double2.Zero, Double2.Zero);


        /// <summary>Column 0 of the matrix.</summary>
        public Double2 c0;
        /// <summary>Column 1 of the matrix.</summary>
        public Double2 c1;


        #region Properties

        /// <summary>Returns a reference to the Double2 (column) at a specified index.</summary>
        unsafe public ref Double2 this[int index]
        {
            get
            {
                if ((uint)index >= 2)
                    throw new System.ArgumentOutOfRangeException(nameof(index), $"Column index must be between 0 and 1, but was {index}.");

                fixed (Double2* pC0 = &this.c0)
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
                if ((uint)column >= 2)
                    throw new System.ArgumentOutOfRangeException(nameof(column));
                return this[column][row];
            }
            set
            {
                if ((uint)column >= 2)
                    throw new System.ArgumentOutOfRangeException(nameof(column));
                var temp = this[column];
                temp[row] = value;
                this[column] = temp;
            }
        }

        #endregion


        #region Constructors

        public Double2x2(Double2 col0, Double2 col1)
        {
            this.c0 = col0;
            this.c1 = col1;
        }

        
        public Double2x2(double m00, double m01, double m10, double m11)
        {
            this.c0 = new Double2(m00, m10);
            this.c1 = new Double2(m01, m11);
        }

        
        public Double2x2(double v)
        {
            this.c0 = new Double2(v);
            this.c1 = new Double2(v);
        }

        
        public Double2x2(Float2x2 m)
        {
            this.c0 = new Double2(m.c0);
            this.c1 = new Double2(m.c1);
        }

        #endregion


        #region Public Methods

        public Double2 GetRow0() => new Double2(c0.X, c1.X);
        public void SetRow0(Double2 value)
        {
            c0.X = value.X;
            c1.X = value.Y;
        }

        public Double2 GetRow1() => new Double2(c0.Y, c1.Y);
        public void SetRow1(Double2 value)
        {
            c0.Y = value.X;
            c1.Y = value.Y;
        }

        /// <summary>Calculates the inverse of this matrix.</summary>
        /// <returns>The inverted matrix, or a matrix filled with NaN if inversion fails.</returns>
        public Double2x2 Invert()
        {
            Invert(this, out Double2x2 result);
            return result;
        }

        #endregion


        #region Static Methods

        /// <summary>
        /// Creates a 2x2 matrix representing a counter-clockwise rotation by an angle in radians.
        /// </summary>
        /// <param name="angle">Rotation angle in radians.</param>
        /// <returns>The 2x2 rotation matrix.</returns>
        public static Double2x2 Rotate(double angle)
        {
            double s = Math.Sin(angle);
            double c = Math.Cos(angle);
            return new Double2x2(c, -s, s, c);
        }

        /// <summary>Returns a 2x2 matrix representing a uniform scaling of both axes by s.</summary>
        /// <param name="s">The scaling factor.</param>
        public static Double2x2 Scale(double s) => new Double2x2(s, 0.0, 0.0, s);

        /// <summary>Returns a 2x2 matrix representing a non-uniform axis scaling by x and y.</summary>
        /// <param name="x">The x-axis scaling factor.</param>
        /// <param name="y">The y-axis scaling factor.</param>
        public static Double2x2 Scale(double x, double y) => new Double2x2(x, 0.0, 0.0, y);

        /// <summary>Returns a 2x2 matrix representing a non-uniform axis scaling by the components of the Double2 vector v.</summary>
        /// <param name="v">The Double2 containing the x and y axis scaling factors.</param>
        public static Double2x2 Scale(Double2 v) => Scale(v.X, v.Y);

        /// <summary>Returns the transpose of a Double2x2 matrix.</summary>
        /// <param name="m">The matrix to transpose.</param>
        /// <returns>The transposed matrix (Double2x2).</returns>
        public static Double2x2 Transpose(Double2x2 m) => new Double2x2(
                new Double2(m.c0.X, m.c1.X),
                new Double2(m.c0.Y, m.c1.Y)
            );

        /// <summary>Transforms a 2D normal vector using the inverse transpose of a 2x2 matrix.</summary>
        /// <param name="normal">The 2D normal vector to transform.</param>
        /// <param name="matrix">The 2x2 transformation matrix.</param>
        /// <returns>The transformed and normalized normal vector.</returns>
        public static Double2 TransformNormal(Double2 normal, Double2x2 matrix)
        {
            // For normals, we need to use the inverse transpose of the matrix
            if (!Invert(matrix, out Double2x2 inverse))
            {
                // Matrix is singular, return the original normal
                return normal;
            }
            Double2x2 invTranspose = Transpose(inverse);
            Double2 transformed = invTranspose * normal;
            return Double2.Normalize(transformed);
        }

        /// <summary>Attempts to calculate the inverse of the given matrix. If successful, result will contain the inverted matrix.</summary>
        /// <param name="matrix">The source matrix to invert.</param>
        /// <param name="result">If successful, contains the inverted matrix.</param>
        /// <returns>True if the source matrix could be inverted; False otherwise.</returns>
        public static bool Invert(Double2x2 matrix, out Double2x2 result)
        {
            var a = matrix.c0.X; var b = matrix.c1.X;
            var c = matrix.c0.Y; var d = matrix.c1.Y;

            var det = a * d - b * c;

            if (Math.Abs(det) < double.Epsilon)
            {
                result = new Double2x2(
                    new Double2(double.NaN, double.NaN),
                    new Double2(double.NaN, double.NaN)
                );
                return false;
            }

            var invDet = 1.0 / det;

            result = new Double2x2(
                new Double2(d * invDet, -c * invDet),
                new Double2(-b * invDet, a * invDet)
            );
            return true;
        }

        /// <summary>Calculates the determinant of a Double2x2 matrix.</summary>
        /// <param name="m">The matrix to calculate the determinant of.</param>
        /// <returns>The determinant of the matrix.</returns>
        public static double Determinant(Double2x2 m) =>
            // The matrix is column-major. For [[a,b],[c,d]], the columns are c0=(a,c) and c1=(b,d).
            // The determinant is ad - bc, which corresponds to m.c0.X * m.c1.Y - m.c1.X * m.c0.Y.
            m.c0.X * m.c1.Y - m.c1.X * m.c0.Y;

        #endregion


        #region Operators

        /// <summary>
        /// Returns the result of a matrix-matrix multiplication.
        /// </summary>
        /// <returns>Order matters, so the result of A * B is that B is applied first, then A.</returns>
        public static Double2x2 operator *(Double2x2 a, Double2x2 b) => new Double2x2(
                a.c0 * b.c0.X + a.c1 * b.c0.Y,
                a.c0 * b.c1.X + a.c1 * b.c1.Y
            );

        /// <summary>Returns the result of a matrix-vector multiplication.</summary>
        /// <param name="m">The matrix.</param>
        /// <param name="v">The vector.</param>
        /// <returns>The result of m * v.</returns>
        public static Double2 operator *(Double2x2 m, Double2 v) => m.c0 * v.X +
                m.c1 * v.Y;

        #endregion


        #region Casting

        public static explicit operator Double2x2(Float2x2 m)
        {
            return new Double2x2((Double2)m.c0, (Double2)m.c1);
        }

        #endregion


        #region Equals and GetHashCode

        public static bool operator ==(Double2x2 lhs, Double2x2 rhs) => lhs.c0 == rhs.c0 && lhs.c1 == rhs.c1;
        public static bool operator !=(Double2x2 lhs, Double2x2 rhs) => !(lhs == rhs);
        public bool Equals(Double2x2 rhs) => this.c0.Equals(rhs.c0) && this.c1.Equals(rhs.c1);
        public override bool Equals(object? o) => o is Double2x2 converted && Equals(converted);
        public override int GetHashCode()
        {
            unchecked // Overflow is fine, just wrap
            {
                int hash = 17;
                hash = hash * 23 + c0.GetHashCode();
                hash = hash * 23 + c1.GetHashCode();
                return hash;
            }
        }

        #endregion


        public double[] ToArray()
        {
            double[] array = new double[4];
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    array[i * 2 + j] = this[i, j];
            return array;
        }

        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Double2x2(");
            sb.Append(this.c0.X.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c1.X.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append("  ");
            sb.Append(this.c0.Y.ToString(format, formatProvider));
            sb.Append(", ");
            sb.Append(this.c1.Y.ToString(format, formatProvider));
            sb.Append(")");
            return sb.ToString();
        }

    }
}
