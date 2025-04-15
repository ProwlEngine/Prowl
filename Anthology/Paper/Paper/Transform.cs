using System.Runtime.InteropServices;
using System.Numerics;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Represents a 2D affine transformation matrix in the form:
    /// | A C E |
    /// | B D F |
    /// | 0 0 1 |
    /// Used for transformations in a 2D coordinate system.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Transform
    {
        // Matrix coefficients (using standard matrix notation a-f)
        public float A, B, C, D, E, F;

        // Alternative accessor properties for matrix elements
        public readonly float T1 => A;
        public readonly float T2 => B;
        public readonly float T3 => C;
        public readonly float T4 => D;
        public readonly float T5 => E;
        public readonly float T6 => F;

        /// <summary>
        /// Identity transform (no transformation)
        /// </summary>
        public static Transform Identity => new(1, 0, 0, 1, 0, 0);

        /// <summary>
        /// Creates a new transform with the specified coefficients
        /// </summary>
        public Transform(float a, float b, float c, float d, float e, float f)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            E = e;
            F = f;
        }


        #region Basic Matrix Operations

        /// <summary>Sets all coefficients to zero</summary>
        public void Zero() => A = B = C = D = E = F = 0;

        /// <summary>Copies values from another transform</summary>
        public void Set(Transform src)
        {
            A = src.A;
            B = src.B;
            C = src.C;
            D = src.D;
            E = src.E;
            F = src.F;
        }

        /// <summary>Checks if this transform is the identity matrix</summary>
        public readonly bool IsIdentity() => A == 1 && B == 0 && C == 0 && D == 1 && E == 0 && F == 0;

        /// <summary>Checks if this transform only contains translation (no rotation, scaling, etc.)</summary>
        public readonly bool IsIdentityOrTranslation() => A == 1 && B == 0 && C == 0 && D == 1;

        /// <summary>Calculates the scale factor along the X axis</summary>
        public readonly float XScale() => MathF.Sqrt(A * A + B * B);

        /// <summary>Calculates the scale factor along the Y axis</summary>
        public readonly float YScale() => MathF.Sqrt(C * C + D * D);

        #endregion


        #region Static Factory Methods

        /// <summary>Creates a translation transform</summary>
        public static Transform CreateTranslation(Vector2 translation) => CreateTranslation(translation.X, translation.Y);

        /// <summary>Creates a translation transform with the specified X and Y offsets</summary>
        public static Transform CreateTranslation(float tx, float ty) => new Transform(1.0f, 0.0f, 0.0f, 1.0f, tx, ty);

        /// <summary>Creates a rotation transform with the angle specified in degrees</summary>
        public static Transform CreateRotate(float angleInDegrees) => CreateRotateRadians(angleInDegrees * MathF.PI / 180f);

        /// <summary>Creates a rotation transform around the specified origin point with the angle in degrees</summary>
        public static Transform CreateRotate(float angleInDegrees, Vector2 origin) => CreateRotateRadians(angleInDegrees * MathF.PI / 180f, origin);

        /// <summary>Creates a rotation transform with the angle specified in radians</summary>
        public static Transform CreateRotateRadians(float angleInRadians)
        {
            var cs = MathF.Cos(angleInRadians);
            var sn = MathF.Sin(angleInRadians);
            return new Transform(cs, sn, -sn, cs, 0.0f, 0.0f);
        }

        /// <summary>Creates a rotation transform around the specified origin point with the angle in radians</summary>
        public static Transform CreateRotateRadians(float angleInRadians, Vector2 origin)
        {
            Transform rotate = Identity;
            rotate *= CreateTranslation(-origin.X, -origin.Y);  // Move to origin
            rotate *= CreateRotateRadians(angleInRadians);      // Rotate
            rotate *= CreateTranslation(origin.X, origin.Y);    // Move back
            return rotate;
        }

        /// <summary>Creates a uniform scale transform</summary>
        public static Transform CreateScale(float s) => CreateScale(s, s);

        /// <summary>Creates a non-uniform scale transform with separate X and Y scaling factors</summary>
        public static Transform CreateScale(float sx, float sy) => new Transform(sx, 0.0f, 0.0f, sy, 0.0f, 0.0f);

        /// <summary>Creates a scale transform around the specified origin point</summary>
        public static Transform CreateScale(float sx, float sy, Vector2 origin)
        {
            Transform scale = Identity;
            scale *= CreateTranslation(-origin.X, -origin.Y);  // Move to origin
            scale *= CreateScale(sx, sy);                      // Scale
            scale *= CreateTranslation(origin.X, origin.Y);    // Move back
            return scale;
        }

        /// <summary>Creates a shear transform with X and Y shear angles in radians</summary>
        public static Transform CreateShear(float xRadians, float yRadians)
        {
            var a = 1.0f;
            var b = 0.0f;
            var c = 0.0f;
            var d = 1.0f;

            a += yRadians * c;
            b += yRadians * d;
            c += xRadians * a;
            d += xRadians * b;

            return new Transform(a, b, c, d, 0.0f, 0.0f);
        }

        /// <summary>Creates a shear transform around the specified origin point</summary>
        public static Transform CreateShear(float xRadians, float yRadians, Vector2 origin)
        {
            Transform shear = Identity;
            shear *= CreateTranslation(-origin.X, -origin.Y);  // Move to origin
            shear *= CreateShear(xRadians, yRadians);          // Shear
            shear *= CreateTranslation(origin.X, origin.Y);    // Move back
            return shear;
        }

        /// <summary>Creates a skew transform along the X axis with the angle in degrees</summary>
        public static Transform CreateSkewX(float angleInDegrees) => CreateShear(MathF.Tan(angleInDegrees * MathF.PI / 180f), 0);

        /// <summary>Creates a skew transform along the Y axis with the angle in degrees</summary>
        public static Transform CreateSkewY(float angleInDegrees) => CreateShear(0, MathF.Tan(angleInDegrees * MathF.PI / 180f));

        /// <summary>Creates a skew transform along the X axis around the specified origin point</summary>
        public static Transform CreateSkewX(float angleInDegrees, Vector2 origin) => CreateShear(MathF.Tan(angleInDegrees * MathF.PI / 180f), 0, origin);

        /// <summary>Creates a skew transform along the Y axis around the specified origin point</summary>
        public static Transform CreateSkewY(float angleInDegrees, Vector2 origin) => CreateShear(0, MathF.Tan(angleInDegrees * MathF.PI / 180f), origin);

        #endregion


        #region Matrix Operations

        /// <summary>
        /// Multiplies this transform by another transform (this = this * other)
        /// </summary>
        public Transform Multiply(ref Transform other)
        {
            var t0 = A * other.A + B * other.C;
            var t2 = C * other.A + D * other.C;
            var t4 = E * other.A + F * other.C + other.E;
            B = A * other.B + B * other.D;
            D = C * other.B + D * other.D;
            F = E * other.B + F * other.D + other.F;
            A = t0;
            C = t2;
            E = t4;
            return this;
        }

        /// <summary>
        /// Premultiplies this transform by another transform (this = other * this)
        /// </summary>
        public Transform Premultiply(ref Transform other)
        {
            var s2 = other;
            s2.Multiply(ref this);
            Set(s2);
            return this;
        }

        /// <summary>
        /// Checks if this transform can be inverted
        /// </summary>
        public bool IsInvertible()
        {
            var determinant = A * D - C * B;
            return MathF.Abs(determinant) > 1e-6f;
        }

        /// <summary>
        /// Returns the inverse of this transform
        /// </summary>
        public Transform Inverse()
        {
            // Fast path for identity or translation-only matrices
            if (IsIdentityOrTranslation())
                return CreateTranslation(-E, -F);

            var determinant = A * D - C * B;
            if (MathF.Abs(determinant) <= 1e-6f)
                return Identity; // Not invertible, return identity

            var inverseDeterminant = 1.0f / determinant;
            var result = new Transform();
            result.A = D * inverseDeterminant;
            result.C = -C * inverseDeterminant;
            result.E = (C * F - D * E) * inverseDeterminant;
            result.B = -B * inverseDeterminant;
            result.D = A * inverseDeterminant;
            result.F = (B * E - A * F) * inverseDeterminant;

            return result;
        }

        /// <summary>
        /// Transforms a point using this transform, returning the result via out parameters
        /// </summary>
        public void TransformPoint(out float dx, out float dy, float sx, float sy)
        {
            dx = sx * A + sy * C + E;
            dy = sx * B + sy * D + F;
        }

        /// <summary>
        /// Transforms a Vector2 point using this transform
        /// </summary>
        public Vector2 TransformPoint(Vector2 point) => TransformPoint(point.X, point.Y);

        /// <summary>
        /// Transforms a point specified by x,y coordinates using this transform
        /// </summary>
        public Vector2 TransformPoint(float sx, float sy) => new Vector2(sx * A + sy * C + E, sx * B + sy * D + F);

        /// <summary>
        /// Converts this 2D transform to a 4x4 matrix
        /// </summary>
        public Matrix4x4 ToMatrix4x4()
        {
            var result = Matrix4x4.Identity;

            result.M11 = A;
            result.M12 = B;
            result.M21 = C;
            result.M22 = D;
            result.M41 = E;
            result.M42 = F;

            return result;
        }

        #endregion

        #region Decomposition and Recomposition

        /// <summary>
        /// Structure representing a decomposed transform with individual transformation components
        /// </summary>
        public struct DecomposedType
        {
            public float ScaleX;      // X-axis scaling
            public float ScaleY;      // Y-axis scaling
            public float Angle;       // Rotation angle in radians
            public float RemainderA;  // Any remaining transformation components
            public float RemainderB;
            public float RemainderC;
            public float RemainderD;
            public float TranslateX;  // X translation
            public float TranslateY;  // Y translation
        }

        /// <summary>
        /// Decomposes this transform into its basic components (scale, rotation, translation)
        /// </summary>
        public bool Decompose(out DecomposedType decomp)
        {
            decomp = new DecomposedType();
            var m = new Transform(A, B, C, D, E, F);

            // Compute scaling factors
            float sx = m.XScale();
            float sy = m.YScale();

            // Check for axis flip (negative determinant)
            if (m.A * m.D - m.C * m.B < 0)
            {
                // Flip axis with minimum unit vector dot product
                if (m.A < m.D)
                    sx = -sx;
                else
                    sy = -sy;
            }

            // Remove scale from matrix
            var scale = CreateScale(1 / sx, 1 / sy);
            m.Multiply(ref scale);

            // Compute rotation angle
            float angle = MathF.Atan2(m.B, m.A);

            // Remove rotation from matrix
            var rot = CreateRotateRadians(-angle);
            m *= rot;

            // Store decomposition results
            decomp.ScaleX = sx;
            decomp.ScaleY = sy;
            decomp.Angle = angle;
            decomp.RemainderA = m.A;
            decomp.RemainderB = m.B;
            decomp.RemainderC = m.C;
            decomp.RemainderD = m.D;
            decomp.TranslateX = m.E;
            decomp.TranslateY = m.F;

            return true;
        }

        /// <summary>
        /// Reconstructs a transform from decomposed components
        /// </summary>
        public void Recompose(DecomposedType decomp)
        {
            // Start with remainders (should be near identity)
            A = decomp.RemainderA;
            B = decomp.RemainderB;
            C = decomp.RemainderC;
            D = decomp.RemainderD;
            E = decomp.TranslateX;
            F = decomp.TranslateY;

            // Apply rotation
            var rot = CreateRotateRadians(decomp.Angle);
            Multiply(ref rot);

            // Apply scaling
            var scale = CreateScale(decomp.ScaleX, decomp.ScaleY);
            Multiply(ref scale);
        }

        #endregion

        #region Interpolation

        /// <summary>
        /// Interpolates between two transforms
        /// </summary>
        /// <param name="from">Starting transform</param>
        /// <param name="to">Target transform</param>
        /// <param name="progress">Interpolation factor (0.0 to 1.0)</param>
        public static Transform Interpolate(Transform from, Transform to, float progress)
        {
            // Clamp progress to [0,1] range to ensure valid interpolation
            progress = MathF.Max(0, MathF.Min(1, progress));

            // Decompose both transforms
            from.Decompose(out var srA);
            to.Decompose(out var srB);

            // Normalize flipped axes to ensure proper interpolation
            if (srA.ScaleX < 0 && srB.ScaleY < 0 || srA.ScaleY < 0 && srB.ScaleX < 0)
            {
                srA.ScaleX = -srA.ScaleX;
                srA.ScaleY = -srA.ScaleY;
                srA.Angle += srA.Angle < 0 ? MathF.PI * 2 : -MathF.PI * 2;
            }

            // Normalize angles to avoid rotating the long way around
            const float twoPi = MathF.PI * 2;
            srA.Angle = srA.Angle % twoPi;
            srB.Angle = srB.Angle % twoPi;

            if (MathF.Abs(srA.Angle - srB.Angle) > MathF.PI)
            {
                if (srA.Angle > srB.Angle)
                    srA.Angle -= twoPi;
                else
                    srB.Angle -= twoPi;
            }

            // Linear interpolation of all components
            var result = new DecomposedType {
                ScaleX = srA.ScaleX + progress * (srB.ScaleX - srA.ScaleX),
                ScaleY = srA.ScaleY + progress * (srB.ScaleY - srA.ScaleY),
                Angle = srA.Angle + progress * (srB.Angle - srA.Angle),
                RemainderA = srA.RemainderA + progress * (srB.RemainderA - srA.RemainderA),
                RemainderB = srA.RemainderB + progress * (srB.RemainderB - srA.RemainderB),
                RemainderC = srA.RemainderC + progress * (srB.RemainderC - srA.RemainderC),
                RemainderD = srA.RemainderD + progress * (srB.RemainderD - srA.RemainderD),
                TranslateX = srA.TranslateX + progress * (srB.TranslateX - srA.TranslateX),
                TranslateY = srA.TranslateY + progress * (srB.TranslateY - srA.TranslateY)
            };

            // Reconstruct the interpolated transform
            var newTransform = new Transform();
            newTransform.Recompose(result);
            return newTransform;
        }

        #endregion

        #region Operators

        /// <summary>
        /// Multiplies two transforms together
        /// </summary>
        public static Transform operator *(Transform left, Transform right)
        {
            var result = left;
            result.Multiply(ref right);
            return result;
        }

        /// <summary>
        /// Equality operator
        /// </summary>
        public static bool operator ==(Transform left, Transform right)
        {
            return left.A == right.A
                && left.B == right.B
                && left.C == right.C
                && left.D == right.D
                && left.E == right.E
                && left.F == right.F;
        }

        /// <summary>
        /// Inequality operator
        /// </summary>
        public static bool operator !=(Transform left, Transform right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Transforms a Vector2 by this transform
        /// </summary>
        public static Vector2 operator *(Transform transform, Vector2 point)
        {
            return transform.TransformPoint(point);
        }

        /// <summary>
        /// Linear interpolation between two transforms
        /// </summary>
        public static Transform Lerp(Transform start, Transform end, float amount)
        {
            return Interpolate(start, end, amount);
        }

        /// <summary>
        /// Transforms a point (implicit conversion from tuple)
        /// </summary>
        public static Vector2 operator *((float X, float Y) point, Transform transform)
        {
            return transform.TransformPoint(new Vector2(point.X, point.Y));
        }

        /// <summary>
        /// Transforms a point
        /// </summary>
        public static Vector2 operator *(Vector2 point, Transform transform)
        {
            return transform.TransformPoint(point);
        }

        #endregion

        #region Object Overrides

        /// <summary>
        /// Determines whether the specified object is equal to the current object
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is Transform transform)
                return this == transform;
            return false;
        }

        /// <summary>
        /// Returns the hash code for this instance
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(A, B, C, D, E, F);
        }

        /// <summary>
        /// Returns a string that represents the current object
        /// </summary>
        public override string ToString()
        {
            if (IsIdentity())
                return "Identity";
            return $"{{A={A}, B={B}, C={C}, D={D}, E={E}, F={F}}}";
        }

        /// <summary>
        /// Compares two Transform objects for equality
        /// </summary>
        public bool Equals(Transform other)
        {
            return this == other;
        }

        #endregion
    }
}