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
            get => ToEulerDegrees(EulerOrder.ZXYr);
            set => this = Maths.FromEulerDegrees(value, EulerOrder.ZXYr);
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
        /// Returns the Euler angle representation of the quaternion in radians.
        /// </summary>
        /// <param name="order">The desired order of Euler angles.</param>
        /// <returns>A Float3 vector of Euler angles in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 ToEuler(EulerOrder order) => Maths.ToEuler(this, order);

        /// <summary>
        /// Returns the Euler angle representation of the quaternion in degrees.
        /// </summary>
        /// <param name="order">The desired order of Euler angles.</param>
        /// <returns>A Float3 vector of Euler angles in degrees.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 ToEulerDegrees(EulerOrder order) => Maths.ToEulerDegrees(this, order);

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
