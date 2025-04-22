// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace Prowl.Vector;

/// <summary>
/// Represents a 2D affine transformation matrix in the form:
/// | A C E |
/// | B D F |
/// | 0 0 1 |
/// Used for transformations in a 2D coordinate system.
/// </summary>
/// <remarks>
/// Creates a new transform with the specified coefficients
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Transform2D
{
    // Matrix coefficients (using standard matrix notation a-f)
    public double A, B, C, D, E, F;

    // Alternative accessor properties for matrix elements
    public readonly double T1 => A;
    public readonly double T2 => B;
    public readonly double T3 => C;
    public readonly double T4 => D;
    public readonly double T5 => E;
    public readonly double T6 => F;

    /// <summary>
    /// Identity transform (no transformation)
    /// </summary>
    public static Transform2D Identity => new(1, 0, 0, 1, 0, 0);

    public Transform2D(double a, double b, double c, double d, double e, double f)
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
    public void Set(Transform2D src)
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
    public readonly double XScale() => Math.Sqrt(A * A + B * B);

    /// <summary>Calculates the scale factor along the Y axis</summary>
    public readonly double YScale() => Math.Sqrt(C * C + D * D);

    #endregion


    #region Static Factory Methods

    /// <summary>Creates a translation transform</summary>
    public static Transform2D CreateTranslation(Vector2 translation) => CreateTranslation(translation.x, translation.y);

    /// <summary>Creates a translation transform with the specified X and Y offsets</summary>
    public static Transform2D CreateTranslation(double tx, double ty) => new Transform2D(1.0f, 0.0f, 0.0f, 1.0f, tx, ty);

    /// <summary>Creates a rotation transform with the angle specified in degrees</summary>
    public static Transform2D CreateRotate(double angleInDegrees) => CreateRotateRadians(angleInDegrees * Math.PI / 180f);

    /// <summary>Creates a rotation transform around the specified origin point with the angle in degrees</summary>
    public static Transform2D CreateRotate(double angleInDegrees, Vector2 origin) => CreateRotateRadians(angleInDegrees * Math.PI / 180f, origin);

    /// <summary>Creates a rotation transform with the angle specified in radians</summary>
    public static Transform2D CreateRotateRadians(double angleInRadians)
    {
        var cs = Math.Cos(angleInRadians);
        var sn = Math.Sin(angleInRadians);
        return new Transform2D(cs, sn, -sn, cs, 0.0f, 0.0f);
    }

    /// <summary>Creates a rotation transform around the specified origin point with the angle in radians</summary>
    public static Transform2D CreateRotateRadians(double angleInRadians, Vector2 origin)
    {
        Transform2D rotate = Identity;
        rotate *= CreateTranslation(-origin.x, -origin.y);  // Move to origin
        rotate *= CreateRotateRadians(angleInRadians);      // Rotate
        rotate *= CreateTranslation(origin.x, origin.y);    // Move back
        return rotate;
    }

    /// <summary>Creates a uniform scale transform</summary>
    public static Transform2D CreateScale(double s) => CreateScale(s, s);

    /// <summary>Creates a non-uniform scale transform with separate X and Y scaling factors</summary>
    public static Transform2D CreateScale(double sx, double sy) => new Transform2D(sx, 0.0f, 0.0f, sy, 0.0f, 0.0f);

    /// <summary>Creates a scale transform around the specified origin point</summary>
    public static Transform2D CreateScale(double sx, double sy, Vector2 origin)
    {
        Transform2D scale = Identity;
        scale *= CreateTranslation(-origin.x, -origin.y);  // Move to origin
        scale *= CreateScale(sx, sy);                      // Scale
        scale *= CreateTranslation(origin.x, origin.y);    // Move back
        return scale;
    }

    /// <summary>Creates a shear transform with X and Y shear angles in radians</summary>
    public static Transform2D CreateShear(double xRadians, double yRadians)
    {
        var a = 1.0;
        var b = 0.0;
        var c = 0.0;
        var d = 1.0;

        a += yRadians * c;
        b += yRadians * d;
        c += xRadians * a;
        d += xRadians * b;

        return new Transform2D(a, b, c, d, 0.0, 0.0);
    }

    /// <summary>Creates a shear transform around the specified origin point</summary>
    public static Transform2D CreateShear(double xRadians, double yRadians, Vector2 origin)
    {
        Transform2D shear = Identity;
        shear *= CreateTranslation(-origin.x, -origin.y);  // Move to origin
        shear *= CreateShear(xRadians, yRadians);          // Shear
        shear *= CreateTranslation(origin.x, origin.y);    // Move back
        return shear;
    }

    /// <summary>Creates a skew transform along the X axis with the angle in degrees</summary>
    public static Transform2D CreateSkewX(double angleInDegrees) => CreateShear(Math.Tan(angleInDegrees * Math.PI / 180f), 0);

    /// <summary>Creates a skew transform along the Y axis with the angle in degrees</summary>
    public static Transform2D CreateSkewY(double angleInDegrees) => CreateShear(0, Math.Tan(angleInDegrees * Math.PI / 180f));

    /// <summary>Creates a skew transform along the X axis around the specified origin point</summary>
    public static Transform2D CreateSkewX(double angleInDegrees, Vector2 origin) => CreateShear(Math.Tan(angleInDegrees * Math.PI / 180f), 0, origin);

    /// <summary>Creates a skew transform along the Y axis around the specified origin point</summary>
    public static Transform2D CreateSkewY(double angleInDegrees, Vector2 origin) => CreateShear(0, Math.Tan(angleInDegrees * Math.PI / 180f), origin);

    #endregion


    #region Matrix Operations

    /// <summary> Multiplies this transform by another transform (this = this * other) </summary>
    public Transform2D Multiply(ref Transform2D other)
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

    /// <summary> Premultiplies this transform by another transform (this = other * this) </summary>
    public Transform2D Premultiply(ref Transform2D other)
    {
        var s2 = other;
        s2.Multiply(ref this);
        Set(s2);
        return this;
    }

    /// <summary> Checks if this transform can be inverted </summary>
    public bool IsInvertible() => Math.Abs(A * D - C * B) > 1e-6f;

    /// <summary> Returns the inverse of this transform </summary>
    public Transform2D Inverse()
    {
        // Fast path for identity or translation-only matrices
        if (IsIdentityOrTranslation())
            return CreateTranslation(-E, -F);

        var determinant = A * D - C * B;
        if (Math.Abs(determinant) <= 1e-6f)
            return Identity; // Not invertible, return identity

        var inverseDeterminant = 1.0f / determinant;
        var result = new Transform2D();
        result.A = D * inverseDeterminant;
        result.C = -C * inverseDeterminant;
        result.E = (C * F - D * E) * inverseDeterminant;
        result.B = -B * inverseDeterminant;
        result.D = A * inverseDeterminant;
        result.F = (B * E - A * F) * inverseDeterminant;

        return result;
    }

    /// <summary> Transforms a point using this transform, returning the result via out parameters </summary>
    public void TransformPoint(out double dx, out double dy, double sx, double sy)
    {
        dx = sx * A + sy * C + E;
        dy = sx * B + sy * D + F;
    }

    /// <summary> Transforms a Vector2 point using this transform </summary>
    public Vector2 TransformPoint(Vector2 point) => TransformPoint(point.x, point.y);

    /// <summary> Transforms a point specified by x,y coordinates using this transform </summary>
    public Vector2 TransformPoint(double sx, double sy) => new Vector2(sx * A + sy * C + E, sx * B + sy * D + F);

    /// <summary> Converts this 2D transform to a 4x4 matrix </summary>
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

    /// <summary> Structure representing a decomposed transform with individual transformation components </summary>
    public struct DecomposedType
    {
        public double ScaleX;      // X-axis scaling
        public double ScaleY;      // Y-axis scaling
        public double Angle;       // Rotation angle in radians
        public double RemainderA;  // Any remaining transformation components
        public double RemainderB;
        public double RemainderC;
        public double RemainderD;
        public double TranslateX;  // X translation
        public double TranslateY;  // Y translation
    }

    /// <summary> Decomposes this transform into its basic components (scale, rotation, translation) </summary>
    public bool Decompose(out DecomposedType decomp)
    {
        decomp = new DecomposedType();
        var m = new Transform2D(A, B, C, D, E, F);

        // Compute scaling factors
        double sx = m.XScale();
        double sy = m.YScale();

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
        double angle = Math.Atan2(m.B, m.A);

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

    /// <summary> Reconstructs a transform from decomposed components </summary>
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

    /// <summary> Interpolates between two transforms </summary>
    public static Transform2D Interpolate(Transform2D from, Transform2D to, double progress)
    {
        // Clamp progress to [0,1] range to ensure valid interpolation
        progress = Math.Max(0, Math.Min(1, progress));

        // Decompose both transforms
        from.Decompose(out var srA);
        to.Decompose(out var srB);

        // Normalize flipped axes to ensure proper interpolation
        if (srA.ScaleX < 0 && srB.ScaleY < 0 || srA.ScaleY < 0 && srB.ScaleX < 0)
        {
            srA.ScaleX = -srA.ScaleX;
            srA.ScaleY = -srA.ScaleY;
            srA.Angle += srA.Angle < 0 ? Math.PI * 2 : -Math.PI * 2;
        }

        // Normalize angles to avoid rotating the long way around
        const double twoPi = Math.PI * 2;
        srA.Angle = srA.Angle % twoPi;
        srB.Angle = srB.Angle % twoPi;

        if (Math.Abs(srA.Angle - srB.Angle) > Math.PI)
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
        var newTransform = new Transform2D();
        newTransform.Recompose(result);
        return newTransform;
    }

    #endregion

    #region Operators

    /// <summary> Multiplies two transforms together </summary>
    public static Transform2D operator *(Transform2D left, Transform2D right)
    {
        var result = left;
        result.Multiply(ref right);
        return result;
    }

    /// <summary> Equality operator </summary>
    public static bool operator ==(Transform2D left, Transform2D right)
    {
        return left.A == right.A
            && left.B == right.B
            && left.C == right.C
            && left.D == right.D
            && left.E == right.E
            && left.F == right.F;
    }

    /// <summary> Inequality operator </summary>
    public static bool operator !=(Transform2D left, Transform2D right) => !(left == right);

    /// <summary> Transforms a Vector2 by this transform </summary>
    public static Vector2 operator *(Transform2D transform, Vector2 point) => transform.TransformPoint(point);

    /// <summary> Linear interpolation between two transforms </summary>
    public static Transform2D Lerp(Transform2D start, Transform2D end, double amount) => Interpolate(start, end, amount);

    /// <summary> Transforms a point (implicit conversion from tuple) </summary>
    public static Vector2 operator *((double X, double Y) point, Transform2D transform) => transform.TransformPoint(new Vector2(point.X, point.Y));

    /// <summary> Transforms a point </summary>
    public static Vector2 operator *(Vector2 point, Transform2D transform) => transform.TransformPoint(point);

    #endregion

    #region Object Overrides

    /// <summary> Determines whether the specified object is equal to the current object </summary>
    public override bool Equals(object? obj)
    {
        if (obj is Transform2D transform)
            return this == transform;
        return false;
    }

    /// <summary> Returns the hash code for this instance </summary>
    public override int GetHashCode() => HashCode.Combine(A, B, C, D, E, F);

    /// <summary> Returns a string that represents the current object </summary>
    public override string ToString()
    {
        if (IsIdentity())
            return "Identity";
        return $"{{A={A}, B={B}, C={C}, D={D}, E={E}, F={F}}}";
    }

    /// <summary> Compares two Transform objects for equality </summary>
    public bool Equals(Transform2D other) => this == other;

    #endregion
}
