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
    /// Represents a 2-component vector using int precision.
    /// </summary>
    [System.Serializable]
    public partial struct Int2 : IEquatable<Int2>, IFormattable
    {
        /// <summary>Gets the zero vector.</summary>
        public static Int2 Zero => new Int2(0, 0);
        /// <summary>Gets the one vector.</summary>
        public static Int2 One => new Int2(1, 1);
        /// <summary>Gets the unit vector along the X-axis.</summary>
        public static Int2 UnitX => new Int2(1, 0);
        /// <summary>Gets the unit vector along the Y-axis.</summary>
        public static Int2 UnitY => new Int2(0, 1);


        public int X, Y;


        #region Properties

        /// <summary>Gets or sets the component at the specified index.</summary>
        public int this[int index]
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

        public Int2(int scalar) : this(scalar, scalar) { }
        public Int2(int x, int y) { X = x; Y = y; }
        public Int2(Int2 v) : this(v.X, v.Y) { }
        public Int2(int[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length < 2) throw new ArgumentException("Array must contain at least 2 elements.", nameof(array));
            X = array[0];
            Y = array[1];
        }

        public Int2(Float2 v) : this((int)v.X, (int)v.Y) { }
        public Int2(Double2 v) : this((int)v.X, (int)v.Y) { }

        public Int2(IEnumerable<int> values)
        {
            var array = values.ToArray();
            if (array.Length < 2) throw new ArgumentException("Collection must contain at least 2 elements.", nameof(values));
            X = array[0];
            Y = array[1];
        }

        public Int2(ReadOnlySpan<int> span)
        {
            if (span.Length < 2) throw new ArgumentException("Span must contain at least 2 elements.", nameof(span));
            X = span[0];
            Y = span[1];
        }

        public Int2(Span<int> span)
        {
            if (span.Length < 2) throw new ArgumentException("Span must contain at least 2 elements.", nameof(span));
            X = span[0];
            Y = span[1];
        }

        #endregion


        #region Static Methods

        /// <summary>Returns the dot product of two Int2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Dot(Int2 x, Int2 y) => x.X * y.X + x.Y * y.Y;

        #endregion


        #region Operators

        public static Int2 operator -(Int2 v) => new Int2(-v.X, -v.Y);

        public static Int2 operator +(Int2 a, Int2 b) => new Int2(a.X + b.X, a.Y + b.Y);
        public static Int2 operator -(Int2 a, Int2 b) => new Int2(a.X - b.X, a.Y - b.Y);
        public static Int2 operator *(Int2 a, Int2 b) => new Int2(a.X * b.X, a.Y * b.Y);
        public static Int2 operator /(Int2 a, Int2 b) => new Int2(a.X / b.X, a.Y / b.Y);
        public static Int2 operator %(Int2 a, Int2 b) => new Int2(a.X % b.X, a.Y % b.Y);

        // Bitwise Operators
        public static Int2 operator &(Int2 a, Int2 b) => new Int2(a.X & b.X, a.Y & b.Y);
        public static Int2 operator |(Int2 a, Int2 b) => new Int2(a.X | b.X, a.Y | b.Y);
        public static Int2 operator ^(Int2 a, Int2 b) => new Int2(a.X ^ b.X, a.Y ^ b.Y);
        public static Int2 operator ~(Int2 v) => new Int2(~v.X, ~v.Y);
        public static Int2 operator <<(Int2 a, int b) => new Int2(a.X << b, a.Y << b);
        public static Int2 operator >>(Int2 a, int b) => new Int2(a.X >> b, a.Y >> b);

        public static Int2 operator +(Int2 v, int scalar) => new Int2(v.X + scalar, v.Y + scalar);
        public static Int2 operator -(Int2 v, int scalar) => new Int2(v.X - scalar, v.Y - scalar);
        public static Int2 operator *(Int2 v, int scalar) => new Int2(v.X * scalar, v.Y * scalar);
        public static Int2 operator /(Int2 v, int scalar) => new Int2(v.X / scalar, v.Y / scalar);
        public static Int2 operator %(Int2 v, int scalar) => new Int2(v.X % scalar, v.Y % scalar);

        public static Int2 operator +(int scalar, Int2 v) => new Int2(scalar + v.X, scalar + v.Y);
        public static Int2 operator -(int scalar, Int2 v) => new Int2(scalar - v.X, scalar - v.Y);
        public static Int2 operator *(int scalar, Int2 v) => new Int2(scalar * v.X, scalar * v.Y);
        public static Int2 operator /(int scalar, Int2 v) => new Int2(scalar / v.X, scalar / v.Y);
        public static Int2 operator %(int scalar, Int2 v) => new Int2(scalar % v.X, scalar % v.Y);

        #endregion


        #region Casting

        public static explicit operator Int2(Float2 v) => new Int2(v);

        public static explicit operator Int2(Double2 v) => new Int2(v);

        #endregion


        #region Equals and GetHashCode

        public static bool operator ==(Int2 left, Int2 right) { return left.Equals(right); }
        public static bool operator !=(Int2 left, Int2 right) { return !left.Equals(right); }
        public override bool Equals(object? obj) => obj is Int2 && Equals((Int2)obj);
        public bool Equals(Int2 other) => X == other.X && Y == other.Y;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2);

        #endregion


        /// <summary>Returns an array of components.</summary>
        public int[] ToArray() => new int[] { X, Y };

        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : ", ";
            return "(" + X.ToString(format, formatProvider) + separator + Y.ToString(format, formatProvider) + ")";
        }
    }
}
