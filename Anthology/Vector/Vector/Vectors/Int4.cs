using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    /// <summary>
    /// Represents a 4-component vector using int precision.
    /// </summary>
    [System.Serializable]
    public partial struct Int4 : IEquatable<Int4>, IFormattable
    {
        /// <summary>Gets the zero vector.</summary>
        public static Int4 Zero => new Int4(0, 0, 0, 0);
        /// <summary>Gets the one vector.</summary>
        public static Int4 One => new Int4(1, 1, 1, 1);
        /// <summary>Gets the unit vector along the X-axis.</summary>
        public static Int4 UnitX => new Int4(1, 0, 0, 0);
        /// <summary>Gets the unit vector along the Y-axis.</summary>
        public static Int4 UnitY => new Int4(0, 1, 0, 0);
        /// <summary>Gets the unit vector along the Z-axis.</summary>
        public static Int4 UnitZ => new Int4(0, 0, 1, 0);
        /// <summary>Gets the unit vector along the W-axis.</summary>
        public static Int4 UnitW => new Int4(0, 0, 0, 1);


        public int X, Y, Z, W;


        #region Properties

        /// <summary>Gets or sets the component at the specified index.</summary>
        public int this[int index]
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

        public Int4(int scalar) : this(scalar, scalar, scalar, scalar) { }
        public Int4(int x, int y, int z, int w) { X = x; Y = y; Z = z; W = w; }
        public Int4(Int4 v) : this(v.X, v.Y, v.Z, v.W) { }
        public Int4(int[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length < 4) throw new ArgumentException("Array must contain at least 4 elements.", nameof(array));
            X = array[0];
            Y = array[1];
            Z = array[2];
            W = array[3];
        }

        public Int4(Int2 xy, int z, int w) : this(xy.X, xy.Y, z, w) { }
        public Int4(int x, Int2 yz, int w) : this(x, yz.X, yz.Y, w) { }
        public Int4(int x, int y, Int2 zw) : this(x, y, zw.X, zw.Y) { }
        public Int4(Int2 xy, Int2 zw) : this(xy.X, xy.Y, zw.X, zw.Y) { }
        public Int4(Int3 xyz, int w) : this(xyz.X, xyz.Y, xyz.Z, w) { }
        public Int4(int x, Int3 yzw) : this(x, yzw.X, yzw.Y, yzw.Z) { }

        public Int4(Float4 v) : this((int)v.X, (int)v.Y, (int)v.Z, (int)v.W) { }
        public Int4(Double4 v) : this((int)v.X, (int)v.Y, (int)v.Z, (int)v.W) { }

        public Int4(IEnumerable<int> values)
        {
            var array = values.ToArray();
            if (array.Length < 4) throw new ArgumentException("Collection must contain at least 4 elements.", nameof(values));
            X = array[0];
            Y = array[1];
            Z = array[2];
            W = array[3];
        }

        public Int4(ReadOnlySpan<int> span)
        {
            if (span.Length < 4) throw new ArgumentException("Span must contain at least 4 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
            W = span[3];
        }

        public Int4(Span<int> span)
        {
            if (span.Length < 4) throw new ArgumentException("Span must contain at least 4 elements.", nameof(span));
            X = span[0];
            Y = span[1];
            Z = span[2];
            W = span[3];
        }

        #endregion


        #region Static Methods

        /// <summary>Returns the dot product of two Int4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Dot(Int4 x, Int4 y) => x.X * y.X + x.Y * y.Y + x.Z * y.Z + x.W * y.W;

        #endregion


        #region Operators

        public static Int4 operator -(Int4 v) => new Int4(-v.X, -v.Y, -v.Z, -v.W);

        public static Int4 operator +(Int4 a, Int4 b) => new Int4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
        public static Int4 operator -(Int4 a, Int4 b) => new Int4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
        public static Int4 operator *(Int4 a, Int4 b) => new Int4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
        public static Int4 operator /(Int4 a, Int4 b) => new Int4(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
        public static Int4 operator %(Int4 a, Int4 b) => new Int4(a.X % b.X, a.Y % b.Y, a.Z % b.Z, a.W % b.W);

        // Bitwise Operators
        public static Int4 operator &(Int4 a, Int4 b) => new Int4(a.X & b.X, a.Y & b.Y, a.Z & b.Z, a.W & b.W);
        public static Int4 operator |(Int4 a, Int4 b) => new Int4(a.X | b.X, a.Y | b.Y, a.Z | b.Z, a.W | b.W);
        public static Int4 operator ^(Int4 a, Int4 b) => new Int4(a.X ^ b.X, a.Y ^ b.Y, a.Z ^ b.Z, a.W ^ b.W);
        public static Int4 operator ~(Int4 v) => new Int4(~v.X, ~v.Y, ~v.Z, ~v.W);
        public static Int4 operator <<(Int4 a, int b) => new Int4(a.X << b, a.Y << b, a.Z << b, a.W << b);
        public static Int4 operator >>(Int4 a, int b) => new Int4(a.X >> b, a.Y >> b, a.Z >> b, a.W >> b);

        public static Int4 operator +(Int4 v, int scalar) => new Int4(v.X + scalar, v.Y + scalar, v.Z + scalar, v.W + scalar);
        public static Int4 operator -(Int4 v, int scalar) => new Int4(v.X - scalar, v.Y - scalar, v.Z - scalar, v.W - scalar);
        public static Int4 operator *(Int4 v, int scalar) => new Int4(v.X * scalar, v.Y * scalar, v.Z * scalar, v.W * scalar);
        public static Int4 operator /(Int4 v, int scalar) => new Int4(v.X / scalar, v.Y / scalar, v.Z / scalar, v.W / scalar);
        public static Int4 operator %(Int4 v, int scalar) => new Int4(v.X % scalar, v.Y % scalar, v.Z % scalar, v.W % scalar);

        public static Int4 operator +(int scalar, Int4 v) => new Int4(scalar + v.X, scalar + v.Y, scalar + v.Z, scalar + v.W);
        public static Int4 operator -(int scalar, Int4 v) => new Int4(scalar - v.X, scalar - v.Y, scalar - v.Z, scalar - v.W);
        public static Int4 operator *(int scalar, Int4 v) => new Int4(scalar * v.X, scalar * v.Y, scalar * v.Z, scalar * v.W);
        public static Int4 operator /(int scalar, Int4 v) => new Int4(scalar / v.X, scalar / v.Y, scalar / v.Z, scalar / v.W);
        public static Int4 operator %(int scalar, Int4 v) => new Int4(scalar % v.X, scalar % v.Y, scalar % v.Z, scalar % v.W);

        #endregion


        #region Casting

        public static implicit operator Int4(Int2 value) => new Int4(value.X, value.Y, 0, 0);

        public static implicit operator Int4(Int3 value) => new Int4(value.X, value.Y, value.Z, 0);

        public static explicit operator Int2(Int4 value) => new Int2(value.X, value.Y);

        public static explicit operator Int3(Int4 value) => new Int3(value.X, value.Y, value.Z);


        public static explicit operator Int4(Float4 v) => new Int4(v);

        public static explicit operator Int4(Double4 v) => new Int4(v);

        #endregion


        #region Equals and GetHashCode
        public static bool operator ==(Int4 left, Int4 right) { return left.Equals(right); }
        public static bool operator !=(Int4 left, Int4 right) { return !left.Equals(right); }
        public override bool Equals(object? obj) => obj is Int4 && Equals((Int4)obj);
        public bool Equals(Int4 other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2) ^ (W.GetHashCode() >> 1);
        #endregion


        /// <summary>Returns an array of components.</summary>
        public int[] ToArray() => new int[] { X, Y, Z, W };

        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = (formatProvider is CultureInfo) ? ((CultureInfo)formatProvider).TextInfo.ListSeparator : ", ";
            return "(" + X.ToString(format, formatProvider) + separator + Y.ToString(format, formatProvider) + separator + Z.ToString(format, formatProvider) + separator + W.ToString(format, formatProvider) + ")";
        }
    }
}
