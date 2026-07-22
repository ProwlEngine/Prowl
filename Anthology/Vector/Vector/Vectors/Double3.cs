// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    /// <summary>
    /// Represents a 3-component vector using double precision.
    /// </summary>
    [System.Serializable]
    public partial struct Double3 : IEquatable<Double3>, IFormattable
    {
        /// <summary>Gets the zero vector.</summary>
        public static Double3 Zero => new Double3(0.0, 0.0, 0.0);
        /// <summary>Gets the one vector.</summary>
        public static Double3 One => new Double3(1.0, 1.0, 1.0);
        /// <summary>Gets the unit vector along the X-axis.</summary>
        public static Double3 UnitX => new Double3(1.0, 0.0, 0.0);
        /// <summary>Gets the unit vector along the Y-axis.</summary>
        public static Double3 UnitY => new Double3(0.0, 1.0, 0.0);
        /// <summary>Gets the unit vector along the Z-axis.</summary>
        public static Double3 UnitZ => new Double3(0.0, 0.0, 1.0);


        public double X, Y, Z;


        #region Properties

        /// <summary>Gets or sets the component at the specified index.</summary>
        public double this[int index]
        {
            get => index switch
            {
                0 => X,
                1 => Y,
                2 => Z,
                _ => throw new IndexOutOfRangeException(string.Format("Index must be between 0 and 2, but was {0}", index)),
            };

            set
            {
                switch (index)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    case 2: Z = value; break;
                    default: throw new IndexOutOfRangeException(string.Format("Index must be between 0 and 2, but was {0}", index));
                }
            }
        }

        #endregion


        #region Constructors

        public Double3(double scalar) : this(scalar, scalar, scalar) { }
        public Double3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public Double3(Double3 v) : this(v.X, v.Y, v.Z) { }

        public Double3(double[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length < 3) throw new ArgumentException("Array must contain at least 3 elements.", nameof(array));
            X = array[0];
            Y = array[1];
            Z = array[2];
        }

        public Double3(Double2 xy, double z) : this(xy.X, xy.Y, z) { }
        public Double3(double x, Double2 yz) : this(x, yz.X, yz.Y) { }
        public Double3(Float3 v) : this(v.X, v.Y, v.Z) { }
        public Double3(Int3 v) : this(v.X, v.Y, v.Z) { }

        public Double3(IEnumerable<double> values)
        {
            var array = values.ToArray();
            if (array.Length < 3) throw new ArgumentException("Collection must contain at least 3 elements.", nameof(values));
            X = array[0];
            Y = array[1];
            Z = array[2];
        }

        public Double3(ReadOnlySpan<double> span)
        {
            if (span.Length < 3) throw new ArgumentException("Span must contain at least 3 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
        }

        public Double3(Span<double> span)
        {
            if (span.Length < 3) throw new ArgumentException("Span must contain at least 3 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
        }

        #endregion


        #region Static Methods

        /// <summary>Returns a normalized version of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Normalize(Double3 v)
        {
            double length = Length(v);
            if (length > double.Epsilon)
                return v / length;
            return Zero;
        }

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Length(Double3 v) => Maths.Sqrt(LengthSquared(v));

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double LengthSquared(Double3 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;

        /// <summary>Returns the angle in radians between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double AngleBetween(Double3 a, Double3 b)
        {
            double dot = Dot(Normalize(a), Normalize(b));
            return Maths.Acos(Maths.Clamp(dot, -1, 1));
        }

        /// <summary>Returns the cross product of two Double3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Cross(Double3 x, Double3 y) => new Double3(
                x.Y * y.Z - x.Z * y.Y,
                x.Z * y.X - x.X * y.Z,
                x.X * y.Y - x.Y * y.X
            );

        /// <summary>Returns the distance between two Double3 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Distance(Double3 x, Double3 y) => Length(x - y);

        /// <summary>Returns the distance between two Double3 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DistanceSquared(Double3 x, Double3 y) => LengthSquared(x - y);

        /// <summary>Returns the dot product of two Double3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot(Double3 x, Double3 y) => x.X * y.X + x.Y * y.Y + x.Z * y.Z;

        /// <summary>Checks if two vectors are parallel within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsParallel(Double3 a, Double3 b, double tolerance = 1e-15)
        {
            double normalizedDot = Math.Abs(Dot(Normalize(a), Normalize(b)));
            return normalizedDot >= 1 - tolerance;
        }

        /// <summary>Checks if two vectors are perpendicular within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPerpendicular(Double3 a, Double3 b, double tolerance = 1e-15)
        {
            double dot = Math.Abs(Dot(a, b));
            return dot <= tolerance;
        }

        /// <summary>Orthonormalizes a set of three vectors using Gram-Schmidt process.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OrthoNormalize(ref Double3 normal, ref Double3 tangent, ref Double3 binormal)
        {
            normal = Normalize(normal);
            tangent = Normalize(tangent - Project(tangent, normal));
            binormal = Cross(normal, tangent);
        }

        /// <summary>Orthonormalizes two vectors using Gram-Schmidt process.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OrthoNormalize(ref Double3 normal, ref Double3 tangent)
        {
            normal = Normalize(normal);
            tangent = Normalize(tangent - Project(tangent, normal));
        }

        /// <summary>Projects vector a onto vector b.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Project(Double3 a, Double3 b)
        {
            double denominator = Dot(b, b);
            if (denominator <= float.Epsilon)
                return Zero;
            return b * (Dot(a, b) / denominator);
        }

        /// <summary>Projects a vector onto a plane defined by a normal vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 ProjectOntoPlane(Double3 vector, Double3 planeNormal) => vector - Project(vector, planeNormal);

        /// <summary>Reflects a vector off a normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Reflect(Double3 vector, Double3 normal)
        {
            double dot = Dot(vector, normal);
            return vector - 2 * dot * normal;
        }

        /// <summary>Calculates the refraction direction for an incident vector and surface normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Refract(Double3 incident, Double3 normal, double eta)
        {
            double dotNI = Dot(normal, incident);
            double k = 1 - eta * eta * (1 - dotNI * dotNI);
            return k < 0 ? new Double3(0, 0, 0) : eta * incident - (eta * dotNI + Maths.Sqrt(k)) * normal;
        }

        /// <summary>Returns the signed angle in radians between two 3D vectors around a reference axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SignedAngleBetween(Double3 a, Double3 b, Double3 axis)
        {
            double angle = AngleBetween(a, b);
            Double3 cross = Cross(a, b);
            double sign = Dot(cross, axis) < 0 ? -1 : 1;
            return angle * sign;
        }

        /// <summary>Spherically interpolates between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Slerp(Double3 a, Double3 b, double t)
        {
            t = Maths.Saturate(t);
            return SlerpUnclamped(a, b, t);
        }

        /// <summary>Spherically interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 SlerpUnclamped(Double3 a, Double3 b, double t)
        {
            Double3 result;

            // Normalize the vectors
            Double3 normalizedA = Normalize(a);
            Double3 normalizedB = Normalize(b);

            // Calculate the cosine of the angle between them
            double dot = Dot(normalizedA, normalizedB);

            // If the dot product is negative, slerp won't take the shorter path.
            // So negate one vector to get the shorter path.
            if (dot < 0)
            {
                normalizedB = -normalizedB;
                dot = -dot;
            }

            // If the vectors are close to identical, just use linear interpolation
            if (dot > 1 - 1e-15)
            {
                result = normalizedA + t * (normalizedB - normalizedA);
                return Normalize(result) * Maths.Lerp(Length(a), Length(b), t);
            }

            // Calculate angle and sin
            double angle = Math.Acos(Maths.Abs(dot));
            double sinAngle = Math.Sin(angle);

            // Calculate the scale factors
            double scale1 = Math.Sin((1 - t) * angle) / sinAngle;
            double scale2 = Math.Sin(t * angle) / sinAngle;

            // Interpolate
            result = scale1 * normalizedA + scale2 * normalizedB;
            return result * Maths.Lerp(Length(a), Length(b), t);
        }

        /// <summary>Moves a vector current towards target.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 MoveTowards(Double3 current, Double3 target, double maxDistanceDelta)
        {
            Double3 toVector = target - current;
            double distance = Length(toVector);
            if (distance <= maxDistanceDelta || distance < double.Epsilon)
                return target;
            return current + toVector / distance * maxDistanceDelta;
        }

        #endregion


        #region Operators

        public static Double3 operator -(Double3 v) => new Double3(-v.X, -v.Y, -v.Z);

        public static Double3 operator +(Double3 a, Double3 b) => new Double3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Double3 operator -(Double3 a, Double3 b) => new Double3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Double3 operator *(Double3 a, Double3 b) => new Double3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public static Double3 operator /(Double3 a, Double3 b) => new Double3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
        public static Double3 operator %(Double3 a, Double3 b) => new Double3(a.X % b.X, a.Y % b.Y, a.Z % b.Z);

        public static Double3 operator +(Double3 v, double scalar) => new Double3(v.X + scalar, v.Y + scalar, v.Z + scalar);
        public static Double3 operator -(Double3 v, double scalar) => new Double3(v.X - scalar, v.Y - scalar, v.Z - scalar);
        public static Double3 operator *(Double3 v, double scalar) => new Double3(v.X * scalar, v.Y * scalar, v.Z * scalar);
        public static Double3 operator /(Double3 v, double scalar) => new Double3(v.X / scalar, v.Y / scalar, v.Z / scalar);
        public static Double3 operator %(Double3 v, double scalar) => new Double3(v.X % scalar, v.Y % scalar, v.Z % scalar);

        public static Double3 operator +(double scalar, Double3 v) => new Double3(scalar + v.X, scalar + v.Y, scalar + v.Z);
        public static Double3 operator -(double scalar, Double3 v) => new Double3(scalar - v.X, scalar - v.Y, scalar - v.Z);
        public static Double3 operator *(double scalar, Double3 v) => new Double3(scalar * v.X, scalar * v.Y, scalar * v.Z);
        public static Double3 operator /(double scalar, Double3 v) => new Double3(scalar / v.X, scalar / v.Y, scalar / v.Z);
        public static Double3 operator %(double scalar, Double3 v) => new Double3(scalar % v.X, scalar % v.Y, scalar % v.Z);

        #endregion


        #region Casting

        public static explicit operator Double3(Double2 value) => new Double3(value.X, value.Y, 0.0);
        
        public static explicit operator Double2(Double3 value) => new Double2(value.X, value.Y);
        
        public static implicit operator Double3(Float3 v) => new Double3(v);
        
        public static explicit operator Double3(Int3 v) => new Double3(v);

        #endregion


        #region Equals and GetHashCode

        public static bool operator ==(Double3 left, Double3 right) => left.Equals(right);
        public static bool operator !=(Double3 left, Double3 right) => !left.Equals(right);
        public override bool Equals(object? obj) => obj is Double3 && Equals((Double3)obj);
        public bool Equals(Double3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2);

        #endregion


        /// <summary>Returns an array of components.</summary>
        public double[] ToArray() => new double[] { X, Y, Z };

        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : ", ";
            return "(" + X.ToString(format, formatProvider) + separator + Y.ToString(format, formatProvider) + separator + Z.ToString(format, formatProvider) + ")";
        }
    }
}
