using System;
using System.Globalization;
using System.Text;

namespace Prowl.Vector
{
    /// <summary>A 2x2 matrix of floats.</summary>
    [System.Serializable]
    public partial struct Float2x2 : System.IEquatable<Float2x2>, IFormattable
    {
        /// <summary>Float2x2 identity transform.</summary>
        public static readonly Float2x2 Identity = new Float2x2(new Float2(1f, 0f), new Float2(0f, 1f));
        /// <summary>Float2x2 zero value.</summary>
        public static readonly Float2x2 Zero = new Float2x2(Float2.Zero, Float2.Zero);


        /// <summary>Column 0 of the matrix.</summary>
        public Float2 c0;
        /// <summary>Column 1 of the matrix.</summary>
        public Float2 c1;


        #region Properties

        /// <summary>Returns a reference to the Float2 (column) at a specified index.</summary>
        unsafe public ref Float2 this[int index]
        {
            get
            {
                if ((uint)index >= 2)
                    throw new System.ArgumentOutOfRangeException(nameof(index), $"Column index must be between 0 and 1, but was {index}.");

                fixed (Float2* pC0 = &this.c0)
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

        public Float2x2(Float2 col0, Float2 col1)
        {
            this.c0 = col0;
            this.c1 = col1;
        }

        public Float2x2(float m00, float m01, float m10, float m11)
        {
            this.c0 = new Float2(m00, m10);
            this.c1 = new Float2(m01, m11);
        }

        public Float2x2(float v)
        {
            this.c0 = new Float2(v);
            this.c1 = new Float2(v);
        }

        public Float2x2(Double2x2 m)
        {
            this.c0 = new Float2(m.c0);
            this.c1 = new Float2(m.c1);
        }

        #endregion


        #region Public Methods

        public Float2 GetRow0() => new Float2(c0.X, c1.X);
        public void SetRow0(Float2 value)
        {
            c0.X = value.X;
            c1.X = value.Y;
        }

        public Float2 GetRow1() => new Float2(c0.Y, c1.Y);
        public void SetRow1(Float2 value)
        {
            c0.Y = value.X;
            c1.Y = value.Y;
        }

        /// <summary>Calculates the inverse of this matrix.</summary>
        /// <returns>The inverted matrix, or a matrix filled with NaN if inversion fails.</returns>
        public Float2x2 Invert()
        {
            Invert(this, out Float2x2 result);
            return result;
        }

        /// <summary>Calculates the determinant of a Float2x2 matrix.</summary>
        /// <param name="m">The matrix to calculate the determinant of.</param>
        /// <returns>The determinant of the matrix.</returns>
        public static float Determinant(Float2x2 m) =>
            // The matrix is column-major. For [[a,b],[c,d]], the columns are c0=(a,c) and c1=(b,d).
            // The determinant is ad - bc, which corresponds to m.c0.X * m.c1.Y - m.c1.X * m.c0.Y.
            m.c0.X * m.c1.Y - m.c1.X * m.c0.Y;

        #endregion


        #region Static Methods

        /// <summary>
        /// Creates a 2x2 matrix representing a counter-clockwise rotation by an angle in radians.
        /// </summary>
        /// <param name="angle">Rotation angle in radians.</param>
        /// <returns>The 2x2 rotation matrix.</returns>
        public static Float2x2 Rotate(float angle)
        {
            var s = Maths.Sin(angle);
            var c = Maths.Cos(angle);

            return new Float2x2(c, -s, s, c);
        }

        /// <summary>Returns a 2x2 matrix representing a uniform scaling of both axes by s.</summary>
        /// <param name="s">The scaling factor.</param>
        public static Float2x2 Scale(float s) => new Float2x2(s, 0.0f, 0.0f, s);

        /// <summary>Returns a 2x2 matrix representing a non-uniform axis scaling by x and y.</summary>
        /// <param name="x">The x-axis scaling factor.</param>
        /// <param name="y">The y-axis scaling factor.</param>
        public static Float2x2 Scale(float x, float y) => new Float2x2(x, 0.0f, 0.0f, y);

        /// <summary>Returns a 2x2 matrix representing a non-uniform axis scaling by the components of the Float2 vector v.</summary>
        /// <param name="v">The Float2 containing the x and y axis scaling factors.</param>
        public static Float2x2 Scale(Float2 v) => Scale(v.X, v.Y);

        /// <summary>Returns the transpose of a Float2x2 matrix.</summary>
        /// <param name="m">The matrix to transpose.</param>
        /// <returns>The transposed matrix (Float2x2).</returns>
        public static Float2x2 Transpose(Float2x2 m) => new Float2x2(
                new Float2(m.c0.X, m.c1.X),
                new Float2(m.c0.Y, m.c1.Y)
            );

        /// <summary>Transforms a 2D normal vector using the inverse transpose of a 2x2 matrix.</summary>
        /// <param name="normal">The 2D normal vector to transform.</param>
        /// <param name="matrix">The 2x2 transformation matrix.</param>
        /// <returns>The transformed and normalized normal vector.</returns>
        public static Float2 TransformNormal(Float2 normal, Float2x2 matrix)
        {
            // For normals, we need to use the inverse transpose of the matrix
            if (!Invert(matrix, out Float2x2 inverse))
            {
                // Matrix is singular, return the original normal
                return normal;
            }
            Float2x2 invTranspose = Transpose(inverse);
            Float2 transformed = invTranspose * normal;
            return Float2.Normalize(transformed);
        }

        /// <summary>Attempts to calculate the inverse of the given matrix. If successful, result will contain the inverted matrix.</summary>
        /// <param name="matrix">The source matrix to invert.</param>
        /// <param name="result">If successful, contains the inverted matrix.</param>
        /// <returns>True if the source matrix could be inverted; False otherwise.</returns>
        public static bool Invert(Float2x2 matrix, out Float2x2 result)
        {
            var a = matrix.c0.X; var b = matrix.c1.X;
            var c = matrix.c0.Y; var d = matrix.c1.Y;

            var det = a * d - b * c;

            if (MathF.Abs(det) < float.Epsilon)
            {
                result = new Float2x2(
                    new Float2(float.NaN, float.NaN),
                    new Float2(float.NaN, float.NaN)
                );
                return false;
            }

            var invDet = 1f / det;

            result = new Float2x2(
                new Float2(d * invDet, -c * invDet),
                new Float2(-b * invDet, a * invDet)
            );
            return true;
        }

        #endregion


        #region Operators

        /// <summary>
        /// Returns the result of a matrix-matrix multiplication.
        /// </summary>
        /// <returns>Order matters, so the result of A * B is that B is applied first, then A.</returns>
        public static Float2x2 operator *(Float2x2 a, Float2x2 b) => new Float2x2(
                a.c0 * b.c0.X + a.c1 * b.c0.Y,
                a.c0 * b.c1.X + a.c1 * b.c1.Y
            );

        /// <summary>Returns the result of a matrix-vector multiplication.</summary>
        /// <param name="m">The matrix.</param>
        /// <param name="v">The vector.</param>
        /// <returns>The result of m * v.</returns>
        public static Float2 operator *(Float2x2 m, Float2 v) => m.c0 * v.X +
                m.c1 * v.Y;

        #endregion


        #region Casting

        public static explicit operator Float2x2(Double2x2 m)
        {
            return new Float2x2((Float2)m.c0, (Float2)m.c1);
        }

        #endregion


        #region Equals and GetHashCode
        public static bool operator ==(Float2x2 lhs, Float2x2 rhs) => lhs.c0 == rhs.c0 && lhs.c1 == rhs.c1;
        public static bool operator !=(Float2x2 lhs, Float2x2 rhs) => !(lhs == rhs);
        public bool Equals(Float2x2 rhs) => this.c0.Equals(rhs.c0) && this.c1.Equals(rhs.c1);
        public override bool Equals(object? o) => o is Float2x2 converted && Equals(converted);
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


        /// <summary>Returns an array of components.</summary>
        public float[] ToArray()
        {
            float[] array = new float[4];
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
            sb.Append("Float2x2(");
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
