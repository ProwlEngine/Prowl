using System;

namespace Prowl.Vector
{
    public struct IntRect
    {
        public Vector2Int Min; // Upper-left
        public Vector2Int Max; // Lower-right

        public static IntRect Empty {
            get {
                return CreateFromMinMax(
                    new Vector2Int(int.MaxValue, int.MaxValue),
                    new Vector2Int(int.MinValue, int.MinValue));
            }
        }

        public static IntRect Zero {
            get {
                return new IntRect(
                    new Vector2Int(0, 0),
                    new Vector2Int(0, 0));
            }
        }

        public int x {
            readonly get => Min.x;
            set {
                int width = Max.x - Min.x;
                Min.x = value;
                Max.x = value + width;
            }
        }

        public int y {
            readonly get => Min.y;
            set {
                int height = Max.y - Min.y;
                Min.y = value;
                Max.y = value + height;
            }
        }

        public Vector2Int Position {
            get => Min;
            set {
                Max += value - Min;
                Min = value;
            }
        }

        public readonly Vector2Int Center => new Vector2Int((Min.x + Max.x) / 2, (Min.y + Max.y) / 2);

        public int width {
            readonly get => Max.x - Min.x;
            set => Max.x = Min.x + value;
        }

        public int height {
            readonly get => Max.y - Min.y;
            set => Max.y = Min.y + value;
        }

        public readonly Vector2Int Size => new Vector2Int(width, height);

        public readonly int Left => Min.x;
        public readonly int Right => Max.x;
        public readonly int Top => Min.y;
        public readonly int Bottom => Max.y;
        public readonly Vector2Int TopLeft => new Vector2Int(Left, Top);
        public readonly Vector2Int MiddleLeft => new Vector2Int(Left, (Top + Bottom) / 2);
        public readonly Vector2Int TopRight => new Vector2Int(Right, Top);
        public readonly Vector2Int MiddleRight => new Vector2Int(Right, (Top + Bottom) / 2);
        public readonly Vector2Int BottomLeft => new Vector2Int(Left, Bottom);
        public readonly Vector2Int BottomRight => new Vector2Int(Right, Bottom);

        public IntRect(Vector2Int position, Vector2Int scale)
        {
            Min = position;
            Max = position + scale;
        }

        public IntRect(int x, int y, int width, int height) : this(new Vector2Int(x, y), new Vector2Int(width, height)) { }

        public bool Contains(Vector2Int p) { return p.x >= Min.x && p.y >= Min.y && p.x < Max.x && p.y < Max.y; }
        public bool Contains(IntRect r) { return r.Min.x >= Min.x && r.Min.y >= Min.y && r.Max.x < Max.x && r.Max.y < Max.y; }
        public bool Overlaps(IntRect r) { return r.Min.y < Max.y && r.Max.y > Min.y && r.Min.x < Max.x && r.Max.x > Min.x; }
        public void Add(Vector2Int rhs) { if (Min.x > rhs.x) Min.x = rhs.x; if (Min.y > rhs.y) Min.y = rhs.y; if (Max.x < rhs.x) Max.x = rhs.x; if (Max.y < rhs.y) Max.y = rhs.y; }
        public void Add(IntRect rhs) { if (Min.x > rhs.Min.x) Min.x = rhs.Min.x; if (Min.y > rhs.Min.y) Min.y = rhs.Min.y; if (Max.x < rhs.Max.x) Max.x = rhs.Max.x; if (Max.y < rhs.Max.y) Max.y = rhs.Max.y; }
        public void Expand(int amount) { Min.x -= amount; Min.y -= amount; Max.x += amount; Max.y += amount; }
        public void Expand(int horizontal, int vertical) { Min.x -= horizontal; Min.y -= vertical; Max.x += horizontal; Max.y += vertical; }
        public void Expand(Vector2Int amount) { Min.x -= amount.x; Min.y -= amount.y; Max.x += amount.x; Max.y += amount.y; }
        public void Reduce(Vector2Int amount) { Min.x += amount.x; Min.y += amount.y; Max.x -= amount.x; Max.y -= amount.y; }
        public void Clip(IntRect clip) { if (Min.x < clip.Min.x) Min.x = clip.Min.x; if (Min.y < clip.Min.y) Min.y = clip.Min.y; if (Max.x > clip.Max.x) Max.x = clip.Max.x; if (Max.y > clip.Max.y) Max.y = clip.Max.y; }

        public static bool IntersectRect(IntRect Left, IntRect Right, ref IntRect Result)
        {
            if (!Left.Overlaps(Right))
                return false;

            Result = CreateWithBoundary(
                Math.Max(Left.Left, Right.Left),
                Math.Max(Left.Top, Right.Top),
                Math.Min(Left.Right, Right.Right),
                Math.Min(Left.Bottom, Right.Bottom));
            return true;
        }

        public static IntRect CombineRect(IntRect a, IntRect b)
        {
            IntRect result = new IntRect();
            result.Min.x = Math.Min(a.Min.x, b.Min.x);
            result.Min.y = Math.Min(a.Min.y, b.Min.y);
            result.Max.x = Math.Max(a.Max.x, b.Max.x);
            result.Max.y = Math.Max(a.Max.y, b.Max.y);
            return result;
        }

        public Vector2Int GetClosestPoint(Vector2Int p, bool on_edge)
        {
            if (!on_edge && Contains(p))
                return p;
            if (p.x > Max.x) p.x = Max.x;
            else if (p.x < Min.x) p.x = Min.x;
            if (p.y > Max.y) p.y = Max.y;
            else if (p.y < Min.y) p.y = Min.y;
            return p;
        }

        public override string ToString()
        {
            return $"{{ Min: {Min}, Max: {Max} }}";
        }

        public static IntRect CreateFromMinMax(Vector2Int min, Vector2Int max) => new IntRect(min, max - min);

        public static IntRect CreateWithCenter(Vector2Int CenterPos, Vector2Int Size)
        {
            return new IntRect(CenterPos.x - Size.x / 2, CenterPos.y - Size.y / 2, Size.x, Size.y);
        }

        public static IntRect CreateWithCenter(int CenterX, int CenterY, int Width, int Height)
        {
            return new IntRect(CenterX - Width / 2, CenterY - Height / 2, Width, Height);
        }

        public static IntRect CreateWithBoundary(int Left, int Top, int Right, int Bottom)
        {
            return new IntRect(Left, Top, Right - Left, Bottom - Top);
        }

        public static bool operator ==(IntRect a, IntRect b) => a.Min == b.Min && a.Max == b.Max;
        public static bool operator !=(IntRect a, IntRect b) => a.Min != b.Min || a.Max != b.Max;

        public override bool Equals(object? obj) => obj is IntRect r && r == this;
        public override int GetHashCode() => Min.GetHashCode() ^ Max.GetHashCode();
    }
}
