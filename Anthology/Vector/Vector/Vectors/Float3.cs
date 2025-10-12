// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    /// <summary>
    /// Represents a 3-component vector using float precision.
    /// </summary>
    [System.Serializable]
    public partial struct Float3 : IEquatable<Float3>, IFormattable
    {
        /// <summary>Gets the zero vector.</summary>
        public static Float3 Zero => new Float3(0f, 0f, 0f);
        /// <summary>Gets the one vector.</summary>
        public static Float3 One => new Float3(1f, 1f, 1f);
        /// <summary>Gets the unit vector along the X-axis.</summary>
        public static Float3 UnitX => new Float3(1f, 0f, 0f);
        /// <summary>Gets the unit vector along the Y-axis.</summary>
        public static Float3 UnitY => new Float3(0f, 1f, 0f);
        /// <summary>Gets the unit vector along the Z-axis.</summary>
        public static Float3 UnitZ => new Float3(0f, 0f, 1f);


        public float X, Y, Z;


        #region Properties

        /// <summary>Gets or sets the component at the specified index.</summary>
        public float this[int index]
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

        public Float3(float scalar) : this(scalar, scalar, scalar) { }
        public Float3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public Float3(Float3 v) : this(v.X, v.Y, v.Z) { }

        public Float3(float[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length < 3) throw new ArgumentException("Array must contain at least 3 elements.", nameof(array));
            X = array[0];
            Y = array[1];
            Z = array[2];
        }

        public Float3(Float2 xy, float z) : this(xy.X, xy.Y, z) { }
        public Float3(float x, Float2 yz) : this(x, yz.X, yz.Y) { }
        public Float3(Double3 v) : this((float)v.X, (float)v.Y, (float)v.Z) { }
        public Float3(Int3 v) : this((float)v.X, (float)v.Y, (float)v.Z) { }

        public Float3(IEnumerable<float> values)
        {
            var array = values.ToArray();
            if (array.Length < 3) throw new ArgumentException("Collection must contain at least 3 elements.", nameof(values));
            X = array[0];
            Y = array[1];
            Z = array[2];
        }

        public Float3(ReadOnlySpan<float> span)
        {
            if (span.Length < 3) throw new ArgumentException("Span must contain at least 3 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
        }

        public Float3(Span<float> span)
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
        public static Float3 Normalize(Float3 v)
        {
            float length = Length(v);
            if (length > float.Epsilon)
                return v / length;
            return Zero;
        }

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Length(Float3 v) => Maths.Sqrt(LengthSquared(v));

        /// <summary>Returns the magnitude (length) of the given vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LengthSquared(Float3 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;

        /// <summary>Returns the angle in radians between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AngleBetween(Float3 a, Float3 b)
        {
            float dot = Dot(Normalize(a), Normalize(b));
            return Maths.Acos(Maths.Clamp(dot, -1f, 1f));
        }

        /// <summary>Returns the cross product of two Float3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Cross(Float3 x, Float3 y) => new Float3(
                x.Y * y.Z - x.Z * y.Y,
                x.Z * y.X - x.X * y.Z,
                x.X * y.Y - x.Y * y.X
            );

        /// <summary>Returns the distance between two Float3 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Float3 x, Float3 y) => Length(x - y);

        /// <summary>Returns the distance between two Float3 points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSquared(Float3 x, Float3 y) => LengthSquared(x - y);

        /// <summary>Returns the dot product of two Float3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Float3 x, Float3 y) => x.X * y.X + x.Y * y.Y + x.Z * y.Z;

        /// <summary>Checks if two vectors are parallel within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsParallel(Float3 a, Float3 b, float tolerance = 1e-6f)
        {
            float normalizedDot = MathF.Abs(Dot(Normalize(a), Normalize(b)));
            return normalizedDot >= 1f - tolerance;
        }

        /// <summary>Checks if two vectors are perpendicular within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPerpendicular(Float3 a, Float3 b, float tolerance = 1e-6f)
        {
            float dot = MathF.Abs(Dot(a, b));
            return dot <= tolerance;
        }

        /// <summary>Orthonormalizes a set of three vectors using Gram-Schmidt process.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OrthoNormalize(ref Float3 normal, ref Float3 tangent, ref Float3 binormal)
        {
            normal = Normalize(normal);
            tangent = Normalize(tangent - Project(tangent, normal));
            binormal = Cross(normal, tangent);
        }

        /// <summary>Orthonormalizes two vectors using Gram-Schmidt process.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OrthoNormalize(ref Float3 normal, ref Float3 tangent)
        {
            normal = Normalize(normal);
            tangent = Normalize(tangent - Project(tangent, normal));
        }

        /// <summary>Projects vector a onto vector b.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Project(Float3 a, Float3 b)
        {
            float denominator = Dot(b, b);
            if (denominator <= float.Epsilon)
                return Zero;
            return b * (Dot(a, b) / denominator);
        }

        /// <summary>Projects a vector onto a plane defined by a normal vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 ProjectOntoPlane(Float3 vector, Float3 planeNormal) => vector - Project(vector, planeNormal);

        /// <summary>Reflects a vector off a normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Reflect(Float3 vector, Float3 normal)
        {
            float dot = Dot(vector, normal);
            return vector - 2 * dot * normal;
        }

        /// <summary>Calculates the refraction direction for an incident vector and surface normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Refract(Float3 incident, Float3 normal, float eta)
        {
            float dotNI = Dot(normal, incident);
            float k = 1f - eta * eta * (1f - dotNI * dotNI);
            return k < 0f ? new Float3(0, 0, 0) : eta * incident - (eta * dotNI + Maths.Sqrt(k)) * normal;
        }

        /// <summary>Returns the signed angle in radians between two 3D vectors around a reference axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SignedAngleBetween(Float3 a, Float3 b, Float3 axis)
        {
            float angle = AngleBetween(a, b);
            Float3 cross = Cross(a, b);
            float sign = Dot(cross, axis) < 0f ? -1f : 1f;
            return angle * sign;
        }

        /// <summary>Spherically interpolates between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Slerp(Float3 a, Float3 b, float t)
        {
            t = Maths.Saturate(t);
            return SlerpUnclamped(a, b, t);
        }

        /// <summary>Spherically interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 SlerpUnclamped(Float3 a, Float3 b, float t)
        {
            Float3 result;

            // Normalize the vectors
            Float3 normalizedA = Normalize(a);
            Float3 normalizedB = Normalize(b);

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
        public static Float3 MoveTowards(Float3 current, Float3 target, float maxDistanceDelta)
        {
            Float3 toVector = target - current;
            float distance = Length(toVector);
            if (distance <= maxDistanceDelta || distance < float.Epsilon)
                return target;
            return current + toVector / distance * maxDistanceDelta;
        }

        #endregion


        #region Operators

        public static Float3 operator -(Float3 v) => new Float3(-v.X, -v.Y, -v.Z);

        public static Float3 operator +(Float3 a, Float3 b) => new Float3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Float3 operator -(Float3 a, Float3 b) => new Float3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Float3 operator *(Float3 a, Float3 b) => new Float3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public static Float3 operator /(Float3 a, Float3 b) => new Float3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
        public static Float3 operator %(Float3 a, Float3 b) => new Float3(a.X % b.X, a.Y % b.Y, a.Z % b.Z);

        public static Float3 operator +(Float3 v, float scalar) => new Float3(v.X + scalar, v.Y + scalar, v.Z + scalar);
        public static Float3 operator -(Float3 v, float scalar) => new Float3(v.X - scalar, v.Y - scalar, v.Z - scalar);
        public static Float3 operator *(Float3 v, float scalar) => new Float3(v.X * scalar, v.Y * scalar, v.Z * scalar);
        public static Float3 operator /(Float3 v, float scalar) => new Float3(v.X / scalar, v.Y / scalar, v.Z / scalar);
        public static Float3 operator %(Float3 v, float scalar) => new Float3(v.X % scalar, v.Y % scalar, v.Z % scalar);

        public static Float3 operator +(float scalar, Float3 v) => new Float3(scalar + v.X, scalar + v.Y, scalar + v.Z);
        public static Float3 operator -(float scalar, Float3 v) => new Float3(scalar - v.X, scalar - v.Y, scalar - v.Z);
        public static Float3 operator *(float scalar, Float3 v) => new Float3(scalar * v.X, scalar * v.Y, scalar * v.Z);
        public static Float3 operator /(float scalar, Float3 v) => new Float3(scalar / v.X, scalar / v.Y, scalar / v.Z);
        public static Float3 operator %(float scalar, Float3 v) => new Float3(scalar % v.X, scalar % v.Y, scalar % v.Z);

        #endregion


        #region Casting

        // System.Numerics Cast
        public static implicit operator Vector3(Float3 value) => new Vector3(value.X, value.Y, value.Z);

        public static implicit operator Float3(Vector3 value) => new Float3(value.X, value.Y, value.Z);


        public static explicit operator Float3(Float2 value) => new Float3(value.X, value.Y, 0f);

        public static explicit operator Float2(Float3 value) => new Float2(value.X, value.Y);

        public static explicit operator Float3(Double3 v) => new Float3(v);

        public static explicit operator Float3(Int3 v) => new Float3(v);

        #endregion


        #region Equals and GetHashCode

        public static bool operator ==(Float3 left, Float3 right) => left.Equals(right);
        public static bool operator !=(Float3 left, Float3 right) => !left.Equals(right);
        public override bool Equals(object? obj) => obj is Float3 && Equals((Float3)obj);
        public bool Equals(Float3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2);

        #endregion


        /// <summary>Returns an array of components.</summary>
        public float[] ToArray() => new float[] { X, Y, Z };

        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : ", ";
            return "(" + X.ToString(format, formatProvider) + separator + Y.ToString(format, formatProvider) + separator + Z.ToString(format, formatProvider) + ")";
        }
    }
}
