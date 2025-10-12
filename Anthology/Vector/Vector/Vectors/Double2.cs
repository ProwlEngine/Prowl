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
    /// Represents a 2-component vector using double precision.
    /// </summary>
    [System.Serializable]
    public partial struct Double2 : IEquatable<Double2>, IFormattable
    {
        /// <summary>Gets the zero vector.</summary>
        public static Double2 Zero { get { return new Double2(0.0, 0.0); } }
        /// <summary>Gets the one vector.</summary>
        public static Double2 One { get { return new Double2(1.0, 1.0); } }
        /// <summary>Gets the unit vector along the X-axis.</summary>
        public static Double2 UnitX { get { return new Double2(1.0, 0.0); } }
        /// <summary>Gets the unit vector along the Y-axis.</summary>
        public static Double2 UnitY { get { return new Double2(0.0, 1.0); } }


        public double X, Y;


        #region Properties

        /// <summary>Gets or sets the component at the specified index.</summary>
        public double this[int index]
        {
            get => index switch
            {
                0 => X,
                1 => Y,
                _ => throw new IndexOutOfRangeException(string.Format("Index must be between 0 and 1, but was {0}", index)),
            };

            set
            {
                switch (index)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    default: throw new IndexOutOfRangeException(string.Format("Index must be between 0 and 1, but was {0}", index));
                }
            }
        }

        #endregion


        #region Constructors

        public Double2(double scalar) : this(scalar, scalar) { }
        public Double2(double x, double y) { X = x; Y = y; }
        public Double2(Double2 v) : this(v.X, v.Y) { }
        public Double2(double[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length < 2) throw new ArgumentException("Array must contain at least 2 elements.", nameof(array));
            X = array[0];
            Y = array[1];
        }

        public Double2(Float2 v) : this(v.X, v.Y) { }
        public Double2(Int2 v) : this(v.X, v.Y) { }

        public Double2(IEnumerable<double> values)
        {
            var array = values.ToArray();
            if (array.Length < 2) throw new ArgumentException("Collection must contain at least 2 elements.", nameof(values));
            X = array[0];
            Y = array[1];
        }

        public Double2(ReadOnlySpan<double> span)
        {
            if (span.Length < 2) throw new ArgumentException("Span must contain at least 2 elements.", nameof(span));
            X = span[0];
            Y = span[1];
        }

        public Double2(Span<double> span)
        {
            if (span.Length < 2) throw new ArgumentException("Span must contain at least 2 elements.", nameof(span));
            X = span[0];
            Y = span[1];
        }

        #endregion


        #region Static Methods

        /// <summary>Returns a normalized version of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Normalize(Double2 v)
        {
            double length = Length(v);
            if (length > double.Epsilon)
                return v / length;
            return Zero;
        }

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Length(Double2 v) => Maths.Sqrt(LengthSquared(v));

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double LengthSquared(Double2 v) => v.X * v.X + v.Y * v.Y;

        /// <summary>Returns the angle in radians between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double AngleBetween(Double2 a, Double2 b)
        {
            double dot = Dot(Normalize(a), Normalize(b));
            return Maths.Acos(Maths.Clamp(dot, -1, 1));
        }

        /// <summary>Returns the distance between two Double2 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Distance(Double2 x, Double2 y) => Length(x - y);

        /// <summary>Returns the distance between two Double2 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DistanceSquared(Double2 x, Double2 y) => LengthSquared(x - y);

        /// <summary>Returns the dot product of two Double2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot(Double2 x, Double2 y) => x.X * y.X + x.Y * y.Y;

        /// <summary>Checks if two vectors are parallel within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsParallel(Double2 a, Double2 b, double tolerance = 1e-15)
        {
            double normalizedDot = Math.Abs(Dot(Normalize(a), Normalize(b)));
            return normalizedDot >= 1 - tolerance;
        }

        /// <summary>Checks if two vectors are perpendicular within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPerpendicular(Double2 a, Double2 b, double tolerance = 1e-15)
        {
            double dot = Math.Abs(Dot(a, b));
            return dot <= tolerance;
        }

        /// <summary>Projects vector a onto vector b.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Project(Double2 a, Double2 b)
        {
            double denominator = Dot(b, b);
            if (denominator <= float.Epsilon)
                return Zero;
            return b * (Dot(a, b) / denominator);
        }

        /// <summary>Projects a vector onto a plane defined by a normal vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 ProjectOntoPlane(Double2 vector, Double2 planeNormal) => vector - Project(vector, planeNormal);

        /// <summary>Reflects a vector off a normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Reflect(Double2 vector, Double2 normal)
        {
            double dot = Dot(vector, normal);
            return vector - 2 * dot * normal;
        }

        /// <summary>Calculates the refraction direction for an incident vector and surface normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Refract(Double2 incident, Double2 normal, double eta)
        {
            double dotNI = Dot(normal, incident);
            double k = 1 - eta * eta * (1 - dotNI * dotNI);
            return k < 0 ? new Double2(0, 0) : eta * incident - (eta * dotNI + Maths.Sqrt(k)) * normal;
        }

        /// <summary>Returns the signed angle in radians between two 2D vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SignedAngleBetween(Double2 a, Double2 b) => Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y);

        /// <summary>Spherically interpolates between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Slerp(Double2 a, Double2 b, double t)
        {
            t = Maths.Saturate(t);
            return SlerpUnclamped(a, b, t);
        }

        /// <summary>Spherically interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 SlerpUnclamped(Double2 a, Double2 b, double t)
        {
            Double2 result;

            // Normalize the vectors
            Double2 normalizedA = Normalize(a);
            Double2 normalizedB = Normalize(b);

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
        public static Double2 MoveTowards(Double2 current, Double2 target, double maxDistanceDelta)
        {
            Double2 toVector = target - current;
            double distance = Length(toVector);
            if (distance <= maxDistanceDelta || distance < double.Epsilon)
                return target;
            return current + toVector / distance * maxDistanceDelta;
        }

        #endregion


        #region Operators

        public static Double2 operator -(Double2 v) => new Double2(-v.X, -v.Y);

        public static Double2 operator +(Double2 a, Double2 b) => new Double2(a.X + b.X, a.Y + b.Y);
        public static Double2 operator -(Double2 a, Double2 b) => new Double2(a.X - b.X, a.Y - b.Y);
        public static Double2 operator *(Double2 a, Double2 b) => new Double2(a.X * b.X, a.Y * b.Y);
        public static Double2 operator /(Double2 a, Double2 b) => new Double2(a.X / b.X, a.Y / b.Y);
        public static Double2 operator %(Double2 a, Double2 b) => new Double2(a.X % b.X, a.Y % b.Y);

        public static Double2 operator +(Double2 v, double scalar) => new Double2(v.X + scalar, v.Y + scalar);
        public static Double2 operator -(Double2 v, double scalar) => new Double2(v.X - scalar, v.Y - scalar);
        public static Double2 operator *(Double2 v, double scalar) => new Double2(v.X * scalar, v.Y * scalar);
        public static Double2 operator /(Double2 v, double scalar) => new Double2(v.X / scalar, v.Y / scalar);
        public static Double2 operator %(Double2 v, double scalar) => new Double2(v.X % scalar, v.Y % scalar);

        public static Double2 operator +(double scalar, Double2 v) => new Double2(scalar + v.X, scalar + v.Y);
        public static Double2 operator -(double scalar, Double2 v) => new Double2(scalar - v.X, scalar - v.Y);
        public static Double2 operator *(double scalar, Double2 v) => new Double2(scalar * v.X, scalar * v.Y);
        public static Double2 operator /(double scalar, Double2 v) => new Double2(scalar / v.X, scalar / v.Y);
        public static Double2 operator %(double scalar, Double2 v) => new Double2(scalar % v.X, scalar % v.Y);

        #endregion


        #region Casting

        public static implicit operator Double2(Float2 v) => new Double2(v);
        
        public static explicit operator Double2(Int2 v) => new Double2(v);

        #endregion


        #region Equals and GetHashCode

        public static bool operator ==(Double2 left, Double2 right) => left.Equals(right);
        public static bool operator !=(Double2 left, Double2 right) => !left.Equals(right);
        public override bool Equals(object? obj) => obj is Double2 && Equals((Double2)obj);
        public bool Equals(Double2 other) => X == other.X && Y == other.Y;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2);

        #endregion


        /// <summary>Returns an array of components.</summary>
        public double[] ToArray() => new double[] { X, Y };

        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : ", ";
            return "(" + X.ToString(format, formatProvider) + separator + Y.ToString(format, formatProvider) + ")";
        }
    }
}
