using System;

namespace Prowl.Recast.Core.Numerics
{
    public struct RcVec3i : IEquatable<RcVec3i>
    {
        public int X;
        public int Y;
        public int Z;

        public static RcVec3i Zero => new RcVec3i(0, 0, 0);
        public static RcVec3i UnitX => new RcVec3i(1, 0, 0);
        public static RcVec3i UnitY => new RcVec3i(0, 1, 0);
        public static RcVec3i UnitZ => new RcVec3i(0, 1, 1);

        // Comparison Operators
        public static bool operator ==(RcVec3i left, RcVec3i right) => left.Equals(right);
        public static bool operator !=(RcVec3i left, RcVec3i right) => !left.Equals(right);

        // Arithmetic Operators
        public static RcVec3i operator +(RcVec3i a, RcVec3i b) => new RcVec3i(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static RcVec3i operator -(RcVec3i a, RcVec3i b) => new RcVec3i(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static RcVec3i operator *(RcVec3i a, int scalar) => new RcVec3i(a.X * scalar, a.Y * scalar, a.Z * scalar);

        public RcVec3i(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int this[int index]
        {
            get
            {
                return index switch
                {
                    0 => X,
                    1 => Y,
                    2 => Z,
                    _ => throw new IndexOutOfRangeException()
                };
            }
        }


        public bool Equals(RcVec3i other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object? obj) // null 허용 확인
        {
            return obj is RcVec3i other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }
    }
}