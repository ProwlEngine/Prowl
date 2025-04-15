using System.Numerics;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Represents a rectangle with position (Min) and size (Max - Min).
    /// </summary>
    public struct Rect
    {
        #region Fields

        /// <summary>Upper-left corner coordinates.</summary>
        public Vector2 Min;

        /// <summary>Lower-right corner coordinates.</summary>
        public Vector2 Max;

        #endregion

        #region Static Properties

        /// <summary>
        /// Gets an empty rectangle with infinite negative area.
        /// </summary>
        public static Rect Empty {
            get {
                return CreateFromMinMax(
                    new Vector2(float.MaxValue, float.MaxValue),
                    new Vector2(float.MinValue, float.MinValue));
            }
        }

        /// <summary>
        /// Gets a rectangle with zero position and size.
        /// </summary>
        public static Rect Zero {
            get {
                return new Rect(
                    new Vector2(0, 0),
                    new Vector2(0, 0));
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the X coordinate of the rectangle.
        /// </summary>
        public float X {
            readonly get => Min.X;
            set {
                float width = Max.X - Min.X;
                Min.X = value;
                Max.X = value + width;
            }
        }

        /// <summary>
        /// Gets or sets the Y coordinate of the rectangle.
        /// </summary>
        public float Y {
            readonly get => Min.Y;
            set {
                float height = Max.Y - Min.Y;
                Min.Y = value;
                Max.Y = value + height;
            }
        }

        /// <summary>
        /// Gets or sets the position of the rectangle (upper-left corner).
        /// </summary>
        public Vector2 Position {
            get => Min;
            set {
                Max += value - Min;
                Min = value;
            }
        }

        /// <summary>
        /// Gets the center point of the rectangle.
        /// </summary>
        public readonly Vector2 Center => new((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2);

        /// <summary>
        /// Gets or sets the width of the rectangle.
        /// </summary>
        public float Width {
            readonly get => Max.X - Min.X;
            set => Max.X = Min.X + value;
        }

        /// <summary>
        /// Gets or sets the height of the rectangle.
        /// </summary>
        public float Height {
            readonly get => Max.Y - Min.Y;
            set => Max.Y = Min.Y + value;
        }

        /// <summary>
        /// Gets the size of the rectangle as a Vector2.
        /// </summary>
        public readonly Vector2 Size => new(Width, Height);

        #endregion

        #region Edge Properties

        /// <summary>Gets the left edge X coordinate.</summary>
        public readonly float Left => Min.X;

        /// <summary>Gets the right edge X coordinate.</summary>
        public readonly float Right => Max.X;

        /// <summary>Gets the top edge Y coordinate.</summary>
        public readonly float Top => Min.Y;

        /// <summary>Gets the bottom edge Y coordinate.</summary>
        public readonly float Bottom => Max.Y;

        #endregion

        #region Corner and Edge Point Properties

        /// <summary>Gets the top-left corner coordinates.</summary>
        public readonly Vector2 TopLeft => new(Left, Top);

        /// <summary>Gets the middle-left edge coordinates.</summary>
        public readonly Vector2 MiddleLeft => new(Left, (Top + Bottom) / 2);

        /// <summary>Gets the top-right corner coordinates.</summary>
        public readonly Vector2 TopRight => new(Right, Top);

        /// <summary>Gets the middle-right edge coordinates.</summary>
        public readonly Vector2 MiddleRight => new(Right, (Top + Bottom) / 2);

        /// <summary>Gets the bottom-left corner coordinates.</summary>
        public readonly Vector2 BottomLeft => new(Left, Bottom);

        /// <summary>Gets the bottom-right corner coordinates.</summary>
        public readonly Vector2 BottomRight => new(Right, Bottom);

        /// <summary>Gets the top-center edge coordinates.</summary>
        public readonly Vector2 TopCenter => new((Left + Right) / 2, Top);

        /// <summary>Gets the bottom-center edge coordinates.</summary>
        public readonly Vector2 BottomCenter => new((Left + Right) / 2, Bottom);

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a rectangle with the specified position and size.
        /// </summary>
        /// <param name="position">The upper-left corner position.</param>
        /// <param name="scale">The size of the rectangle.</param>
        public Rect(Vector2 position, Vector2 scale)
        {
            Min = position;
            Max = position + scale;
        }

        /// <summary>
        /// Creates a rectangle with the specified coordinates and dimensions.
        /// </summary>
        /// <param name="x">The X coordinate of the upper-left corner.</param>
        /// <param name="y">The Y coordinate of the upper-left corner.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        public Rect(float x, float y, float width, float height)
            : this(new Vector2(x, y), new Vector2(width, height)) { }

        #endregion

        #region Methods

        /// <summary>
        /// Determines if the rectangle contains the specified point.
        /// </summary>
        /// <param name="p">The point to check.</param>
        /// <returns>True if the point is inside the rectangle.</returns>
        public bool Contains(Vector2 p) => p.X >= Min.X && p.Y >= Min.Y && p.X < Max.X && p.Y < Max.Y;

        /// <summary>
        /// Determines if the rectangle fully contains another rectangle.
        /// </summary>
        /// <param name="r">The rectangle to check.</param>
        /// <returns>True if the rectangle is contained within this rectangle.</returns>
        public bool Contains(Rect r) => r.Min.X >= Min.X && r.Min.Y >= Min.Y && r.Max.X <= Max.X && r.Max.Y <= Max.Y;

        /// <summary>
        /// Determines if the rectangle overlaps with another rectangle.
        /// </summary>
        /// <param name="r">The rectangle to check against.</param>
        /// <returns>True if the rectangles overlap.</returns>
        public bool Overlaps(Rect r) => r.Min.Y < Max.Y && r.Max.Y > Min.Y && r.Min.X < Max.X && r.Max.X > Min.X;

        /// <summary>
        /// Expands the rectangle to include the specified point.
        /// </summary>
        /// <param name="rhs">The point to include.</param>
        public void Add(Vector2 rhs)
        {
            if (Min.X > rhs.X) Min.X = rhs.X;
            if (Min.Y > rhs.Y) Min.Y = rhs.Y;
            if (Max.X < rhs.X) Max.X = rhs.X;
            if (Max.Y < rhs.Y) Max.Y = rhs.Y;
        }

        /// <summary>
        /// Expands the rectangle to include another rectangle.
        /// </summary>
        /// <param name="rhs">The rectangle to include.</param>
        public void Add(Rect rhs)
        {
            if (Min.X > rhs.Min.X) Min.X = rhs.Min.X;
            if (Min.Y > rhs.Min.Y) Min.Y = rhs.Min.Y;
            if (Max.X < rhs.Max.X) Max.X = rhs.Max.X;
            if (Max.Y < rhs.Max.Y) Max.Y = rhs.Max.Y;
        }

        /// <summary>
        /// Expands the rectangle by the specified amount in all directions.
        /// </summary>
        /// <param name="amount">The amount to expand by.</param>
        public void Expand(float amount)
        {
            Min.X -= amount;
            Min.Y -= amount;
            Max.X += amount;
            Max.Y += amount;
        }

        /// <summary>
        /// Expands the rectangle by different amounts horizontally and vertically.
        /// </summary>
        /// <param name="horizontal">The amount to expand horizontally.</param>
        /// <param name="vertical">The amount to expand vertically.</param>
        public void Expand(float horizontal, float vertical)
        {
            Min.X -= horizontal;
            Min.Y -= vertical;
            Max.X += horizontal;
            Max.Y += vertical;
        }

        /// <summary>
        /// Expands the rectangle by the specified vector amounts.
        /// </summary>
        /// <param name="amount">The amounts to expand by in each dimension.</param>
        public void Expand(Vector2 amount)
        {
            Min.X -= amount.X;
            Min.Y -= amount.Y;
            Max.X += amount.X;
            Max.Y += amount.Y;
        }

        /// <summary>
        /// Reduces the rectangle by the specified vector amounts.
        /// </summary>
        /// <param name="amount">The amounts to reduce by in each dimension.</param>
        public void Reduce(Vector2 amount)
        {
            Min.X += amount.X;
            Min.Y += amount.Y;
            Max.X -= amount.X;
            Max.Y -= amount.Y;
        }

        /// <summary>
        /// Clips this rectangle to fit within another rectangle.
        /// </summary>
        /// <param name="clip">The rectangle to clip against.</param>
        public void Clip(Rect clip)
        {
            if (Min.X < clip.Min.X) Min.X = clip.Min.X;
            if (Min.Y < clip.Min.Y) Min.Y = clip.Min.Y;
            if (Max.X > clip.Max.X) Max.X = clip.Max.X;
            if (Max.Y > clip.Max.Y) Max.Y = clip.Max.Y;
        }

        /// <summary>
        /// Rounds the rectangle's coordinates to integer values.
        /// </summary>
        public void Round()
        {
            Min.X = (float)(int)Min.X;
            Min.Y = (float)(int)Min.Y;
            Max.X = (float)(int)Max.X;
            Max.Y = (float)(int)Max.Y;
        }

        /// <summary>
        /// Gets the closest point on or within the rectangle to the specified point.
        /// </summary>
        /// <param name="p">The point to find the closest point to.</param>
        /// <param name="on_edge">If true, the point must be on the edge of the rectangle.</param>
        /// <returns>The closest point on or within the rectangle.</returns>
        public Vector2 GetClosestPoint(Vector2 p, bool on_edge)
        {
            if (!on_edge && Contains(p))
                return p;
            if (p.X > Max.X) p.X = Max.X;
            else if (p.X < Min.X) p.X = Min.X;
            if (p.Y > Max.Y) p.Y = Max.Y;
            else if (p.Y < Min.Y) p.Y = Min.Y;
            return p;
        }

        /// <summary>
        /// Returns a string representation of the rectangle.
        /// </summary>
        /// <returns>A string representation.</returns>
        public override string ToString()
        {
            return $"{{ Min: {Min}, Max: {Max} }}";
        }

        /// <summary>
        /// Determines whether this rectangle equals another rectangle.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True if the rectangles are equal.</returns>
        public override bool Equals(object? obj) => obj is Rect r && r == this;

        /// <summary>
        /// Gets a hash code for the rectangle.
        /// </summary>
        /// <returns>A hash code.</returns>
        public override int GetHashCode() => Min.GetHashCode() ^ Max.GetHashCode();

        #endregion

        #region Static Methods

        /// <summary>
        /// Creates a rectangle from min and max coordinates.
        /// </summary>
        /// <param name="min">The minimum coordinates (upper-left).</param>
        /// <param name="max">The maximum coordinates (lower-right).</param>
        /// <returns>A new rectangle.</returns>
        public static Rect CreateFromMinMax(Vector2 min, Vector2 max) => new(min, max - min);

        /// <summary>
        /// Creates a rectangle centered at a point with the specified size.
        /// </summary>
        /// <param name="CenterPos">The center position.</param>
        /// <param name="Size">The size of the rectangle.</param>
        /// <returns>A new rectangle.</returns>
        public static Rect CreateWithCenter(Vector2 CenterPos, Vector2 Size)
        {
            return new Rect(CenterPos.X - Size.X / 2.0f, CenterPos.Y - Size.Y / 2.0f, Size.X, Size.Y);
        }

        /// <summary>
        /// Creates a rectangle centered at coordinates with the specified dimensions.
        /// </summary>
        /// <param name="CenterX">The center X coordinate.</param>
        /// <param name="CenterY">The center Y coordinate.</param>
        /// <param name="Width">The width of the rectangle.</param>
        /// <param name="Height">The height of the rectangle.</param>
        /// <returns>A new rectangle.</returns>
        public static Rect CreateWithCenter(float CenterX, float CenterY, float Width, float Height)
        {
            return new Rect(CenterX - Width / 2.0f, CenterY - Height / 2.0f, Width, Height);
        }

        /// <summary>
        /// Creates a rectangle from boundary coordinates.
        /// </summary>
        /// <param name="Left">The left edge coordinate.</param>
        /// <param name="Top">The top edge coordinate.</param>
        /// <param name="Right">The right edge coordinate.</param>
        /// <param name="Bottom">The bottom edge coordinate.</param>
        /// <returns>A new rectangle.</returns>
        public static Rect CreateWithBoundary(float Left, float Top, float Right, float Bottom)
        {
            return new Rect(Left, Top, Right - Left, Bottom - Top);
        }

        /// <summary>
        /// Calculates the intersection of two rectangles.
        /// </summary>
        /// <param name="Left">The first rectangle.</param>
        /// <param name="Right">The second rectangle.</param>
        /// <param name="Result">The resulting intersection rectangle.</param>
        /// <returns>True if the rectangles intersect, false otherwise.</returns>
        public static bool IntersectRect(Rect Left, Rect Right, out Rect Result)
        {
            Result = new Rect();

            if (!Left.Overlaps(Right))
                return false;

            Result = CreateWithBoundary(
                MathF.Max(Left.Left, Right.Left),
                MathF.Max(Left.Top, Right.Top),
                MathF.Min(Left.Right, Right.Right),
                MathF.Min(Left.Bottom, Right.Bottom));
            return true;
        }

        /// <summary>
        /// Combines two rectangles to create a rectangle that contains both.
        /// </summary>
        /// <param name="a">The first rectangle.</param>
        /// <param name="b">The second rectangle.</param>
        /// <returns>A rectangle containing both input rectangles.</returns>
        public static Rect CombineRect(Rect a, Rect b)
        {
            Rect result = new Rect();
            result.Min.X = MathF.Min(a.Min.X, b.Min.X);
            result.Min.Y = MathF.Min(a.Min.Y, b.Min.Y);
            result.Max.X = MathF.Max(a.Max.X, b.Max.X);
            result.Max.Y = MathF.Max(a.Max.Y, b.Max.Y);
            return result;
        }

        #endregion

        #region Operators

        /// <summary>
        /// Converts a rectangle to a Vector4.
        /// </summary>
        /// <param name="v">The rectangle to convert.</param>
        public static explicit operator Vector4(Rect v)
        {
            return new Vector4(v.Min.X, v.Min.Y, v.Max.X, v.Max.Y);
        }

        /// <summary>
        /// Compares two rectangles for equality.
        /// </summary>
        /// <param name="a">The first rectangle.</param>
        /// <param name="b">The second rectangle.</param>
        /// <returns>True if the rectangles are equal.</returns>
        public static bool operator ==(Rect a, Rect b) => a.Min == b.Min && a.Max == b.Max;

        /// <summary>
        /// Compares two rectangles for inequality.
        /// </summary>
        /// <param name="a">The first rectangle.</param>
        /// <param name="b">The second rectangle.</param>
        /// <returns>True if the rectangles are not equal.</returns>
        public static bool operator !=(Rect a, Rect b) => a.Min != b.Min || a.Max != b.Max;

        #endregion
    }
}