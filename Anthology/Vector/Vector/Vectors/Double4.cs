using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    /// <summary>
    /// Represents a 4-component vector using double precision.
    /// </summary>
    [System.Serializable]
    public partial struct Double4 : IEquatable<Double4>, IFormattable
    {
        /// <summary>Gets the zero vector.</summary>
        public static Double4 Zero => new Double4(0.0, 0.0, 0.0, 0.0);
        /// <summary>Gets the one vector.</summary>
        public static Double4 One => new Double4(1.0, 1.0, 1.0, 1.0);
        /// <summary>Gets the unit vector along the X-axis.</summary>
        public static Double4 UnitX => new Double4(1.0, 0.0, 0.0, 0.0);
        /// <summary>Gets the unit vector along the Y-axis.</summary>
        public static Double4 UnitY => new Double4(0.0, 1.0, 0.0, 0.0);
        /// <summary>Gets the unit vector along the Z-axis.</summary>
        public static Double4 UnitZ => new Double4(0.0, 0.0, 1.0, 0.0);
        /// <summary>Gets the unit vector along the W-axis.</summary>
        public static Double4 UnitW => new Double4(0.0, 0.0, 0.0, 1.0);


        public double X, Y, Z, W;


        #region Properties

        /// <summary>Gets or sets the component at the specified index.</summary>
        public double this[int index]
        {
            get => index switch
            {
                0 => X,
                1 => Y,
                2 => Z,
                3 => W,
                _ => throw new IndexOutOfRangeException(string.Format("Index must be between 0 and 3, but was {0}", index)),
            };

            set
            {
                switch (index)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    case 2: Z = value; break;
                    case 3: W = value; break;
                    default: throw new IndexOutOfRangeException(string.Format("Index must be between 0 and 3, but was {0}", index));
                }
            }
        }

        #endregion


        #region Constructors

        public Double4(double scalar) : this(scalar, scalar, scalar, scalar) { }
        public Double4(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
        public Double4(Double4 v) : this(v.X, v.Y, v.Z, v.W) { }
        public Double4(double[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length < 4) throw new ArgumentException("Array must contain at least 4 elements.", nameof(array));
            X = array[0];
            Y = array[1];
            Z = array[2];
            W = array[3];
        }

        public Double4(Double2 xy, double z, double w) : this(xy.X, xy.Y, z, w) { }
        public Double4(double x, Double2 yz, double w) : this(x, yz.X, yz.Y, w) { }
        public Double4(double x, double y, Double2 zw) : this(x, y, zw.X, zw.Y) { }
        public Double4(Double2 xy, Double2 zw) : this(xy.X, xy.Y, zw.X, zw.Y) { }
        public Double4(Double3 xyz, double w) : this(xyz.X, xyz.Y, xyz.Z, w) { }
        public Double4(double x, Double3 yzw) : this(x, yzw.X, yzw.Y, yzw.Z) { }

        public Double4(Float4 v) : this((double)v.X, (double)v.Y, (double)v.Z, (double)v.W) { }
        public Double4(Int4 v) : this((double)v.X, (double)v.Y, (double)v.Z, (double)v.W) { }

        public Double4(IEnumerable<double> values)
        {
            var array = values.ToArray();
            if (array.Length < 4) throw new ArgumentException("Collection must contain at least 4 elements.", nameof(values));
            X = array[0];
            Y = array[1];
            Z = array[2];
            W = array[3];
        }

        public Double4(ReadOnlySpan<double> span)
        {
            if (span.Length < 4) throw new ArgumentException("Span must contain at least 4 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
            W = span[3];
        }

        public Double4(Span<double> span)
        {
            if (span.Length < 4) throw new ArgumentException("Span must contain at least 4 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
            W = span[3];
        }

        #endregion


        #region Static Method

        /// <summary>Returns a normalized version of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Normalize(Double4 v)
        {
            double length = Length(v);
            if (length > double.Epsilon)
                return v / length;
            return Zero;
        }

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Length(Double4 v) => Maths.Sqrt(LengthSquared(v));

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double LengthSquared(Double4 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z + v.W * v.W;

        /// <summary>Returns the angle in radians between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double AngleBetween(Double4 a, Double4 b)
        {
            double dot = Dot(Normalize(a), Normalize(b));
            return Maths.Acos(Maths.Clamp(dot, -1, 1));
        }

        /// <summary>Returns the distance between two Double4 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Distance(Double4 x, Double4 y) => Length(x - y);

        /// <summary>Returns the distance between two Double4 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DistanceSquared(Double4 x, Double4 y) => LengthSquared(x - y);

        /// <summary>Returns the dot product of two Double4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot(Double4 x, Double4 y) => x.X * y.X + x.Y * y.Y + x.Z * y.Z + x.W * y.W;

        /// <summary>Checks if two vectors are parallel within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsParallel(Double4 a, Double4 b, double tolerance = 1e-15)
        {
            double normalizedDot = Math.Abs(Dot(Normalize(a), Normalize(b)));
            return normalizedDot >= 1 - tolerance;
        }

        /// <summary>Checks if two vectors are perpendicular within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPerpendicular(Double4 a, Double4 b, double tolerance = 1e-15)
        {
            double dot = Math.Abs(Dot(a, b));
            return dot <= tolerance;
        }

        /// <summary>Projects vector a onto vector b.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Project(Double4 a, Double4 b)
        {
            double denominator = Dot(b, b);
            if (denominator <= double.Epsilon)
                return Zero;
            return b * (Dot(a, b) / denominator);
        }

        /// <summary>Projects a vector onto a plane defined by a normal vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 ProjectOntoPlane(Double4 vector, Double4 planeNormal) => vector - Project(vector, planeNormal);

        /// <summary>Reflects a vector off a normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Reflect(Double4 vector, Double4 normal)
        {
            double dot = Dot(vector, normal);
            return vector - 2 * dot * normal;
        }

        /// <summary>Calculates the refraction direction for an incident vector and surface normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Refract(Double4 incident, Double4 normal, double eta)
        {
            double dotNI = Dot(normal, incident);
            double k = 1 - eta * eta * (1 - dotNI * dotNI);
            return k < 0 ? new Double4(0, 0, 0, 0) : eta * incident - (eta * dotNI + Maths.Sqrt(k)) * normal;
        }

        /// <summary>Spherically interpolates between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Slerp(Double4 a, Double4 b, double t)
        {
            t = Maths.Saturate(t);
            return SlerpUnclamped(a, b, t);
        }

        /// <summary>Spherically interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 SlerpUnclamped(Double4 a, Double4 b, double t)
        {
            Double4 result;

            // Normalize the vectors
            Double4 normalizedA = Normalize(a);
            Double4 normalizedB = Normalize(b);

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
        public static Double4 MoveTowards(Double4 current, Double4 target, double maxDistanceDelta)
        {
            Double4 toVector = target - current;
            double distance = Length(toVector);
            if (distance <= maxDistanceDelta || distance < double.Epsilon)
                return target;
            return current + toVector / distance * maxDistanceDelta;
        }

        #endregion


        #region Operators

        public static Double4 operator -(Double4 v) => new Double4(-v.X, -v.Y, -v.Z, -v.W);

        public static Double4 operator +(Double4 a, Double4 b) => new Double4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
        public static Double4 operator -(Double4 a, Double4 b) => new Double4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
        public static Double4 operator *(Double4 a, Double4 b) => new Double4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
        public static Double4 operator /(Double4 a, Double4 b) => new Double4(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
        public static Double4 operator %(Double4 a, Double4 b) => new Double4(a.X % b.X, a.Y % b.Y, a.Z % b.Z, a.W % b.W);

        public static Double4 operator +(Double4 v, double scalar) => new Double4(v.X + scalar, v.Y + scalar, v.Z + scalar, v.W + scalar);
        public static Double4 operator -(Double4 v, double scalar) => new Double4(v.X - scalar, v.Y - scalar, v.Z - scalar, v.W - scalar);
        public static Double4 operator *(Double4 v, double scalar) => new Double4(v.X * scalar, v.Y * scalar, v.Z * scalar, v.W * scalar);
        public static Double4 operator /(Double4 v, double scalar) => new Double4(v.X / scalar, v.Y / scalar, v.Z / scalar, v.W / scalar);
        public static Double4 operator %(Double4 v, double scalar) => new Double4(v.X % scalar, v.Y % scalar, v.Z % scalar, v.W % scalar);

        public static Double4 operator +(double scalar, Double4 v) => new Double4(scalar + v.X, scalar + v.Y, scalar + v.Z, scalar + v.W);
        public static Double4 operator -(double scalar, Double4 v) => new Double4(scalar - v.X, scalar - v.Y, scalar - v.Z, scalar - v.W);
        public static Double4 operator *(double scalar, Double4 v) => new Double4(scalar * v.X, scalar * v.Y, scalar * v.Z, scalar * v.W);
        public static Double4 operator /(double scalar, Double4 v) => new Double4(scalar / v.X, scalar / v.Y, scalar / v.Z, scalar / v.W);
        public static Double4 operator %(double scalar, Double4 v) => new Double4(scalar % v.X, scalar % v.Y, scalar % v.Z, scalar % v.W);


        #endregion


        #region Casting

        public static explicit operator Double4(Double2 value) => new Double4(value.X, value.Y, 0.0, 0.0);

        public static explicit operator Double4(Double3 value) => new Double4(value.X, value.Y, value.Z, 0.0);

        public static explicit operator Double2(Double4 value) => new Double2(value.X, value.Y);

        public static explicit operator Double3(Double4 value) => new Double3(value.X, value.Y, value.Z);

        public static explicit operator Double4(Float4 v) => new Double4(v);

        public static explicit operator Double4(Int4 v) => new Double4(v);

        #endregion


        #region Equals and GetHashCode

        public static bool operator ==(Double4 left, Double4 right) => left.Equals(right);
        public static bool operator !=(Double4 left, Double4 right) => !left.Equals(right);
        public override bool Equals(object? obj) => obj is Double4 && Equals((Double4)obj);
        public bool Equals(Double4 other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2) ^ (W.GetHashCode() >> 1);

        #endregion


        /// <summary>Returns an array of components.</summary>
        public double[] ToArray() => new double[] { X, Y, Z, W };

        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : ", ";
            return "(" + X.ToString(format, formatProvider) + separator + Y.ToString(format, formatProvider) + separator + Z.ToString(format, formatProvider) + separator + W.ToString(format, formatProvider) + ")";
        }
    }
}
