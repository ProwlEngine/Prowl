using StbTrueTypeSharp;

namespace Prowl.Scribe
{
    public class AtlasGlyph
    {
        public int Codepoint { get; }
        public int GlyphIndex { get; }
        public float PixelSize { get; }
        public FontInfo Font { get; }

        // Glyph metrics
        public GlyphMetrics Metrics { get; }

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

        public bool IsInAtlas => AtlasX >= 0 && AtlasY >= 0;

        public AtlasGlyph(int codepoint, float pixelSize, FontInfo font, FontSystem atlas)
        {
            Codepoint = codepoint;
            GlyphIndex = font.FindGlyphIndex(codepoint);
            PixelSize = pixelSize;
            Font = font;
            Metrics = atlas.GetGlyphMetrics(font, codepoint, pixelSize) ?? default;
        }

        internal struct CacheKey : IEquatable<CacheKey>
        {
            public readonly int Codepoint;
            public readonly int QuantizedSize;
            private readonly FontInfo fontFace;

            public CacheKey(int codepoint, float pixelSize, FontInfo fontFace)
            {
                Codepoint = codepoint;
                QuantizedSize = (int)(pixelSize * 10 + 0.5f); // 0.1 precision
                this.fontFace = fontFace;
            }

            public bool Equals(CacheKey other)
            {
                return Codepoint == other.Codepoint &&
                       QuantizedSize == other.QuantizedSize &&
                       ReferenceEquals(fontFace, other.fontFace);
            }

            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + Codepoint;
                    hash = hash * 23 + QuantizedSize;
                    hash = hash * 23 + (fontFace?.GetHashCode() ?? 0);
                    return hash;
                }
            }
        }
    }
}
