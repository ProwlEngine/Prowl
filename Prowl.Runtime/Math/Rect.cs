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

namespace Prowl.Runtime
{
    public struct Rect
    {
        public Vector2 Min;    // Upper-left
        public Vector2 Max;    // Lower-right

        public static Rect Empty {
            get {
                return new Rect(
                    new Vector2(double.MaxValue, double.MaxValue),
                    new Vector2(double.MinValue, double.MinValue));
            }
        }
        public static Rect Zero {
            get {
                return new Rect(
                    new Vector2(0, 0),
                    new Vector2(0, 0));
            }
        }
        public double Left => Min.x;
        public double Right => Max.x;
        public double Top => Min.y;
        public double Bottom => Max.y;
        public double X => Min.x;
        public double Y => Min.y;
        public double Width => Max.x - Min.x;
        public double Height => Max.y - Min.y;
        public Vector2 Size => new Vector2(Width, Height);
        public double CenterX => Min.x + Width / 2.0f;
        public double CenterY => Min.y + Height / 2.0f;

        public Rect(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
        }

        public Rect(Vector4 v) : this(new Vector2(v.x, v.y), new Vector2(v.z, v.w)) { }
        public Rect(double x, double y, double width, double height) : this(new Vector2(x, y), new Vector2(x+width, y+height)) { }

        public Vector2 GetCenter() { return new Vector2((Min.x + Max.x) * 0.5f, (Min.y + Max.y) * 0.5f); }

        public double GetWidth() { return Max.x - Min.x; }
        public double GetHeight() { return Max.y - Min.y; }
        public Vector2 GetTL() { return Min; }                   // Top-left
        public Vector2 GetTR() { return new Vector2(Max.x, Min.y); }  // Top-right
        public Vector2 GetBL() { return new Vector2(Min.x, Max.y); }  // Bottom-left
        public Vector2 GetBR() { return Max; }                   // Bottom-right

        public bool Contains(Vector2 p) { return p.x >= Min.x && p.y >= Min.y && p.x < Max.x && p.y < Max.y; }
        public bool Contains(Rect r) { return r.Min.x >= Min.x && r.Min.y >= Min.y && r.Max.x < Max.x && r.Max.y < Max.y; }
        public bool Overlaps(Rect r) { return r.Min.y < Max.y && r.Max.y > Min.y && r.Min.x < Max.x && r.Max.x > Min.x; }
        public void Add(Vector2 rhs) { if (Min.x > rhs.x) Min.x = rhs.x; if (Min.y > rhs.y) Min.y = rhs.y; if (Max.x < rhs.x) Max.x = rhs.x; if (Max.y < rhs.y) Max.y = rhs.y; }
        public void Add(Rect rhs) { if (Min.x > rhs.Min.x) Min.x = rhs.Min.x; if (Min.y > rhs.Min.y) Min.y = rhs.Min.y; if (Max.x < rhs.Max.x) Max.x = rhs.Max.x; if (Max.y < rhs.Max.y) Max.y = rhs.Max.y; }
        public void Expand(float amount) { Min.x -= amount; Min.y -= amount; Max.x += amount; Max.y += amount; }
        public void Expand(Vector2 amount) { Min.x -= amount.x; Min.y -= amount.y; Max.x += amount.x; Max.y += amount.y; }
        public void Reduce(Vector2 amount) { Min.x += amount.x; Min.y += amount.y; Max.x -= amount.x; Max.y -= amount.y; }
        public void Clip(Rect clip) { if (Min.x < clip.Min.x) Min.x = clip.Min.x; if (Min.y < clip.Min.y) Min.y = clip.Min.y; if (Max.x > clip.Max.x) Max.x = clip.Max.x; if (Max.y > clip.Max.y) Max.y = clip.Max.y; }
        public void Round() { Min.x = (float)(int)Min.x; Min.y = (float)(int)Min.y; Max.x = (float)(int)Max.x; Max.y = (float)(int)Max.y; }

        public static bool IntersectRect(Rect Left, Rect Right, ref Rect Result)
        {
            if (!Left.Overlaps(Right))
                return false;

            Result = CreateWithBoundary(
                Mathf.Max(Left.Left, Right.Left),
                Mathf.Max(Left.Top, Right.Top),
                Mathf.Min(Left.Right, Right.Right),
                Mathf.Min(Left.Bottom, Right.Bottom));
            return true;
        }

        public static Rect CombineRect(Rect Parent, Rect Child)
        {
            var Result = Rect.Zero;
            if (!IntersectRect(Parent, Child, ref Result))
            {
                return Parent;
            }

            return Result;
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

        public double x {
            get { return Min.x; }
            set { Min.x = value; }
        }

        public double y {
            get { return Min.y; }
            set { Min.y = value; }
        }

        public double width {
            get { return Max.x - Min.x; }
            set { Max.x = Min.x + value; }
        }

        public double height {
            get { return Max.y - Min.y; }
            set { Max.y = Min.y + value; }
        }

        public override string ToString()
        {
            return $"{{ Min: {Min}, Max: {Max} }}";
        }

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
    }
}
