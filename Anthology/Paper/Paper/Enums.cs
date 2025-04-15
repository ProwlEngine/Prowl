namespace Prowl.PaperUI
{
    /// <summary>
    /// Defines the direction in which elements are arranged in a layout container.
    /// </summary>
    public enum LayoutType
    {
        /// <summary>
        /// Elements are arranged horizontally from left to right.
        /// </summary>
        Row,

        /// <summary>
        /// Elements are arranged vertically from top to bottom.
        /// </summary>
        Column
    }

    /// <summary>
    /// Defines how an element's position is determined within its parent.
    /// </summary>
    public enum PositionType
    {
        /// <summary>
        /// Element positions itself using its own coordinates (similar to "absolute" positioning).
        /// </summary>
        SelfDirected,

        /// <summary>
        /// Element's position is determined by the parent layout (similar to "relative" positioning).
        /// </summary>
        ParentDirected
    }

    /// <summary>
    /// Defines measurement units for element dimensions and positioning.
    /// </summary>
    public enum Units
    {
        /// <summary>
        /// Fixed pixel measurements.
        /// </summary>
        Pixels,

        /// <summary>
        /// Percentage of parent container's corresponding dimension.
        /// </summary>
        Percentage,

        /// <summary>
        /// Flexible sizing that distributes available space based on stretch factors.
        /// </summary>
        Stretch,

        /// <summary>
        /// Size is determined automatically based on content or other constraints.
        /// </summary>
        Auto
    }

    /// <summary>
    /// Defines path winding direction for vector shapes.
    /// </summary>
    public enum Winding
    {
        /// <summary>
        /// Counter-clockwise winding, used for defining solid shapes.
        /// </summary>
        CounterClockWise = 1,

        /// <summary>
        /// Clockwise winding, used for defining holes or cutouts in shapes.
        /// </summary>
        ClockWise = 2,
    }

    /// <summary>
    /// Defines fill rules for vector paths.
    /// </summary>
    public enum Solidity
    {
        /// <summary>
        /// Fills interior of counter-clockwise paths.
        /// </summary>
        Solid = 1,

        /// <summary>
        /// Creates a hole in existing shapes using clockwise paths.
        /// </summary>
        Hole = 2,
    }

    /// <summary>
    /// Defines how line endpoints are rendered in stroked paths.
    /// </summary>
    public enum LineCap
    {
        /// <summary>
        /// End is squared off at the endpoint with no extension.
        /// </summary>
        Butt,

        /// <summary>
        /// End is rounded with a semicircle whose diameter equals the line width.
        /// </summary>
        Round,

        /// <summary>
        /// End is squared off with an extension of half the line width.
        /// </summary>
        Square,

        /// <summary>
        /// Joins are cut at a 45-degree angle, with the line extending to form a diamond.
        /// </summary>
        Bevel,

        /// <summary>
        /// Joins extend to a sharp point, limited by miter limit.
        /// </summary>
        Miter,
    }

    ///// <summary>
    ///// Defines flags that control image loading and rendering behavior.
    ///// </summary>
    //[Flags]
    //public enum ImageFlags
    //{
    //    /// <summary>
    //    /// Generate mipmaps during creation of the image for better quality when scaled down.
    //    /// </summary>
    //    GenerateMipMaps = 1 << 0,
    //
    //    /// <summary>
    //    /// Repeat (tile) the image in X direction when texture coordinates exceed bounds.
    //    /// </summary>
    //    RepeatX = 1 << 1,
    //
    //    /// <summary>
    //    /// Repeat (tile) the image in Y direction when texture coordinates exceed bounds.
    //    /// </summary>
    //    RepeatY = 1 << 2,
    //
    //    /// <summary>
    //    /// Flips (inverts) image in Y direction when rendered.
    //    /// </summary>
    //    FlipY = 1 << 3,
    //
    //    /// <summary>
    //    /// Indicates that image data has premultiplied alpha, affecting blending calculations.
    //    /// </summary>
    //    Premultiplied = 1 << 4,
    //
    //    /// <summary>
    //    /// Uses nearest-neighbor interpolation instead of linear when scaling the image.
    //    /// Produces pixelated rather than smoothed appearance when scaled.
    //    /// </summary>
    //    Nearest = 1 << 5,
    //}

    public enum TextHorizontalAlignment { Left, Center, Right }
    public enum TextVerticalAlignment { Top, Center, Bottom }

    /// <summary>
    /// Defines alignment options for positioning text.
    /// Can be combined using bitwise
    /// </summary>
    [Flags]
    public enum Align
    {
        // Horizontal alignment options

        /// <summary>
        /// Aligns text horizontally to the left.
        /// </summarytext
        Left = 1 << 0,

        /// <summary>
        /// Centers text horizontally.
        /// </summary>
        Center = 1 << 1,

        /// <summary>
        /// Aligns text horizontally to the right.
        /// </summary>
        Right = 1 << 2,

        // Vertical alignment options

        /// <summary>
        /// Aligns text vertically to the top.
        /// </summary>
        Top = 1 << 3,

        /// <summary>
        /// Centers text vertically.
        /// </summary>
        Middle = 1 << 4,

        /// <summary>
        /// Aligns text vertically to the bottom.
        /// </summary>
        Bottom = 1 << 5,

        /// <summary>
        /// Aligns text vertically to the baseline (default for text).
        /// </summary>
        Baseline = 1 << 6
    }
}