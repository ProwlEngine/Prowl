// Slug GPU curve font rendering data structures.
// Slug algorithm by Eric Lengyel, MIT OR Apache-2.0.

using System;
using System.Collections.Generic;

namespace Prowl.Quill.Slug
{
    /// <summary>
    /// Axis-aligned bounding box in font design units (TrueType convention: Y increases upward).
    /// </summary>
    public readonly struct SlugBoundingBox
    {
        public int X1 { get; }
        public int Y1 { get; }
        public int X2 { get; }
        public int Y2 { get; }
        public int Width => X2 - X1;
        public int Height => Y2 - Y1;

        public SlugBoundingBox(int x1, int y1, int x2, int y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }
    }

    /// <summary>
    /// Band partition metadata for a glyph, describing how the Slug algorithm divides
    /// the glyph bounding box into a grid of bands and where that grid is stored in the band texture.
    /// </summary>
    public readonly struct SlugBandInfo
    {
        /// <summary>Number of bands per axis (1-16).</summary>
        public int Count { get; }

        /// <summary>Width of each horizontal band partition in font units.</summary>
        public int DimX { get; }

        /// <summary>Height of each vertical band partition in font units.</summary>
        public int DimY { get; }

        /// <summary>X texel coordinate in band texture where this glyph's data begins.</summary>
        public int TexCoordX { get; }

        /// <summary>Y texel coordinate in band texture where this glyph's data begins.</summary>
        public int TexCoordY { get; }

        public SlugBandInfo(int count, int dimX, int dimY, int texCoordX, int texCoordY)
        {
            Count = count;
            DimX = dimX;
            DimY = dimY;
            TexCoordX = texCoordX;
            TexCoordY = texCoordY;
        }
    }

    /// <summary>
    /// Per-glyph metadata produced by font processing, expressed in font design units.
    /// </summary>
    public readonly struct SlugGlyph
    {
        public int CodePoint { get; }
        public SlugBoundingBox BoundingBox { get; }
        public int AdvanceWidth { get; }
        public int LeftSideBearing { get; }
        public SlugBandInfo BandInfo { get; }

        public SlugGlyph(int codePoint, SlugBoundingBox boundingBox, int advanceWidth, int leftSideBearing, SlugBandInfo bandInfo)
        {
            CodePoint = codePoint;
            BoundingBox = boundingBox;
            AdvanceWidth = advanceWidth;
            LeftSideBearing = leftSideBearing;
            BandInfo = bandInfo;
        }
    }

    /// <summary>
    /// Flat-float texture data with dimensions. Used for curve texture (RGBA32F, 4 floats/texel)
    /// and band texture (RG32F, 2 floats/texel).
    /// </summary>
    public readonly struct SlugTextureData
    {
        private readonly float[] _data;

        public int Width { get; }
        public int Height { get; }
        public ReadOnlyMemory<float> Data => _data;
        public float[] RawData => _data;

        public SlugTextureData(float[] data, int width, int height)
        {
            _data = data;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Vertical metrics for a font, expressed in font design units.
    /// </summary>
    public readonly struct SlugFontMetrics
    {
        public int UnitsPerEm { get; }
        public int Ascent { get; }
        public int Descent { get; }
        public int LineGap { get; }

        public SlugFontMetrics(int unitsPerEm, int ascent, int descent, int lineGap)
        {
            UnitsPerEm = unitsPerEm;
            Ascent = ascent;
            Descent = descent;
            LineGap = lineGap;
        }
    }

    /// <summary>
    /// A fully processed font ready for Slug GPU rendering.
    /// Contains curve/band texture data and per-glyph metadata.
    /// </summary>
    public sealed class SlugFontData
    {
        public SlugFontMetrics Metrics { get; }
        public IReadOnlyDictionary<int, SlugGlyph> Glyphs { get; }
        public SlugTextureData CurveTexture { get; }
        public SlugTextureData BandTexture { get; }

        internal SlugFontData(SlugFontMetrics metrics, Dictionary<int, SlugGlyph> glyphs,
            SlugTextureData curveTexture, SlugTextureData bandTexture)
        {
            Metrics = metrics;
            Glyphs = glyphs;
            CurveTexture = curveTexture;
            BandTexture = bandTexture;
        }

        public float GetLineHeight(float sizePixels)
            => (Metrics.Ascent - Metrics.Descent + Metrics.LineGap) * (sizePixels / Math.Max(1, Metrics.UnitsPerEm));
    }
}
