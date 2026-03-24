// Slug text renderer - writes glyph quads directly into Canvas vertex/index buffers.

using System;
using System.Collections.Generic;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Vector.Spatial;

namespace Prowl.Quill.Slug
{
    /// <summary>
    /// Manages Slug font data and writes glyph quads into Canvas's vertex/index buffers.
    /// </summary>
    internal sealed class SlugTextRenderer
    {
        private const float PadPixels = 2.0f;

        private readonly Canvas _canvas;
        private readonly Dictionary<FontFile, CachedSlugFont> _fontCache = new Dictionary<FontFile, CachedSlugFont>();

        internal SlugTextRenderer(Canvas canvas)
        {
            _canvas = canvas;
        }

        /// <summary>
        /// Gets the Slug font data for a font, processing it on first use.
        /// </summary>
        internal CachedSlugFont GetOrCreateCachedFont(FontFile font)
        {
            if (_fontCache.TryGetValue(font, out CachedSlugFont cached))
                return cached;

            var processor = new SlugFontProcessor();
            SlugFontMetrics metrics = processor.ProcessFont(font);

            // Process printable ASCII + extended latin
            for (int cp = 32; cp <= 126; cp++)
                processor.ProcessCodePoint(font, cp);
            for (int cp = 160; cp <= 591; cp++)
                processor.ProcessCodePoint(font, cp);

            SlugFontData fontData = processor.Build(metrics);

            // Upload float textures via Canvas renderer
            object? curveTexture = _canvas._renderer.CreateFloatTexture(
                fontData.CurveTexture.Width, fontData.CurveTexture.Height,
                4, fontData.CurveTexture.RawData);
            object? bandTexture = _canvas._renderer.CreateFloatTexture(
                fontData.BandTexture.Width, fontData.BandTexture.Height,
                2, fontData.BandTexture.RawData);

            if (curveTexture == null || bandTexture == null)
                return cached; // Backend doesn't support Slug textures

            cached = new CachedSlugFont(fontData, curveTexture, bandTexture);
            _fontCache[font] = cached;
            return cached;
        }

        /// <summary>
        /// Draw text by writing Slug glyph quads directly into Canvas vertex/index buffers.
        /// Positions are in logical space; canvas transform is applied per-glyph.
        /// </summary>
        internal void DrawText(FontFile font, string text, float x, float y,
            Color32 color, float pixelSize, float scale, Transform2D canvasTransform)
        {
            CachedSlugFont cached = GetOrCreateCachedFont(font);
            float fontScale = pixelSize / Math.Max(1, cached.Data.Metrics.UnitsPerEm);
            float logicalScale = fontScale / scale;

            // Canvas passes position as top-of-text. Slug needs baseline.
            y += cached.Data.Metrics.Ascent * logicalScale;

            // Set up the draw state for Slug - we need the curve/band textures bound
            _canvas.SetSlugTextures(cached.CurveTexture, cached.BandTexture,
                cached.Data.CurveTexture.Width, cached.Data.CurveTexture.Height,
                cached.Data.BandTexture.Width, cached.Data.BandTexture.Height);

            float cursorX = x;

            int i = 0;
            while (i < text.Length)
            {
                int cp;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i += 2;
                }
                else
                {
                    cp = text[i];
                    i++;
                }

                if (cached.Data.Glyphs.TryGetValue(cp, out SlugGlyph glyph))
                {
                    if (glyph.BandInfo.Count > 0)
                    {
                        EmitGlyphQuad(glyph, cursorX, y, pixelSize, scale,
                            logicalScale, color, canvasTransform, cached);
                    }
                    cursorX += glyph.AdvanceWidth * logicalScale;
                }
            }

            _canvas.SetSlugTextures(null, null, 0, 0, 0, 0);
        }

        private void EmitGlyphQuad(SlugGlyph g, float baseX, float baseY,
            float pixelSize, float scale, float logicalScale,
            Color32 color, Transform2D transform, CachedSlugFont font)
        {
            float padLogical = PadPixels / scale;
            float padEm = PadPixels * font.Data.Metrics.UnitsPerEm / pixelSize;

            // Logical-space quad corners (with padding)
            float lx0 = baseX + g.BoundingBox.X1 * logicalScale - padLogical;
            float ly0 = baseY - g.BoundingBox.Y2 * logicalScale - padLogical;
            float lx1 = baseX + g.BoundingBox.X2 * logicalScale + padLogical;
            float ly1 = baseY - g.BoundingBox.Y1 * logicalScale + padLogical;

            // Transform each corner to pixel space
            Float2 p0 = transform.TransformPoint(new Float2(lx0, ly0)) * scale; // TL
            Float2 p1 = transform.TransformPoint(new Float2(lx1, ly0)) * scale; // TR
            Float2 p2 = transform.TransformPoint(new Float2(lx1, ly1)) * scale; // BR
            Float2 p3 = transform.TransformPoint(new Float2(lx0, ly1)) * scale; // BL

            // Em-space corners (with padding)
            float ex0 = g.BoundingBox.X1 - padEm;
            float ey0 = g.BoundingBox.Y1 - padEm;
            float ex1 = g.BoundingBox.X2 + padEm;
            float ey1 = g.BoundingBox.Y2 + padEm;

            // Band transform data
            float bandScaleX = 1.0f / Math.Max(1f, g.BandInfo.DimX);
            float bandScaleY = 1.0f / Math.Max(1f, g.BandInfo.DimY);
            float bandOffsetX = -g.BoundingBox.X1 * bandScaleX;
            float bandOffsetY = -g.BoundingBox.Y1 * bandScaleY;
            float packedBandLoc = (float)g.BandInfo.TexCoordY * 4096f + (float)g.BandInfo.TexCoordX;
            float bandCount = (float)g.BandInfo.Count;

            // Write directly into Canvas vertex buffer
            uint baseIdx = (uint)_canvas.Vertices.Count;

            // [0] TL screen → TL font (ey1 = font top)
            _canvas.AddVertex(new Vertex(
                new Float2((float)p0.X, (float)p0.Y), new Float2(ex0, ey1), color,
                bandScaleX, bandScaleY, bandOffsetX, bandOffsetY, packedBandLoc, bandCount));

            // [1] TR screen → TR font
            _canvas.AddVertex(new Vertex(
                new Float2((float)p1.X, (float)p1.Y), new Float2(ex1, ey1), color,
                bandScaleX, bandScaleY, bandOffsetX, bandOffsetY, packedBandLoc, bandCount));

            // [2] BR screen → BR font (ey0 = font bottom)
            _canvas.AddVertex(new Vertex(
                new Float2((float)p2.X, (float)p2.Y), new Float2(ex1, ey0), color,
                bandScaleX, bandScaleY, bandOffsetX, bandOffsetY, packedBandLoc, bandCount));

            // [3] BL screen → BL font
            _canvas.AddVertex(new Vertex(
                new Float2((float)p3.X, (float)p3.Y), new Float2(ex0, ey0), color,
                bandScaleX, bandScaleY, bandOffsetX, bandOffsetY, packedBandLoc, bandCount));

            _canvas.AddTriangle(baseIdx + 0, baseIdx + 1, baseIdx + 2);
            _canvas.AddTriangle(baseIdx + 0, baseIdx + 2, baseIdx + 3);
        }

        internal sealed class CachedSlugFont
        {
            internal SlugFontData Data { get; }
            internal object CurveTexture { get; }
            internal object BandTexture { get; }

            internal CachedSlugFont(SlugFontData data, object curveTexture, object bandTexture)
            {
                Data = data;
                CurveTexture = curveTexture;
                BandTexture = bandTexture;
            }
        }
    }
}
