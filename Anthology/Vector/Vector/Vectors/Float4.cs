using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    /// <summary>
    /// Represents a 4-component vector using float precision.
    /// </summary>
    [System.Serializable]
    public partial struct Float4 : IEquatable<Float4>, IFormattable
    {
        /// <summary>Gets the zero vector.</summary>
        public static Float4 Zero => new Float4(0f, 0f, 0f, 0f);
        /// <summary>Gets the one vector.</summary>
        public static Float4 One => new Float4(1f, 1f, 1f, 1f);
        /// <summary>Gets the unit vector along the X-axis.</summary>
        public static Float4 UnitX => new Float4(1f, 0f, 0f, 0f);
        /// <summary>Gets the unit vector along the Y-axis.</summary>
        public static Float4 UnitY => new Float4(0f, 1f, 0f, 0f);
        /// <summary>Gets the unit vector along the Z-axis.</summary>
        public static Float4 UnitZ => new Float4(0f, 0f, 1f, 0f);
        /// <summary>Gets the unit vector along the W-axis.</summary>
        public static Float4 UnitW => new Float4(0f, 0f, 0f, 1f);


        public float X, Y, Z, W;


        #region Properties

        /// <summary>Gets or sets the component at the specified index.</summary>
        public float this[int index]
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

        public Float4(float scalar) : this(scalar, scalar, scalar, scalar) { }
        public Float4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public Float4(Float4 v) : this(v.X, v.Y, v.Z, v.W) { }
        public Float4(float[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length < 4) throw new ArgumentException("Array must contain at least 4 elements.", nameof(array));
            X = array[0];
            Y = array[1];
            Z = array[2];
            W = array[3];
        }

        public Float4(Float2 xy, float z, float w) : this(xy.X, xy.Y, z, w) { }
        public Float4(float x, Float2 yz, float w) : this(x, yz.X, yz.Y, w) { }
        public Float4(float x, float y, Float2 zw) : this(x, y, zw.X, zw.Y) { }
        public Float4(Float2 xy, Float2 zw) : this(xy.X, xy.Y, zw.X, zw.Y) { }
        public Float4(Float3 xyz, float w) : this(xyz.X, xyz.Y, xyz.Z, w) { }
        public Float4(float x, Float3 yzw) : this(x, yzw.X, yzw.Y, yzw.Z) { }

        public Float4(Double4 v) : this((float)v.X, (float)v.Y, (float)v.Z, (float)v.W) { }
        public Float4(Int4 v) : this(v.X, v.Y, v.Z, v.W) { }

        public Float4(IEnumerable<float> values)
        {
            var array = values.ToArray();
            if (array.Length < 4) throw new ArgumentException("Collection must contain at least 4 elements.", nameof(values));
            X = array[0];
            Y = array[1];
            Z = array[2];
            W = array[3];
        }

        public Float4(ReadOnlySpan<float> span)
        {
            if (span.Length < 4) throw new ArgumentException("Span must contain at least 4 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
            W = span[3];
        }

        public Float4(Span<float> span)
        {
            if (span.Length < 4) throw new ArgumentException("Span must contain at least 4 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
            W = span[3];
        }

        #endregion


        #region Static Methods

        /// <summary>Returns a normalized version of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Normalize(Float4 v)
        {
            float length = Length(v);
            if (length > float.Epsilon)
                return v / length;
            return Zero;
        }

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Length(Float4 v) => Maths.Sqrt(LengthSquared(v));

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LengthSquared(Float4 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z + v.W * v.W;

        /// <summary>Returns the angle in radians between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AngleBetween(Float4 a, Float4 b)
        {
            float dot = Dot(Normalize(a), Normalize(b));
            return Maths.Acos(Maths.Clamp(dot, -1f, 1f));
        }

        /// <summary>Returns the distance between two Float4 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Float4 x, Float4 y) => Length(x - y);

        /// <summary>Returns the distance between two Float4 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSquared(Float4 x, Float4 y) => LengthSquared(x - y);

        /// <summary>Returns the dot product of two Float4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Float4 x, Float4 y) => x.X * y.X + x.Y * y.Y + x.Z * y.Z + x.W * y.W;

        /// <summary>Checks if two vectors are parallel within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsParallel(Float4 a, Float4 b, float tolerance = 1e-6f)
        {
            float normalizedDot = MathF.Abs(Dot(Normalize(a), Normalize(b)));
            return normalizedDot >= 1f - tolerance;
        }

        /// <summary>Checks if two vectors are perpendicular within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPerpendicular(Float4 a, Float4 b, float tolerance = 1e-6f)
        {
            float dot = MathF.Abs(Dot(a, b));
            return dot <= tolerance;
        }

        /// <summary>Projects vector a onto vector b.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Project(Float4 a, Float4 b)
        {
            float denominator = Dot(b, b);
            if (denominator <= float.Epsilon)
                return Zero;
            return b * (Dot(a, b) / denominator);
        }

        /// <summary>Projects a vector onto a plane defined by a normal vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 ProjectOntoPlane(Float4 vector, Float4 planeNormal) => vector - Project(vector, planeNormal);

        /// <summary>Reflects a vector off a normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Reflect(Float4 vector, Float4 normal)
        {
            float dot = Dot(vector, normal);
            return vector - 2 * dot * normal;
        }

        /// <summary>Calculates the refraction direction for an incident vector and surface normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Refract(Float4 incident, Float4 normal, float eta)
        {
            float dotNI = Dot(normal, incident);
            float k = 1f - eta * eta * (1f - dotNI * dotNI);
            return k < 0f ? new Float4(0, 0, 0, 0) : eta * incident - (eta * dotNI + Maths.Sqrt(k)) * normal;
        }

        /// <summary>Spherically interpolates between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Slerp(Float4 a, Float4 b, float t)
        {
            t = Maths.Saturate(t);
            return SlerpUnclamped(a, b, t);
        }

        /// <summary>Spherically interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 SlerpUnclamped(Float4 a, Float4 b, float t)
        {
            Float4 result;

            // Normalize the vectors
            Float4 normalizedA = Normalize(a);
            Float4 normalizedB = Normalize(b);

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
        public static Float4 MoveTowards(Float4 current, Float4 target, float maxDistanceDelta)
        {
            Float4 toVector = target - current;
            float distance = Length(toVector);
            if (distance <= maxDistanceDelta || distance < float.Epsilon)
                return target;
            return current + toVector / distance * maxDistanceDelta;
        }

        #endregion


        #region Operators

        public static Float4 operator -(Float4 v) => new Float4(-v.X, -v.Y, -v.Z, -v.W);

        public static Float4 operator +(Float4 a, Float4 b) => new Float4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
        public static Float4 operator -(Float4 a, Float4 b) => new Float4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
        public static Float4 operator *(Float4 a, Float4 b) => new Float4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
        public static Float4 operator /(Float4 a, Float4 b) => new Float4(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
        public static Float4 operator %(Float4 a, Float4 b) => new Float4(a.X % b.X, a.Y % b.Y, a.Z % b.Z, a.W % b.W);

        public static Float4 operator +(Float4 v, float scalar) => new Float4(v.X + scalar, v.Y + scalar, v.Z + scalar, v.W + scalar);
        public static Float4 operator -(Float4 v, float scalar) => new Float4(v.X - scalar, v.Y - scalar, v.Z - scalar, v.W - scalar);
        public static Float4 operator *(Float4 v, float scalar) => new Float4(v.X * scalar, v.Y * scalar, v.Z * scalar, v.W * scalar);
        public static Float4 operator /(Float4 v, float scalar) => new Float4(v.X / scalar, v.Y / scalar, v.Z / scalar, v.W / scalar);
        public static Float4 operator %(Float4 v, float scalar) => new Float4(v.X % scalar, v.Y % scalar, v.Z % scalar, v.W % scalar);

        public static Float4 operator +(float scalar, Float4 v) => new Float4(scalar + v.X, scalar + v.Y, scalar + v.Z, scalar + v.W);
        public static Float4 operator -(float scalar, Float4 v) => new Float4(scalar - v.X, scalar - v.Y, scalar - v.Z, scalar - v.W);
        public static Float4 operator *(float scalar, Float4 v) => new Float4(scalar * v.X, scalar * v.Y, scalar * v.Z, scalar * v.W);
        public static Float4 operator /(float scalar, Float4 v) => new Float4(scalar / v.X, scalar / v.Y, scalar / v.Z, scalar / v.W);
        public static Float4 operator %(float scalar, Float4 v) => new Float4(scalar % v.X, scalar % v.Y, scalar % v.Z, scalar % v.W);

        #endregion


        #region Casting

        // System.Numerics cast
        public static implicit operator Vector4(Float4 value) => new Vector4(value.X, value.Y, value.Z, value.W);

        public static implicit operator Float4(Vector4 value) => new Float4(value.X, value.Y, value.Z, value.W);


        public static explicit operator Float4(Float2 value) => new Float4(value.X, value.Y, 0f, 0f);

        public static explicit operator Float4(Float3 value) => new Float4(value.X, value.Y, value.Z, 0f);

        public static explicit operator Float2(Float4 value) => new Float2(value.X, value.Y);

        public static explicit operator Float3(Float4 value) => new Float3(value.X, value.Y, value.Z);


        public static explicit operator Float4(Double4 v) => new Float4(v);

        public static explicit operator Float4(Int4 v) => new Float4(v);

        #endregion


        #region Equals and GetHashCode

        public static bool operator ==(Float4 left, Float4 right) => left.Equals(right);
        public static bool operator !=(Float4 left, Float4 right) => !left.Equals(right);
        public override bool Equals(object? obj) => obj is Float4 && Equals((Float4)obj);
        public bool Equals(Float4 other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2) ^ (W.GetHashCode() >> 1);

        #endregion


        /// <summary>Returns an array of components.</summary>
        public float[] ToArray() => new float[] { X, Y, Z, W };

        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : ", ";
            return "(" + X.ToString(format, formatProvider) + separator + Y.ToString(format, formatProvider) + separator + Z.ToString(format, formatProvider) + separator + W.ToString(format, formatProvider) + ")";
        }
    }
}
