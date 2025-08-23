using StbTrueTypeSharp;
using System.Numerics;

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
        public FontInfo PreferredFont;
        public float LetterSpacing;
        public float WordSpacing;
        public float LineHeight; // multiplier (1.0 = normal, 1.2 = 20% larger)
        public int TabSize; // in characters
        public TextWrapMode WrapMode;
        public TextAlignment Alignment;
        public float MaxWidth; // for wrapping, 0 = no limit

        public Func<int, FontInfo> FontSelector; // optional: index in the full string -> font

        public static TextLayoutSettings Default => new TextLayoutSettings {
            PixelSize = 16,
            PreferredFont = null,
            LetterSpacing = 0,
            WordSpacing = 0,
            LineHeight = 1.0f,
            TabSize = 4,
            WrapMode = TextWrapMode.NoWrap,
            Alignment = TextAlignment.Left,
            MaxWidth = 0
        };
    }

    public struct GlyphInstance
    {
        public AtlasGlyph Glyph;
        public Vector2 Position;
        public char Character;
        public float AdvanceWidth;
        public int CharIndex;

        public GlyphInstance(AtlasGlyph glyph, Vector2 position, char character, float advanceWidth, int charIndex)
        {
            Glyph = glyph;
            Position = position;
            Character = character;
            AdvanceWidth = advanceWidth;
            CharIndex = charIndex;
        }
    }

    public struct Line
    {
        public List<GlyphInstance> Glyphs;
        public float Width;
        public float Height;
        public Vector2 Position; // relative to layout origin
        public int StartIndex; // character index in original string
        public int EndIndex; // character index in original string

        public Line(Vector2 position, int startIndex)
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
