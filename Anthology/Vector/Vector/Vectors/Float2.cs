using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    /// <summary>
    /// Represents a 2-component vector using float precision.
    /// </summary>
    [System.Serializable]
    public partial struct Float2 : IEquatable<Float2>, IFormattable
    {
        /// <summary>Gets the zero vector.</summary>
        public static Float2 Zero => new Float2(0f, 0f);
        /// <summary>Gets the one vector.</summary>
        public static Float2 One => new Float2(1f, 1f);
        /// <summary>Gets the unit vector along the X-axis.</summary>
        public static Float2 UnitX => new Float2(1f, 0f);
        /// <summary>Gets the unit vector along the Y-axis.</summary>
        public static Float2 UnitY => new Float2(0f, 1f);


        public float X, Y;


        #region Properties

        /// <summary>Gets or sets the component at the specified index.</summary>
        public float this[int index]
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

        public Float2(float scalar) : this(scalar, scalar) { }
        public Float2(float x, float y) { X = x; Y = y; }
        public Float2(Float2 v) : this(v.X, v.Y) { }
        public Float2(float[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length < 2) throw new ArgumentException("Array must contain at least 2 elements.", nameof(array));
            X = array[0];
            Y = array[1];
        }

        public Float2(Double2 v) : this((float)v.X, (float)v.Y) { }
        public Float2(Int2 v) : this((float)v.X, (float)v.Y) { }

        public Float2(IEnumerable<float> values)
        {
            var array = values.ToArray();
            if (array.Length < 2) throw new ArgumentException("Collection must contain at least 2 elements.", nameof(values));
            X = array[0];
            Y = array[1];
        }

        public Float2(ReadOnlySpan<float> span)
        {
            if (span.Length < 2) throw new ArgumentException("Span must contain at least 2 elements.", nameof(span));
            X = span[0];
            Y = span[1];
        }

        public Float2(Span<float> span)
        {
            if (span.Length < 2) throw new ArgumentException("Span must contain at least 2 elements.", nameof(span));
            X = span[0];
            Y = span[1];
        }

        #endregion


        #region Static Methods

        /// <summary>Returns a normalized version of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Normalize(Float2 v)
        {
            float length = Length(v);
            if (length > float.Epsilon)
                return v / length;
            return Zero;
        }

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Length(Float2 v) => Maths.Sqrt(LengthSquared(v));

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LengthSquared(Float2 v) => v.X * v.X + v.Y * v.Y;

        /// <summary>Returns the angle in radians between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AngleBetween(Float2 a, Float2 b)
        {
            float dot = Dot(Normalize(a), Normalize(b));
            return Maths.Acos(Maths.Clamp(dot, -1f, 1f));
        }

        /// <summary>Returns the distance between two Float2 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Float2 x, Float2 y) => Length(x - y);

        /// <summary>Returns the distance between two Float2 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSquared(Float2 x, Float2 y) => LengthSquared(x - y);

        /// <summary>Returns the dot product of two Float2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Float2 x, Float2 y) => x.X * y.X + x.Y * y.Y;

        /// <summary>Checks if two vectors are parallel within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsParallel(Float2 a, Float2 b, float tolerance = 1e-6f)
        {
            float normalizedDot = Maths.Abs(Dot(Normalize(a), Normalize(b)));
            return normalizedDot >= 1f - tolerance;
        }
        /// <summary>Checks if two vectors are perpendicular within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPerpendicular(Float2 a, Float2 b, float tolerance = 1e-6f)
        {
            float dot = MathF.Abs(Dot(a, b));
            return dot <= tolerance;
        }

        /// <summary>Projects vector a onto vector b.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Project(Float2 a, Float2 b)
        {
            float denominator = Dot(b, b);
            if (denominator <= float.Epsilon)
                return Float2.Zero;
            return b * (Dot(a, b) / denominator);
        }

        /// <summary>Projects a vector onto a plane defined by a normal vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 ProjectOntoPlane(Float2 vector, Float2 planeNormal) => vector - Project(vector, planeNormal);

        /// <summary>Reflects a vector off a normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Reflect(Float2 vector, Float2 normal)
        {
            float dot = Dot(vector, normal);
            return vector - 2 * dot * normal;
        }

        /// <summary>Calculates the refraction direction for an incident vector and surface normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Refract(Float2 incident, Float2 normal, float eta)
        {
            float dotNI = Dot(normal, incident);
            float k = 1f - eta * eta * (1f - dotNI * dotNI);
            return k < 0f ? new Float2(0, 0) : eta * incident - (eta * dotNI + Maths.Sqrt(k)) * normal;
        }

        /// <summary>Returns the signed angle in radians between two 2D vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SignedAngleBetween(Float2 a, Float2 b) => MathF.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y);

        /// <summary>Spherically interpolates between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Slerp(Float2 a, Float2 b, float t)
        {
            t = Maths.Saturate(t);
            return SlerpUnclamped(a, b, t);
        }

        /// <summary>Spherically interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 SlerpUnclamped(Float2 a, Float2 b, float t)
        {
            Float2 result;

            // Normalize the vectors
            Float2 normalizedA = Normalize(a);
            Float2 normalizedB = Normalize(b);

            // Calculate the cosine of the angle between them
            float dot = Dot(normalizedA, normalizedB);

            // If the dot product is negative, slerp won't take the shorter path.
            // So negate one vector to get the shorter path.
            if (dot < 0f)
            {
                normalizedB = -normalizedB;
                dot = -dot;
            }

            // If the vectors are close to identical, just use linear interpolation
            if (dot > 1f - 1e-6f)
            {
                result = normalizedA + t * (normalizedB - normalizedA);
                return Normalize(result) * Maths.Lerp(Length(a), Length(b), t);
            }

            // Calculate angle and sin
            float angle = MathF.Acos(Maths.Abs(dot));
            float sinAngle = MathF.Sin(angle);

            // Calculate the scale factors
            float scale1 = MathF.Sin((1f - t) * angle) / sinAngle;
            float scale2 = MathF.Sin(t * angle) / sinAngle;

            // Interpolate
            result = scale1 * normalizedA + scale2 * normalizedB;
            return result * Maths.Lerp(Length(a), Length(b), t);
        }

        /// <summary>Moves a vector current towards target.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 MoveTowards(Float2 current, Float2 target, float maxDistanceDelta)
        {
            Float2 toVector = target - current;
            float distance = Length(toVector);
            if (distance <= maxDistanceDelta || distance < float.Epsilon)
                return target;
            return current + toVector / distance * maxDistanceDelta;
        }

        #endregion

        #region Operators

        public static Float2 operator -(Float2 v) => new Float2(-v.X, -v.Y);

        public static Float2 operator +(Float2 a, Float2 b) => new Float2(a.X + b.X, a.Y + b.Y);
        public static Float2 operator -(Float2 a, Float2 b) => new Float2(a.X - b.X, a.Y - b.Y);
        public static Float2 operator *(Float2 a, Float2 b) => new Float2(a.X * b.X, a.Y * b.Y);
        public static Float2 operator /(Float2 a, Float2 b) => new Float2(a.X / b.X, a.Y / b.Y);
        public static Float2 operator %(Float2 a, Float2 b) => new Float2(a.X % b.X, a.Y % b.Y);

        public static Float2 operator +(Float2 v, float scalar) => new Float2(v.X + scalar, v.Y + scalar);
        public static Float2 operator -(Float2 v, float scalar) => new Float2(v.X - scalar, v.Y - scalar);
        public static Float2 operator *(Float2 v, float scalar) => new Float2(v.X * scalar, v.Y * scalar);
        public static Float2 operator /(Float2 v, float scalar) => new Float2(v.X / scalar, v.Y / scalar);
        public static Float2 operator %(Float2 v, float scalar) => new Float2(v.X % scalar, v.Y % scalar);

        public static Float2 operator +(float scalar, Float2 v) => new Float2(scalar + v.X, scalar + v.Y);
        public static Float2 operator -(float scalar, Float2 v) => new Float2(scalar - v.X, scalar - v.Y);
        public static Float2 operator *(float scalar, Float2 v) => new Float2(scalar * v.X, scalar * v.Y);
        public static Float2 operator /(float scalar, Float2 v) => new Float2(scalar / v.X, scalar / v.Y);
        public static Float2 operator %(float scalar, Float2 v) => new Float2(scalar % v.X, scalar % v.Y);

        #endregion


        #region Casting

        // System.Numerics Cast
        public static implicit operator Vector2(Float2 value) => new Vector2(value.X, value.Y);

        public static implicit operator Float2(Vector2 value) => new Float2(value.X, value.Y);


        public static explicit operator Float2(Double2 v) => new Float2(v);

        public static explicit operator Float2(Int2 v) => new Float2(v);

        #endregion


        #region Equals and GetHashCode

        public static bool operator ==(Float2 left, Float2 right) => left.Equals(right);
        public static bool operator !=(Float2 left, Float2 right) => !left.Equals(right);
        public override bool Equals(object? obj) => obj is Float2 && Equals((Float2)obj);
        public bool Equals(Float2 other) => X == other.X && Y == other.Y;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2);

        #endregion


        /// <summary>Returns an array of components.</summary>
        public float[] ToArray() => new float[] { X, Y };

        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : ", ";
            return "(" + X.ToString(format, formatProvider) + separator + Y.ToString(format, formatProvider) + ")";
        }
    }
}
