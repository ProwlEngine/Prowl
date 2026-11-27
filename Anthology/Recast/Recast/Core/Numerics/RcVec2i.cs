using System;

namespace Prowl.Recast.Core.Numerics
{
    public struct RcVec2i : IEquatable<RcVec2i>
    {
        public int X;
        public int Y;

        public static RcVec2i Zero => new RcVec2i(0, 0);
        public static RcVec2i UnitX => new RcVec2i(1, 0);
        public static RcVec2i UnitY => new RcVec2i(0, 1);

        // Comparison Operators
        public static bool operator ==(RcVec2i left, RcVec2i right) => left.Equals(right);
        public static bool operator !=(RcVec2i left, RcVec2i right) => !left.Equals(right);

        // Arithmetic Operators
        public static RcVec2i operator +(RcVec2i a, RcVec2i b) => new RcVec2i(a.X + b.X, a.Y + b.Y);
        public static RcVec2i operator -(RcVec2i a, RcVec2i b) => new RcVec2i(a.X - b.X, a.Y - b.Y);
        public static RcVec2i operator *(RcVec2i a, int scalar) => new RcVec2i(a.X * scalar, a.Y * scalar);

        public RcVec2i(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int this[int index]
        {
            get
            {
                return index switch
                {
                    0 => X,
                    1 => Y,
                    _ => throw new IndexOutOfRangeException()
                };
            }
        }


        public bool Equals(RcVec2i other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object? obj)
        {
            return obj is RcVec2i other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }
    }

}