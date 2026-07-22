using Prowl.Scribe.Internal;
using System;
using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Scribe
{

    /// <summary>
    /// Represents a node in the font atlas, typically a free rectangular space.
    /// For a skyline bottom-left bin packer, this might store skyline segments.
    /// </summary>
    internal struct AtlasNode
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public AtlasNode(int x, int y, int width, int height = 0)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Represents a rectangular area, typically in a texture atlas.
    /// </summary>
    public struct AtlasRect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public AtlasRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    public enum TextWrapMode
    {
        NoWrap,
        Wrap
    }

    public enum TextAlignment
    {
        Left,
        Center,
        Right,
        //Justify
    }


    public enum FontStyle
    {
        Regular,
        Bold,
        Italic,
        BoldItalic
    }

    public struct TextLayoutSettings
    {
        public float PixelSize;
        public FontFile Font;
        public float LetterSpacing;
        public float WordSpacing;
        public float LineHeight; // multiplier (1.0 = normal, 1.2 = 20% larger)
        public int TabSize; // in characters
        public TextWrapMode WrapMode;
        public TextAlignment Alignment;
        public float MaxWidth; // for wrapping, 0 = no limit

        // Atlas rasterization quality. Independent of PixelSize - the distance field is generated
        // once per quality and scaled to any display size at draw time.
        public FontQuality Quality;

        public Func<int, FontFile> FontSelector; // optional: index in the full string -> font

        public static TextLayoutSettings Default => new TextLayoutSettings {
            PixelSize = 16,
            Font = null,
            LetterSpacing = 0,
            WordSpacing = 0,
            LineHeight = 1.0f,
            TabSize = 4,
            WrapMode = TextWrapMode.NoWrap,
            Alignment = TextAlignment.Left,
            MaxWidth = 0,
            Quality = FontQuality.Normal
        };
    }

    public struct GlyphInstance
    {
        public AtlasGlyph Glyph;
        public Float2 Position;
        public char Character;
        public float AdvanceWidth;
        public float PixelSize;
        public int CharIndex;

        // Number of source characters this glyph's cluster covers. Normally 1, but a ligature (e.g.
        // "fi") collapses several characters into one glyph - hit-testing interpolates within it.
        public int CharCount;

        public GlyphInstance(AtlasGlyph glyph, Float2 position, char character, float advanceWidth, float pixelSize, int charIndex)
            : this(glyph, position, character, advanceWidth, pixelSize, charIndex, 1)
        {
        }

        public GlyphInstance(AtlasGlyph glyph, Float2 position, char character, float advanceWidth, float pixelSize, int charIndex, int charCount)
        {
            Glyph = glyph;
            Position = position;
            Character = character;
            AdvanceWidth = advanceWidth;
            PixelSize = pixelSize;
            CharIndex = charIndex;
            CharCount = charCount < 1 ? 1 : charCount;
        }
    }

    public struct Line
    {
        public List<GlyphInstance> Glyphs;
        public float Width;
        public float Height;
        public Float2 Position; // relative to layout origin
        public int StartIndex; // character index in original string
        public int EndIndex; // character index in original string

        public Line(Float2 position, int startIndex)
        {
            Glyphs = new List<GlyphInstance>();
            Width = 0;
            Height = 0;
            Position = position;
            StartIndex = startIndex;
            EndIndex = startIndex;
        }
    }
}
