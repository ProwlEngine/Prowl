using Prowl.Vector;

namespace Prowl.Scribe;

/// <summary>
/// One glyph on its way to the renderer. The corners arrive holding the plain axis-aligned quad
/// Scribe laid out; a modifier may move each of them independently, which is what lets a caller
/// rotate, shear or scale a glyph without Scribe knowing anything about the effect.
///
/// Positions are relative to the draw origin, so a modifier never needs to know where the text
/// was placed on screen.
/// </summary>
public struct GlyphDraw
{
    /// <summary>Index of this glyph's first character in the laid-out string.</summary>
    public int CharIndex;
    /// <summary>Running index of the glyph within the layout, for effects that march along the text.</summary>
    public int GlyphIndex;
    public float PixelSize;

    public Float2 TopLeft;
    public Float2 TopRight;
    public Float2 BottomLeft;
    public Float2 BottomRight;

    public FontColor Color;

    /// <summary>Set false to drop the glyph for this frame.</summary>
    public bool Visible;

    /// <summary>The centre of the untouched quad, which is the natural pivot for a transform.</summary>
    public readonly Float2 Centre => new(
        (TopLeft.X + BottomRight.X) * 0.5f,
        (TopLeft.Y + BottomRight.Y) * 0.5f);

    public readonly float Width => BottomRight.X - TopLeft.X;
    public readonly float Height => BottomRight.Y - TopLeft.Y;

    /// <summary>Replace the quad with one built from a centre, a size and four corner offsets.</summary>
    public void SetCorners(Float2 topLeft, Float2 topRight, Float2 bottomLeft, Float2 bottomRight)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomLeft = bottomLeft;
        BottomRight = bottomRight;
    }
}

/// <summary>Adjusts a glyph just before it is drawn. Called once per glyph per draw, so it must be
/// cheap and must not allocate.</summary>
public delegate void GlyphModifier(ref GlyphDraw glyph);
