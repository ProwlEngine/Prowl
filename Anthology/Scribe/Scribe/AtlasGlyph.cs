using Prowl.Scribe.Internal;
using System;

namespace Prowl.Scribe
{
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

        public AtlasGlyph(int codepoint, FontFile font, FontQuality quality)
        {
            Codepoint = codepoint;
            GlyphIndex = font.FindGlyphIndex(codepoint);
            Font = font;
            Quality = quality;
        }

        internal struct CacheKey : IEquatable<CacheKey>
        {
            public readonly int Codepoint;
            public readonly FontQuality Quality;
            private readonly FontFile fontFace;

            public CacheKey(int codepoint, FontQuality quality, FontFile fontFace)
            {
                Codepoint = codepoint;
                Quality = quality;
                this.fontFace = fontFace;
            }

            public bool Equals(CacheKey other)
            {
                return Codepoint == other.Codepoint &&
                       Quality == other.Quality &&
                       ReferenceEquals(fontFace, other.fontFace);
            }

            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + Codepoint;
                    hash = hash * 23 + (int)Quality;
                    hash = hash * 23 + (fontFace?.GetHashCode() ?? 0);
                    return hash;
                }
            }
        }
    }
}
