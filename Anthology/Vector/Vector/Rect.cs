// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

#region License
/*
MIT License
Copyright © 2006 The Mono.Xna Team

All rights reserved.

Authors:
Olivier Dufour (Duff)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
#endregion License

namespace Prowl.Vector
{
    public struct Rect
    {
        public Vector2 Min; // Upper-left
        public Vector2 Max; // Lower-right

        public static Rect Empty
        {
            get
            {
                return CreateFromMinMax(
                    new Vector2(double.MaxValue, double.MaxValue),
                    new Vector2(double.MinValue, double.MinValue));
            }
        }
        public static Rect Zero
        {
            get
            {
                return new Rect(
                    new Vector2(0, 0),
                    new Vector2(0, 0));
            }
        }

        public double x
        {
            readonly get => Min.x;
            set
            {
                double width = Max.x - Min.x;
                Min.x = value;
                Max.x = value + width;
            }
        }

        public double y
        {
            readonly get => Min.y;
            set
            {
                double height = Max.y - Min.y;
                Min.y = value;
                Max.y = value + height;
            }
        }
        public Vector2 Position
        {
            get => Min;
            set
            {
                Max += value - Min;
                Min = value;
            }
        }
        public readonly Vector2 Center => new Vector2((Min.x + Max.x) / 2, (Min.y + Max.y) / 2);

        public double width
        {
            readonly get => Max.x - Min.x;
            set => Max.x = Min.x + value;
        }

        public double height
        {
            readonly get => Max.y - Min.y;
            set => Max.y = Min.y + value;
        }
        public readonly Vector2 Size => new Vector2(width, height);

        public readonly double Left => Min.x;
        public readonly double Right => Max.x;
        public readonly double Top => Min.y;
        public readonly double Bottom => Max.y;
        public readonly Vector2 TopLeft => new Vector2(Left, Top);
        public readonly Vector2 MiddleLeft => new Vector2(Left, (Top + Bottom) / 2);
        public readonly Vector2 TopRight => new Vector2(Right, Top);
        public readonly Vector2 MiddleRight => new Vector2(Right, (Top + Bottom) / 2);
        public readonly Vector2 BottomLeft => new Vector2(Left, Bottom);
        public readonly Vector2 BottomRight => new Vector2(Right, Bottom);

        public Rect(Vector2 position, Vector2 scale)
        {
            Min = position;
            Max = position + scale;
        }

        public Rect(double x, double y, double width, double height) : this(new Vector2(x, y), new Vector2(width, height)) { }

        public bool Contains(Vector2 p) { return p.x >= Min.x && p.y >= Min.y && p.x < Max.x && p.y < Max.y; }
        public bool Contains(Rect r) { return r.Min.x >= Min.x && r.Min.y >= Min.y && r.Max.x < Max.x && r.Max.y < Max.y; }
        public bool Overlaps(Rect r) { return r.Min.y < Max.y && r.Max.y > Min.y && r.Min.x < Max.x && r.Max.x > Min.x; }
        public void Add(Vector2 rhs) { if (Min.x > rhs.x) Min.x = rhs.x; if (Min.y > rhs.y) Min.y = rhs.y; if (Max.x < rhs.x) Max.x = rhs.x; if (Max.y < rhs.y) Max.y = rhs.y; }
        public void Add(Rect rhs) { if (Min.x > rhs.Min.x) Min.x = rhs.Min.x; if (Min.y > rhs.Min.y) Min.y = rhs.Min.y; if (Max.x < rhs.Max.x) Max.x = rhs.Max.x; if (Max.y < rhs.Max.y) Max.y = rhs.Max.y; }
        public void Expand(float amount) { Min.x -= amount; Min.y -= amount; Max.x += amount; Max.y += amount; }
        public void Expand(float horizontal, float vertical) { Min.x -= horizontal; Min.y -= vertical; Max.x += horizontal; Max.y += vertical; }
        public void Expand(Vector2 amount) { Min.x -= amount.x; Min.y -= amount.y; Max.x += amount.x; Max.y += amount.y; }
        public void Reduce(Vector2 amount) { Min.x += amount.x; Min.y += amount.y; Max.x -= amount.x; Max.y -= amount.y; }
        public void Clip(Rect clip) { if (Min.x < clip.Min.x) Min.x = clip.Min.x; if (Min.y < clip.Min.y) Min.y = clip.Min.y; if (Max.x > clip.Max.x) Max.x = clip.Max.x; if (Max.y > clip.Max.y) Max.y = clip.Max.y; }
        public void Round() { Min.x = (float)(int)Min.x; Min.y = (float)(int)Min.y; Max.x = (float)(int)Max.x; Max.y = (float)(int)Max.y; }

        public static bool IntersectRect(Rect Left, Rect Right, ref Rect Result)
        {
            if (!Left.Overlaps(Right))
                return false;

            Result = CreateWithBoundary(
                MathD.Max(Left.Left, Right.Left),
                MathD.Max(Left.Top, Right.Top),
                MathD.Min(Left.Right, Right.Right),
                MathD.Min(Left.Bottom, Right.Bottom));
            return true;
        }

        public static Rect CombineRect(Rect a, Rect b)
        {
            Rect result = new Rect();
            result.Min.x = MathD.Min(a.Min.x, b.Min.x);
            result.Min.y = MathD.Min(a.Min.y, b.Min.y);
            result.Max.x = MathD.Max(a.Max.x, b.Max.x);
            result.Max.y = MathD.Max(a.Max.y, b.Max.y);
            return result;
        }

        public Vector2 GetClosestPoint(Vector2 p, bool on_edge)
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

        public static Rect CreateFromMinMax(Vector2 min, Vector2 max) => new Rect(min, max - min);

        public static Rect CreateWithCenter(Vector2 CenterPos, Vector2 Size)
        {
            return new Rect(CenterPos.x - Size.x / 2.0, CenterPos.y - Size.y / 2.0, Size.x, Size.y);
        }

        public static Rect CreateWithCenter(double CenterX, double CenterY, double Width, double Height)
        {
            return new Rect(CenterX - Width / 2.0, CenterY - Height / 2.0, Width, Height);
        }

        public static Rect CreateWithBoundary(double Left, double Top, double Right, double Bottom)
        {
            return new Rect(Left, Top, Right - Left, Bottom - Top);
        }

        internal bool IsFinite()
        {
            return Min.IsFinate() && Max.IsFinate();
        }

        public static explicit operator Vector4(Rect v)
        {
            return new Vector4((float)v.Min.x, (float)v.Min.y, (float)v.Max.x, (float)v.Max.y);
        }

        public static bool operator ==(Rect a, Rect b) => a.Min == b.Min && a.Max == b.Max;
        public static bool operator !=(Rect a, Rect b) => a.Min != b.Min || a.Max != b.Max;

        public override bool Equals(object? obj) => obj is Rect r && r == this;
        public override int GetHashCode() => Min.GetHashCode() ^ Max.GetHashCode();
    }
}
