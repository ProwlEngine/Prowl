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
    /// Represents a 3-component vector using int precision.
    /// </summary>
    [System.Serializable]
    public partial struct Int3 : IEquatable<Int3>, IFormattable
    {
        /// <summary>Gets the zero vector.</summary>
        public static Int3 Zero => new Int3(0, 0, 0);
        /// <summary>Gets the one vector.</summary>
        public static Int3 One => new Int3(1, 1, 1);
        /// <summary>Gets the unit vector along the X-axis.</summary>
        public static Int3 UnitX => new Int3(1, 0, 0);
        /// <summary>Gets the unit vector along the Y-axis.</summary>
        public static Int3 UnitY => new Int3(0, 1, 0);
        /// <summary>Gets the unit vector along the Z-axis.</summary>
        public static Int3 UnitZ => new Int3(0, 0, 1);


        public int X, Y, Z;


        #region Properties

        /// <summary>Gets or sets the component at the specified index.</summary>
        public int this[int index]
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

        public Int3(int scalar) : this(scalar, scalar, scalar) { }
        public Int3(int x, int y, int z) { X = x; Y = y; Z = z; }
        public Int3(Int3 v) : this(v.X, v.Y, v.Z) { }
        public Int3(int[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length < 3) throw new ArgumentException("Array must contain at least 3 elements.", nameof(array));
            X = array[0];
            Y = array[1];
            Z = array[2];
        }

        public Int3(Int2 xy, int z) : this(xy.X, xy.Y, z) { }
        public Int3(int x, Int2 yz) : this(x, yz.X, yz.Y) { }
        public Int3(Float3 v) : this((int)v.X, (int)v.Y, (int)v.Z) { }
        public Int3(Double3 v) : this((int)v.X, (int)v.Y, (int)v.Z) { }

        public Int3(IEnumerable<int> values)
        {
            var array = values.ToArray();
            if (array.Length < 3) throw new ArgumentException("Collection must contain at least 3 elements.", nameof(values));
            X = array[0];
            Y = array[1];
            Z = array[2];
        }

        public Int3(ReadOnlySpan<int> span)
        {
            if (span.Length < 3) throw new ArgumentException("Span must contain at least 3 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
        }

        public Int3(Span<int> span)
        {
            if (span.Length < 3) throw new ArgumentException("Span must contain at least 3 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
        }

        #endregion


        #region Static Methods

        /// <summary>Returns the dot product of two Int3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Dot(Int3 x, Int3 y) => x.X * y.X + x.Y * y.Y + x.Z * y.Z;

        #endregion


        #region Operators

        public static Int3 operator -(Int3 v) => new Int3(-v.X, -v.Y, -v.Z);

        public static Int3 operator +(Int3 a, Int3 b) => new Int3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Int3 operator -(Int3 a, Int3 b) => new Int3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Int3 operator *(Int3 a, Int3 b) => new Int3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public static Int3 operator /(Int3 a, Int3 b) => new Int3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
        public static Int3 operator %(Int3 a, Int3 b) => new Int3(a.X % b.X, a.Y % b.Y, a.Z % b.Z);

        // Bitwise Operators
        public static Int3 operator &(Int3 a, Int3 b) => new Int3(a.X & b.X, a.Y & b.Y, a.Z & b.Z);
        public static Int3 operator |(Int3 a, Int3 b) => new Int3(a.X | b.X, a.Y | b.Y, a.Z | b.Z);
        public static Int3 operator ^(Int3 a, Int3 b) => new Int3(a.X ^ b.X, a.Y ^ b.Y, a.Z ^ b.Z);
        public static Int3 operator ~(Int3 v) => new Int3(~v.X, ~v.Y, ~v.Z);
        public static Int3 operator <<(Int3 a, int b) => new Int3(a.X << b, a.Y << b, a.Z << b);
        public static Int3 operator >>(Int3 a, int b) => new Int3(a.X >> b, a.Y >> b, a.Z >> b);

        public static Int3 operator +(Int3 v, int scalar) => new Int3(v.X + scalar, v.Y + scalar, v.Z + scalar);
        public static Int3 operator -(Int3 v, int scalar) => new Int3(v.X - scalar, v.Y - scalar, v.Z - scalar);
        public static Int3 operator *(Int3 v, int scalar) => new Int3(v.X * scalar, v.Y * scalar, v.Z * scalar);
        public static Int3 operator /(Int3 v, int scalar) => new Int3(v.X / scalar, v.Y / scalar, v.Z / scalar);
        public static Int3 operator %(Int3 v, int scalar) => new Int3(v.X % scalar, v.Y % scalar, v.Z % scalar);

        public static Int3 operator +(int scalar, Int3 v) => new Int3(scalar + v.X, scalar + v.Y, scalar + v.Z);
        public static Int3 operator -(int scalar, Int3 v) => new Int3(scalar - v.X, scalar - v.Y, scalar - v.Z);
        public static Int3 operator *(int scalar, Int3 v) => new Int3(scalar * v.X, scalar * v.Y, scalar * v.Z);
        public static Int3 operator /(int scalar, Int3 v) => new Int3(scalar / v.X, scalar / v.Y, scalar / v.Z);
        public static Int3 operator %(int scalar, Int3 v) => new Int3(scalar % v.X, scalar % v.Y, scalar % v.Z);

        #endregion


        #region Casting

        public static explicit operator Int3(Int2 value) => new Int3(value.X, value.Y, 0);

        public static explicit operator Int2(Int3 value) => new Int2(value.X, value.Y);

        public static explicit operator Int3(Float3 v) => new Int3(v);

        public static explicit operator Int3(Double3 v) => new Int3(v);

        #endregion


        #region Equals and GetHashCode
        public static bool operator ==(Int3 left, Int3 right) => left.Equals(right);
        public static bool operator !=(Int3 left, Int3 right) => !left.Equals(right);
        public override bool Equals(object? obj) => obj is Int3 && Equals((Int3)obj);
        public bool Equals(Int3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2);
        #endregion


        /// <summary>Returns an array of components.</summary>
        public int[] ToArray() => new int[] { X, Y, Z };

        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : ", ";
            return "(" + X.ToString(format, formatProvider) + separator + Y.ToString(format, formatProvider) + separator + Z.ToString(format, formatProvider) + ")";
        }
    }
}
