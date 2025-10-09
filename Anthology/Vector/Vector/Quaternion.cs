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
        /// <summary>The X component of the quaternion.</summary>
        public float X;
        /// <summary>The Y component of the quaternion.</summary>
        public float Y;
        /// <summary>The Z component of the quaternion.</summary>
        public float Z;
        /// <summary>The W component of the quaternion (scalar part).</summary>
        public float W;

        /// <summary>
        /// Gets or sets the rotation as Euler angles in degrees, using the ZXYr order.
        /// This is useful for editor inspectors and simple rotation control.
        /// </summary>
        public Float3 eulerAngles
        {
            get => ToEulerDegrees();
            set => this = Maths.FromEulerDegrees(value);
        }

        /// <summary>A quaternion representing the identity transform (no rotation).</summary>
        public static readonly Quaternion Identity = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

        /// <summary>Constructs a quaternion from four float values.</summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <param name="z">The Z component.</param>
        /// <param name="w">The W component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>Constructs a quaternion from a Float4 vector (xyzw).</summary>
        /// <param name="value">The Float4 vector containing xyzw components.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(Float4 value)
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
            W = value.W;
        }

        /// <summary>Constructs a quaternion from a vector part (Float3) and a scalar part (float).</summary>
        /// <param name="vectorPart">The vector part (xyz).</param>
        /// <param name="scalarPart">The scalar part (w).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(Float3 vectorPart, float scalarPart)
        {
            X = vectorPart.X;
            Y = vectorPart.Y;
            Z = vectorPart.Z;
            W = scalarPart;
        }

        /// <summary>Gets or sets the component at the specified index (0=X, 1=Y, 2=Z, 3=W).</summary>
        public float this[int index]
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

        /// <summary>
        /// Flips the sign of each component of the quaternion.
        /// </summary>
        /// <param name="value">The source Quaternion.</param>
        /// <returns>The negated Quaternion.</returns>
        public static Quaternion operator -(Quaternion value)
        {
            Quaternion ans;

            ans.X = -value.X;
            ans.Y = -value.Y;
            ans.Z = -value.Z;
            ans.W = -value.W;

            return ans;
        }

        /// <summary>
        /// Adds two Quaternions element-by-element.
        /// </summary>
        /// <param name="value1">The first source Quaternion.</param>
        /// <param name="value2">The second source Quaternion.</param>
        /// <returns>The result of adding the Quaternions.</returns>
        public static Quaternion operator +(Quaternion value1, Quaternion value2)
        {
            Quaternion ans;

            ans.X = value1.X + value2.X;
            ans.Y = value1.Y + value2.Y;
            ans.Z = value1.Z + value2.Z;
            ans.W = value1.W + value2.W;

            return ans;
        }

        /// <summary>
        /// Subtracts one Quaternion from another.
        /// </summary>
        /// <param name="value1">The first source Quaternion.</param>
        /// <param name="value2">The second Quaternion, to be subtracted from the first.</param>
        /// <returns>The result of the subtraction.</returns>
        public static Quaternion operator -(Quaternion value1, Quaternion value2)
        {
            Quaternion ans;

            ans.X = value1.X - value2.X;
            ans.Y = value1.Y - value2.Y;
            ans.Z = value1.Z - value2.Z;
            ans.W = value1.W - value2.W;

            return ans;
        }

        public static Quaternion operator *(Quaternion lhs, float scalar)
        {
            Quaternion ans;

            ans.X = lhs.X * scalar;
            ans.Y = lhs.Y * scalar;
            ans.Z = lhs.Z * scalar;
            ans.W = lhs.W * scalar;

            return ans;
        }

        public static Quaternion operator *(Quaternion lhs, Quaternion rhs)
        {
            return new Quaternion(lhs.W * rhs.X + lhs.X * rhs.W + lhs.Y * rhs.Z - lhs.Z * rhs.Y,
                                   lhs.W * rhs.Y + lhs.Y * rhs.W + lhs.Z * rhs.X - lhs.X * rhs.Z,
                                   lhs.W * rhs.Z + lhs.Z * rhs.W + lhs.X * rhs.Y - lhs.Y * rhs.X,
                                   lhs.W * rhs.W - lhs.X * rhs.X - lhs.Y * rhs.Y - lhs.Z * rhs.Z);
        }

        public static Float3 operator *(Quaternion rotation, Float3 point)
        {
            float num = rotation.X * 2;
            float num2 = rotation.Y * 2;
            float num3 = rotation.Z * 2;
            float num4 = rotation.X * num;
            float num5 = rotation.Y * num2;
            float num6 = rotation.Z * num3;
            float num7 = rotation.X * num2;
            float num8 = rotation.X * num3;
            float num9 = rotation.Y * num3;
            float num10 = rotation.W * num;
            float num11 = rotation.W * num2;
            float num12 = rotation.W * num3;
            Float3 result;
            result.X = (1f - (num5 + num6)) * point.X + (num7 - num12) * point.Y + (num8 + num11) * point.Z;
            result.Y = (num7 + num12) * point.X + (1f - (num4 + num6)) * point.Y + (num9 - num10) * point.Z;
            result.Z = (num8 - num11) * point.X + (num9 + num10) * point.Y + (1f - (num4 + num5)) * point.Z;
            return result;
        }

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

        /// <summary>
        /// Returns the Euler angle representation of the quaternion in radians.
        /// </summary>
        /// <returns>A Float3 vector of Euler angles in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 ToEuler() => Maths.ToEuler(this);

        /// <summary>
        /// Returns the Euler angle representation of the quaternion in degrees.
        /// </summary>
        /// <returns>A Float3 vector of Euler angles in degrees.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 ToEulerDegrees() => Maths.ToEulerDegrees(this);

        /// <summary>Implicitly converts a Float4 vector to a Quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Quaternion(Float4 v) => new Quaternion(v);

        /// <summary>Explicitly converts a Quaternion to a Float4 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Float4(Quaternion q) => new Float4(q.X, q.Y, q.Z, q.W);


        /// <summary>Checks if this quaternion is equal to another quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Quaternion other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;

        /// <summary>Checks if this quaternion is equal to an object.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);

        /// <summary>Gets the hash code for this quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

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

        /// <summary>Component-wise equality comparison with a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ApproximatelyEquals(Quaternion other, float tolerance = 1e-6f)
        {
            return Maths.Abs(X - other.X) <= tolerance &&
                   Maths.Abs(Y - other.Y) <= tolerance &&
                   Maths.Abs(Z - other.Z) <= tolerance &&
                   Maths.Abs(W - other.W) <= tolerance;
        }
    }
}
