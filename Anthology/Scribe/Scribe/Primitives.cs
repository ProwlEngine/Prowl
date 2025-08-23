using StbTrueTypeSharp;
using System.Numerics;

namespace Prowl.Scribe
{
    public enum TextWrapMode
    {
        NoWrap,
        Wrap
    }

    public enum TextAlignmentMD
    {
        Left,
        Center,
        Right,
        //Justify
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
        public TextAlignmentMD Alignment;
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
            Alignment = TextAlignmentMD.Left,
            MaxWidth = 0
        };
    }

    public struct GlyphInstance
    {
        public AtlasGlyph Glyph;
        public Vector2 Position;
        public char Character;
        public float AdvanceWidth;

        public GlyphInstance(AtlasGlyph glyph, Vector2 position, char character, float advanceWidth)
        {
            Glyph = glyph;
            Position = position;
            Character = character;
            AdvanceWidth = advanceWidth;
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
