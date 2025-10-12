// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{

    /// <summary>
    /// Represents a quaternion used for 3D rotations.
    /// Components are ordered X, Y, Z, W.
    /// </summary>
    [System.Serializable]
    public struct Quaternion : IEquatable<Quaternion>, IFormattable
    {
        /// <summary>A quaternion representing the identity transform (no rotation).</summary>
        public static readonly Quaternion Identity = new Quaternion(0.0, 0.0, 0.0, 1.0);

        /// <summary>The X component of the quaternion.</summary>
        public double X;
        /// <summary>The Y component of the quaternion.</summary>
        public double Y;
        /// <summary>The Z component of the quaternion.</summary>
        public double Z;
        /// <summary>The W component of the quaternion (scalar part).</summary>
        public double W;

        #region Properties

        /// <summary>
        /// Gets or sets the rotation as Euler angles in degrees, using the ZXYr order.
        /// This is useful for editor inspectors and simple rotation control.
        /// </summary>
        public Double3 EulerAngles
        {
            get => ToEuler(this);
            set => this = FromEuler(value);
        }

        /// <summary>Gets or sets the component at the specified index (0=X, 1=Y, 2=Z, 3=W).</summary>
        public double this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                switch (index)
                {
                    case 0: return X;
                    case 1: return Y;
                    case 2: return Z;
                    case 3: return W;
                    default: throw new IndexOutOfRangeException("Quaternion index out of range.");
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                switch (index)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    case 2: Z = value; break;
                    case 3: W = value; break;
                    default: throw new IndexOutOfRangeException("Quaternion index out of range.");
                }
            }
        }

        #endregion

        #region Constructors

        /// <summary>Constructs a quaternion from four double values.</summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <param name="z">The Z component.</param>
        /// <param name="w">The W component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>Constructs a quaternion from a Double4 vector (xyzw).</summary>
        /// <param name="value">The Double4 vector containing xyzw components.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(Double4 value)
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
            W = value.W;
        }

        /// <summary>Constructs a quaternion from a vector part (Double3) and a scalar part (double).</summary>
        /// <param name="vectorPart">The vector part (xyz).</param>
        /// <param name="scalarPart">The scalar part (w).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(Double3 vectorPart, double scalarPart)
        {
            X = vectorPart.X;
            Y = vectorPart.Y;
            Z = vectorPart.Z;
            W = scalarPart;
        }

        #endregion

        #region Static Methods

        /// <summary>
        /// Creates a quaternion from a 4x4 rotation matrix.
        /// The matrix must be orthonormal in its upper-left 3x3 part.
        /// </summary>
        public static Quaternion FromMatrix(Double4x4 m) => FromMatrix(new Double3x3(m.c0.XYZ, m.c1.XYZ, m.c2.XYZ));

        /// <summary>
        /// Creates a quaternion from a 3x3 rotation matrix.
        /// The matrix must be orthonormal (a pure rotation matrix).
        /// </summary>
        public static Quaternion FromMatrix(Double3x3 m)
        {
            double trace = m.c0.X + m.c1.Y + m.c2.Z;
            double x, y, z, w;

            if (trace > 0.0)
            {
                double s = Maths.Sqrt(trace + 1.0) * 2.0;
                w = 0.25 * s;
                x = (m.c1.Z - m.c2.Y) / s;
                y = (m.c2.X - m.c0.Z) / s;
                z = (m.c0.Y - m.c1.X) / s;
            }
            else if ((m.c0.X > m.c1.Y) && (m.c0.X > m.c2.Z))
            {
                double s = Maths.Sqrt(1.0 + m.c0.X - m.c1.Y - m.c2.Z) * 2.0;
                w = (m.c1.Z - m.c2.Y) / s;
                x = 0.25 * s;
                y = (m.c0.Y + m.c1.X) / s;
                z = (m.c0.Z + m.c2.X) / s;
            }
            else if (m.c1.Y > m.c2.Z)
            {
                double s = Maths.Sqrt(1.0 + m.c1.Y - m.c0.X - m.c2.Z) * 2.0;
                w = (m.c2.X - m.c0.Z) / s;
                x = (m.c0.Y + m.c1.X) / s;
                y = 0.25 * s;
                z = (m.c2.Y + m.c1.Z) / s;
            }
            else
            {
                double s = Maths.Sqrt(1.0f + m.c2.Z - m.c0.X - m.c1.Y) * 2.0f;
                w = (m.c0.Y - m.c1.X) / s;
                x = (m.c2.X + m.c0.Z) / s;
                y = (m.c2.Y + m.c1.Z) / s;
                z = 0.25 * s;
            }
            return new Quaternion(x, y, z, w);
        }

        public static Quaternion FromEuler(Double3 euler) => FromEuler(euler.X, euler.Y, euler.Z);
        public static Quaternion FromEuler(double x, double y, double z)
        {
            double yawOver2 = Maths.Deg2Rad * x * 0.5;
            double pitchOver2 = Maths.Deg2Rad * y * 0.5;
            double rollOver2 = Maths.Deg2Rad * z * 0.5;

            double cosYawOver2 = Maths.Cos(yawOver2);
            double sinYawOver2 = Maths.Sin(yawOver2);
            double cosPitchOver2 = Maths.Cos(pitchOver2);
            double sinPitchOver2 = Maths.Sin(pitchOver2);
            double cosRollOver2 = Maths.Cos(rollOver2);
            double sinRollOver2 = Maths.Sin(rollOver2);

            Quaternion result = new Quaternion();
            result.W = cosYawOver2 * cosPitchOver2 * cosRollOver2 + sinYawOver2 * sinPitchOver2 * sinRollOver2;
            result.X = sinYawOver2 * cosPitchOver2 * cosRollOver2 + cosYawOver2 * sinPitchOver2 * sinRollOver2;
            result.Y = cosYawOver2 * sinPitchOver2 * cosRollOver2 - sinYawOver2 * cosPitchOver2 * sinRollOver2;
            result.Z = cosYawOver2 * cosPitchOver2 * sinRollOver2 - sinYawOver2 * sinPitchOver2 * cosRollOver2;

            return result;
        }

        public static Double3 ToEuler(Quaternion q)
        {
            double CUTOFF = (1.0 - 2.0 * double.Epsilon) * (1.0 - 2.0 * double.Epsilon);

            Double4 qv = new Double4(q.X, q.Y, q.Z, q.W);

            Double4 d1 = qv * (qv.W * 2.0);
            Double4 d2 = new Double4(qv.X * qv.Y * 2.0, qv.Y * qv.Z * 2.0, qv.Z * qv.X * 2.0, qv.W * qv.W * 2.0);
            Double4 d3 = new Double4(qv.X * qv.X, qv.Y * qv.Y, qv.Z * qv.Z, qv.W * qv.W);

            Double3 euler = Double3.Zero;
            double y1 = d2.Y - d1.X;

            if (y1 * y1 < CUTOFF)
            {
                double x1 = d2.X + d1.Z;
                double x2 = d3.Y + d3.W - d3.X - d3.Z;
                double z1 = d2.Z + d1.Y;
                double z2 = d3.Z + d3.W - d3.X - d3.Y;

                euler = new Double3(
                    Maths.Atan2(x1, x2),
                    -Maths.Asin(y1),
                    Maths.Atan2(z1, z2)
                );
            }
            else
            {
                y1 = Maths.Clamp(y1, -1.0, 1.0);

                Double4 abcd = new Double4(d2.Z, d1.Y, d2.Y, d1.X);
                double x1 = 2.0 * (abcd.X * abcd.W + abcd.Y * abcd.Z);

                Double4 x = new Double4(
                    abcd.X * abcd.X * -1.0,
                    abcd.Y * abcd.Y * 1.0,
                    abcd.Z * abcd.Z * -1.0,
                    abcd.W * abcd.W * 1.0
                );

                double x2 = (x.X + x.Y) + (x.Z + x.W);
                euler = new Double3(
                    Maths.Atan2(x1, x2),
                    -Maths.Asin(y1),
                    0.0
                );
            }

            // Convert from radians to degrees
            euler *= (double)Maths.Rad2Deg;

            // Reorder YZX
            euler = new Double3(euler.Y, euler.Z, euler.X);

            euler.X = NormalizeAngle(euler.X);
            euler.Y = NormalizeAngle(euler.Y);
            euler.Z = NormalizeAngle(euler.Z);

            return euler;
        }

        private static double NormalizeAngle(double angle)
        {
            // Unity-style normalization
            angle %= 360;
            if (angle < 0)
                angle += 360;
            return angle;
        }

        /// <summary>Creates a quaternion representing a rotation around an axis by an angle.</summary>
        /// <param name="angle">The angle of rotation in radians.</param>
        /// <param name="axis">The axis of rotation (must be normalized).</param>
        public static Quaternion AngleAxis(double angle, Double3 axis) => AxisAngle(axis, angle);

        /// <summary>Creates a quaternion representing a rotation around an axis by an angle.</summary>
        /// <param name="axis">The axis of rotation (must be normalized).</param>
        /// <param name="angle">The angle of rotation in radians.</param>
        public static Quaternion AxisAngle(Double3 axis, double angle)
        {
            double halfAngle = angle * 0.5;
            //Maths.Sincos(halfAngle, out double s, out double c);
            double s = Maths.Sin(halfAngle);
            double c = Maths.Cos(halfAngle);
            return new Quaternion(axis.X * s, axis.Y * s, axis.Z * s, c);
        }

        /// <summary>Returns the conjugate of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Conjugate(Quaternion q)
        {
            return new Quaternion(-q.X, -q.Y, -q.Z, q.W);
        }

        /// <summary>Returns the inverse of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Inverse(Quaternion q)
        {
            double lengthSq = LengthSquared(q);
            if (lengthSq <= double.Epsilon) // Should not happen with valid rotations
                return Identity;
            double invLengthSq = 1.0f / lengthSq;
            return new Quaternion(
                -q.X * invLengthSq,
                -q.Y * invLengthSq,
                -q.Z * invLengthSq,
                 q.W * invLengthSq
            );
        }

        /// <summary>Returns the dot product of two quaternions.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot(Quaternion a, Quaternion b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        }

        /// <summary>Returns the length (magnitude) of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Length(Quaternion q)
        {
            return Maths.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        }

        /// <summary>Returns the squared length (magnitude squared) of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double LengthSquared(Quaternion q)
        {
            return q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
        }

        /// <summary>Returns a normalized version of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Normalize(Quaternion q)
        {
            double len = Length(q);
            if (len <= double.Epsilon)
                return Identity; // Or throw
            double invLen = 1.0 / len;
            return new Quaternion(q.X * invLen, q.Y * invLen, q.Z * invLen, q.W * invLen);
        }

        /// <summary>Returns a safe normalized version of the quaternion. Returns identity if normalization fails.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion NormalizeSafe(Quaternion q)
        {
            double lenSq = LengthSquared(q);
            if (lenSq < double.Epsilon)
                return Identity;
            double invLen = Maths.Rsqrt(lenSq);
            return new Quaternion(q.X * invLen, q.Y * invLen, q.Z * invLen, q.W * invLen);
        }

        /// <summary>Returns a safe normalized version of the quaternion. Returns defaultValue if normalization fails.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion NormalizeSafe(Quaternion q, Quaternion defaultValue)
        {
            double lenSq = LengthSquared(q);
            if (lenSq < double.Epsilon)
                return defaultValue;
            double invLen = Maths.Rsqrt(lenSq);
            return new Quaternion(q.X * invLen, q.Y * invLen, q.Z * invLen, q.W * invLen);
        }

        /// <summary>Creates a quaternion for a rotation around the X axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion RotateX(double angle)
        {
            double halfAngle = angle * 0.5;
            //Maths.Sincos(angle * 0.5, out double s, out double c);
            double s = Maths.Sin(halfAngle);
            double c = Maths.Cos(halfAngle);
            return new Quaternion(s, 0.0, 0.0, c);
        }

        /// <summary>Creates a quaternion for a rotation around the Y axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion RotateY(double angle)
        {
            double halfAngle = angle * 0.5;
            //Maths.Sincos(angle * 0.5, out double s, out double c);
            double s = Maths.Sin(halfAngle);
            double c = Maths.Cos(halfAngle);
            return new Quaternion(0.0, s, 0.0, c);
        }

        /// <summary>Creates a quaternion for a rotation around the Z axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion RotateZ(double angle)
        {
            double halfAngle = angle * 0.5;
            //Maths.Sincos(angle * 0.5, out double s, out double c);
            double s = Maths.Sin(halfAngle);
            double c = Maths.Cos(halfAngle);
            return new Quaternion(0.0, 0.0, s, c);
        }

        /// <summary>Creates a quaternion looking from a 'forward' direction with an 'up' vector.</summary>
        public static Quaternion LookRotation(Double3 forward, Double3 up)
        {
            // Ensure forward vector is normalized
            forward = Double3.Normalize(forward);

            Double3 right = Double3.Cross(up, forward);

            // Handle degenerate case when forward and up are parallel
            if (Double3.LengthSquared(right) < double.Epsilon)
            {
                // use X-axis as right when looking up/down
                // For looking straight up (0,1,0) or down (0,-1,0), keep right as (1,0,0)
                right = Double3.UnitX;
            }

            right = Double3.Normalize(right);
            Double3 newUp = Double3.Cross(forward, right);

            // Create rotation matrix components
            Double3x3 m = new Double3x3(right, newUp, forward);
            return FromMatrix(m);
        }

        /// <summary>Normalized Lerp: Linearly interpolates and then normalizes. Faster than Slerp but not constant velocity.</summary>
        public static Quaternion Nlerp(Quaternion q1, Quaternion q2, double t)
        {
            double dot = Dot(q1, q2);
            double w1x = q1.X, w1y = q1.Y, w1z = q1.Z, w1w = q1.W;
            double w2x = q2.X, w2y = q2.Y, w2z = q2.Z, w2w = q2.W;

            if (dot < 0.0) // Ensure shortest path
            {
                w2x = -w2x; w2y = -w2y; w2z = -w2z; w2w = -w2w;
            }

            double resX = w1x + t * (w2x - w1x);
            double resY = w1y + t * (w2y - w1y);
            double resZ = w1z + t * (w2z - w1z);
            double resW = w1w + t * (w2w - w1w);

            return Normalize(new Quaternion(resX, resY, resZ, resW));
        }

        /// <summary>Spherical Linear Interpolation: Interpolates along the great arc on the unit sphere. Constant velocity.</summary>
        public static Quaternion Slerp(Quaternion q1, Quaternion q2, double t)
        {
            double dot = Dot(q1, q2);
            Quaternion q2Adjusted = q2;

            if (dot < 0.0)
            {
                dot = -dot;
                q2Adjusted = new Quaternion(-q2.X, -q2.Y, -q2.Z, -q2.W);
            }

            if (dot > 0.9995) // If quaternions are very close, use Nlerp for stability
            {
                return Nlerp(q1, q2Adjusted, t);
            }

            double angle = Maths.Acos(dot);        // Angle between input quaternions
            double sinAngle = Maths.Sin(angle);    // Sin of angle
            if (Maths.Abs(sinAngle) < double.Epsilon) // Should not happen if dot <= 0.9995f
            {
                return Nlerp(q1, q2Adjusted, t); // Fallback
            }

            double invSinAngle = 1.0 / sinAngle;
            double scale0 = Maths.Sin((1.0 - t) * angle) * invSinAngle;
            double scale1 = Maths.Sin(t * angle) * invSinAngle;

            return new Quaternion(
                (scale0 * q1.X) + (scale1 * q2Adjusted.X),
                (scale0 * q1.Y) + (scale1 * q2Adjusted.Y),
                (scale0 * q1.Z) + (scale1 * q2Adjusted.Z),
                (scale0 * q1.W) + (scale1 * q2Adjusted.W)
            );
        }

        /// <summary>Returns the angle in radians between two unit quaternions.</summary>
        public static double Angle(Quaternion q1, Quaternion q2)
        {
            // Ensure they are unit quaternions or the result might be off
            // For non-unit quaternions, normalizing them first is advisable:
            // q1 = Normalize(q1);
            // q2 = Normalize(q2);
            double dot = Dot(q1, q2);
            // Clamp dot to avoid Acos domain errors due to doubleing point inaccuracies
            return Maths.Acos(Maths.Min(Maths.Abs(dot), 1.0)) * 2.0;
        }

        /// <summary>The "forward" vector of a rotation (0,0,1) rotated by the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Forward(Quaternion q) => q * new Double3(0, 0, 1);

        /// <summary>The "up" vector of a rotation (0,1,0) rotated by the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Up(Quaternion q) => q * new Double3(0, 1, 0);

        /// <summary>The "right" vector of a rotation (1,0,0) rotated by the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Right(Quaternion q) => q * new Double3(1, 0, 0);

        #endregion

        /// <summary>Implicitly converts a Double4 vector to a Quaternion.</summary>
        public static implicit operator Quaternion(Double4 v) => new Quaternion(v);

        /// <summary>Explicitly converts a Quaternion to a Double4 vector.</summary>
        public static explicit operator Double4(Quaternion q) => new Double4(q.X, q.Y, q.Z, q.W);

        public static Quaternion operator -(Quaternion value) => new Quaternion(-value.X, -value.Y, -value.Z, -value.W);

        public static Quaternion operator +(Quaternion lh, double scalar) => new Quaternion(lh.X + scalar, lh.Y + scalar, lh.Z + scalar, lh.W + scalar);
        public static Quaternion operator -(Quaternion lh, double scalar) => new Quaternion(lh.X - scalar, lh.Y - scalar, lh.Z - scalar, lh.W - scalar);
        public static Quaternion operator /(Quaternion lhs, double scalar) => new Quaternion(lhs.X / scalar, lhs.Y / scalar, lhs.Z / scalar, lhs.W / scalar);
        public static Quaternion operator *(Quaternion lhs, double scalar) => new Quaternion(lhs.X * scalar, lhs.Y * scalar, lhs.Z * scalar, lhs.W * scalar);

        public static Quaternion operator +(Quaternion lh, Quaternion rh) => new Quaternion(lh.X + rh.X, lh.Y + rh.Y, lh.Z + rh.Z, lh.W + rh.W);
        public static Quaternion operator -(Quaternion lh, Quaternion rh) => new Quaternion(lh.X - rh.X, lh.Y - rh.Y, lh.Z - rh.Z, lh.W - rh.W);
        public static Quaternion operator /(Quaternion lh, Quaternion rh) => new Quaternion(lh.X / rh.X, lh.Y / rh.Y, lh.Z / rh.Z, lh.W / rh.W);

        /// <summary> Multiplies two quaternions, combining their rotations. </summary>
        /// <remarks>The order is important: a * b means applying rotation b then rotation a.</remarks>
        public static Quaternion operator *(Quaternion lh, Quaternion rh) => new Quaternion(
                lh.W * rh.X + lh.X * rh.W + lh.Y * rh.Z - lh.Z * rh.Y,
                lh.W * rh.Y - lh.X * rh.Z + lh.Y * rh.W + lh.Z * rh.X,
                lh.W * rh.Z + lh.X * rh.Y - lh.Y * rh.X + lh.Z * rh.W,
                lh.W * rh.W - lh.X * rh.X - lh.Y * rh.Y - lh.Z * rh.Z
            );

        /// <summary>Rotates a 3D vector by a quaternion.</summary>
        public static Double3 operator *(Quaternion lh, Double3 vector)
        {
            Double3 qVec = new Double3(lh.X, lh.Y, lh.Z);
            Double3 t = 2.0f * Double3.Cross(qVec, vector);
            return vector + lh.W * t + Double3.Cross(qVec, t);
        }

        #region Equals and GetHashCode

        public static bool operator ==(Quaternion lhs, Quaternion rhs)
        {
            if (lhs.X == rhs.X && lhs.Y == rhs.Y && lhs.Z == rhs.Z && lhs.W == rhs.W)
                return true;
            else
                return false;
        }

        public static bool operator !=(Quaternion lhs, Quaternion rhs)
        {
            return !(lhs == rhs);
        }

        /// <summary>Checks if this quaternion is equal to another quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Quaternion other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;

        /// <summary>Checks if this quaternion is equal to an object.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);

        /// <summary>Gets the hash code for this quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

        #endregion

        /// <summary>Returns a string representation of the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

        /// <summary>Returns a string representation of the quaternion using the specified format.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format) => ToString(format, CultureInfo.CurrentCulture);

        /// <summary>Returns a string representation of the quaternion using the specified format and format provider.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            return string.Format(formatProvider, "Quaternion({0}, {1}, {2}, {3})",
                X.ToString(format, formatProvider),
                Y.ToString(format, formatProvider),
                Z.ToString(format, formatProvider),
                W.ToString(format, formatProvider));
        }
    }
}
