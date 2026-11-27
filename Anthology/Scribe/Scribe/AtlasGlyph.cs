using System;

using Prowl.Scribe.Internal;

namespace Prowl.Scribe;

public class AtlasGlyph
{
    public int Codepoint { get; }
    public int GlyphIndex { get; }
    public FontFile Font { get; }

    // Atlas rasterization quality this glyph's distance field was generated at. This is the
    // cache key dimension instead of pixel size - the field is resolution independent, so one
    // entry per quality serves every display size.
    public FontQuality Quality { get; }

    // Atlas position (-1 if not in atlas)
    public int AtlasX { get; set; } = -1;
    public int AtlasY { get; set; } = -1;

    // Actual bitmap size in atlas
    public int AtlasWidth { get; set; }
    public int AtlasHeight { get; set; }

    // Texture coordinates (0-1)
    public float U0 { get; set; }
    public float V0 { get; set; }
    public float U1 { get; set; }
    public float V1 { get; set; }

    // The rasterized (padded) glyph region in FONT UNITS, Y up. Multiply by a per-size pixel
    // scale (font.ScaleForPixelHeight) at draw time to obtain the on-screen quad relative to
    // the pen origin. Includes the distance-field margin, so it is wider/taller than ink bounds.
    public double RegionX0 { get; set; }
    public double RegionY0 { get; set; }
    public double RegionX1 { get; set; }
    public double RegionY1 { get; set; }

    public bool IsInAtlas => AtlasX >= 0 && AtlasY >= 0;

    // Outline box and advance in font units, read from the font once instead of per placed glyph.
    private bool _metricsRead;
    private bool _hasBox;
    private int _boxX0, _boxY1;
    private int _advance;

    private void ReadMetrics()
    {
        int x0 = 0, y0 = 0, x1 = 0, y1 = 0;
        _hasBox = Font.GetGlyphBox(GlyphIndex, ref x0, ref y0, ref x1, ref y1) != 0;
        _boxX0 = x0;
        _boxY1 = y1;

        int advance = 0, lsb = 0;
        Font.GetGlyphHorizontalMetrics(GlyphIndex, ref advance, ref lsb);
        _advance = advance;
        _metricsRead = true;
    }

    /// <summary>Top left of the glyph's pixel box relative to the pen, the same as GlyphMetrics.OffsetX and OffsetY.</summary>
    internal void GetOffset(float scale, out float offsetX, out float offsetY)
    {
        if (!_metricsRead) ReadMetrics();
        if (!_hasBox)
        {
            offsetX = 0f;
            offsetY = 0f;
            return;
        }

        offsetX = (int)Math.Floor(_boxX0 * scale);
        offsetY = (int)Math.Floor(-_boxY1 * scale);
    }

    /// <summary>Advance width in font units.</summary>
    internal int Advance
    {
        get
        {
            if (!_metricsRead) ReadMetrics();
            return _advance;
        }
    }

    public AtlasGlyph(int codepoint, FontFile font, FontQuality quality)
    {
        Codepoint = codepoint;
        GlyphIndex = font.FindGlyphIndex(codepoint);
        Font = font;
        Quality = quality;
    }

    private AtlasGlyph(FontFile font, int glyphIndex, FontQuality quality)
    {
        Codepoint = 0; // shaped/substituted glyph - no single source codepoint (e.g. a ligature)
        GlyphIndex = glyphIndex;
        Font = font;
        Quality = quality;
    }

    /// <summary>Creates a glyph directly from a glyph index (for shaped/substituted glyphs).</summary>
    internal static AtlasGlyph FromGlyphIndex(FontFile font, int glyphIndex, FontQuality quality)
        => new AtlasGlyph(font, glyphIndex, quality);

    // Cached by glyph INDEX (not codepoint): the distance field is identical regardless of which
    // codepoint mapped to the glyph, and substituted glyphs (ligatures) have no codepoint at all.
    internal struct CacheKey : IEquatable<CacheKey>
    {
        public readonly int GlyphIndex;
        public readonly FontQuality Quality;
        private readonly FontFile fontFace;

        public CacheKey(int glyphIndex, FontQuality quality, FontFile fontFace)
        {
            GlyphIndex = glyphIndex;
            Quality = quality;
            this.fontFace = fontFace;
        }

        public bool Equals(CacheKey other)
        {
            return GlyphIndex == other.GlyphIndex &&
                   Quality == other.Quality &&
                   ReferenceEquals(fontFace, other.fontFace);
        }

        public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + GlyphIndex;
                hash = hash * 23 + (int)Quality;
                hash = hash * 23 + (fontFace?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
