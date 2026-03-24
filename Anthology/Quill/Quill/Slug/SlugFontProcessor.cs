// Slug font processor - builds GPU texture data from glyph outlines.

using System;
using System.Collections.Generic;
using Prowl.Scribe;
using Prowl.Scribe.Internal;
using Prowl.Vector;

namespace Prowl.Quill.Slug
{
    /// <summary>
    /// Processes a font using Prowl.Scribe's FontFile and builds the GPU texture data
    /// required by the Slug algorithm.
    /// </summary>
    internal sealed class SlugFontProcessor
    {
        private const int TextureWidth = 4096;

        // GlyphVertex type constants (from stb_truetype)
        private const byte STBTT_vmove = 1;
        private const byte STBTT_vline = 2;
        private const byte STBTT_vcurve = 3;
        private const byte STBTT_vcubic = 4;

        private readonly List<float> _curveTexData = new List<float>(TextureWidth * 4);

        private readonly List<ushort> _bandHeaderCurveCount = new List<ushort>();
        private readonly List<int> _bandHeaderOffset = new List<int>();
        private readonly List<BandTexelCoord> _bandCurveLocs = new List<BandTexelCoord>();
        private readonly List<GlyphBandRange> _glyphBandRanges = new List<GlyphBandRange>();
        private readonly List<SlugGlyph> _glyphs = new List<SlugGlyph>();
        private readonly List<SlugCurve> _scratchCurves = new List<SlugCurve>();

        public SlugFontMetrics ProcessFont(FontFile font)
        {
            float scale = font.ScaleForPixelHeight(1.0f);
            int unitsPerEm = scale > 0f ? (int)MathF.Round(1.0f / scale) : 1000;

            return new SlugFontMetrics(unitsPerEm, font.Ascent, font.Descent, font.Linegap);
        }

        public void ProcessCodePoint(FontFile font, int codePoint)
        {
            int glyphIdx = font.FindGlyphIndex(codePoint);
            if (glyphIdx == 0)
                return;

            int vertCount = font.GetGlyphShape(glyphIdx, out GlyphVertex[] verts);
            if (vertCount == 0)
            {
                // No visible outline (e.g. space). Still record advance width.
                int blankAdvance = 0, blankLsb = 0;
                font.GetGlyphHorizontalMetrics(glyphIdx, ref blankAdvance, ref blankLsb);
                _glyphs.Add(new SlugGlyph(
                    codePoint: codePoint,
                    boundingBox: new SlugBoundingBox(0, 0, 0, 0),
                    advanceWidth: blankAdvance,
                    leftSideBearing: blankLsb,
                    bandInfo: new SlugBandInfo(0, 1, 1, 0, 0)));
                return;
            }

            int bx1 = 0, by1 = 0, bx2 = 0, by2 = 0;
            font.GetGlyphBox(glyphIdx, ref bx1, ref by1, ref bx2, ref by2);

            int advanceWidth = 0, lsb = 0;
            font.GetGlyphHorizontalMetrics(glyphIdx, ref advanceWidth, ref lsb);

            _scratchCurves.Clear();
            float curX = 0f, curY = 0f;
            bool nextIsFirst = false;

            for (int v = 0; v < vertCount; v++)
            {
                ref GlyphVertex vert = ref verts[v];

                switch (vert.type)
                {
                    case STBTT_vmove:
                        curX = vert.x;
                        curY = vert.y;
                        nextIsFirst = true;
                        break;

                    case STBTT_vline:
                    {
                        float nx = vert.x;
                        float ny = vert.y;
                        _scratchCurves.Add(new SlugCurve
                        {
                            StartPoint = new Float2(curX, curY),
                            ControlPoint = new Float2((curX + nx) * 0.5f, (curY + ny) * 0.5f),
                            EndPoint = new Float2(nx, ny),
                            IsFirst = nextIsFirst
                        });
                        curX = nx;
                        curY = ny;
                        nextIsFirst = false;
                        break;
                    }

                    case STBTT_vcurve:
                    {
                        float nx = vert.x;
                        float ny = vert.y;
                        _scratchCurves.Add(new SlugCurve
                        {
                            StartPoint = new Float2(curX, curY),
                            ControlPoint = new Float2(vert.cx, vert.cy),
                            EndPoint = new Float2(nx, ny),
                            IsFirst = nextIsFirst
                        });
                        curX = nx;
                        curY = ny;
                        nextIsFirst = false;
                        break;
                    }

                    case STBTT_vcubic:
                    {
                        // Approximate cubic bezier with quadratic segments.
                        float nx = vert.x;
                        float ny = vert.y;
                        ApproximateCubicWithQuadratics(
                            curX, curY,
                            vert.cx, vert.cy,
                            vert.cx1, vert.cy1,
                            nx, ny,
                            nextIsFirst);
                        curX = nx;
                        curY = ny;
                        nextIsFirst = false;
                        break;
                    }
                }
            }

            if (_scratchCurves.Count == 0)
                return;

            FixDegenerateControlPoints();

            int bandHeaderStart = _bandHeaderCurveCount.Count;
            int bandCurveStart = _bandCurveLocs.Count;
            int bandsTexelIndex = bandHeaderStart;

            AppendCurveTexture();

            int sizeX = bx2 - bx1 + 1;
            int sizeY = by2 - by1 + 1;
            int bandCount = Math.Max(1, Math.Min(16, Math.Min(sizeX, sizeY) / 2));

            AppendBandData(bandCount, sizeX, sizeY, bx1, by1);

            int bandHeaderCount = _bandHeaderCurveCount.Count - bandHeaderStart;
            _glyphBandRanges.Add(new GlyphBandRange(bandHeaderStart, bandCurveStart, bandHeaderCount));

            SlugGlyph glyph = new SlugGlyph(
                codePoint: codePoint,
                boundingBox: new SlugBoundingBox(bx1, by1, bx2, by2),
                advanceWidth: advanceWidth,
                leftSideBearing: lsb,
                bandInfo: new SlugBandInfo(
                    count: bandCount,
                    dimX: (sizeX + bandCount - 1) / bandCount,
                    dimY: (sizeY + bandCount - 1) / bandCount,
                    texCoordX: bandsTexelIndex % TextureWidth,
                    texCoordY: bandsTexelIndex / TextureWidth));

            _glyphs.Add(glyph);
        }

        /// <summary>
        /// Approximate a cubic bezier with 2 quadratic segments using midpoint subdivision.
        /// </summary>
        private void ApproximateCubicWithQuadratics(
            float x0, float y0,
            float cx0, float cy0,
            float cx1, float cy1,
            float x1, float y1,
            bool firstIsContourStart)
        {
            // Split the cubic at t=0.5 into two cubic halves, then approximate each half
            // as a quadratic bezier.
            //
            // For a cubic P0,P1,P2,P3 split at t=0.5:
            // Left half:  P0, (P0+P1)/2, (P0+2P1+P2)/4, (P0+3P1+3P2+P3)/8
            // Right half: (P0+3P1+3P2+P3)/8, (P1+2P2+P3)/4, (P2+P3)/2, P3
            //
            // Approximate cubic [A,B,C,D] as quadratic [A, (3B+3C-A-D)/4, D] (degree reduction)

            float mx01 = (x0 + cx0) * 0.5f;
            float my01 = (y0 + cy0) * 0.5f;
            float mx12 = (cx0 + cx1) * 0.5f;
            float my12 = (cy0 + cy1) * 0.5f;
            float mx23 = (cx1 + x1) * 0.5f;
            float my23 = (cy1 + y1) * 0.5f;

            float mx012 = (mx01 + mx12) * 0.5f;
            float my012 = (my01 + my12) * 0.5f;
            float mx123 = (mx12 + mx23) * 0.5f;
            float my123 = (my12 + my23) * 0.5f;

            float midX = (mx012 + mx123) * 0.5f;
            float midY = (my012 + my123) * 0.5f;

            // Left half cubic: x0,y0 -> mx01,my01 -> mx012,my012 -> midX,midY
            // Quadratic approx: control point = (3*(mx01+mx012) - x0 - midX) / 4
            float lcx = (3.0f * (mx01 + mx012) - x0 - midX) * 0.25f;
            float lcy = (3.0f * (my01 + my012) - y0 - midY) * 0.25f;

            _scratchCurves.Add(new SlugCurve
            {
                StartPoint = new Float2(x0, y0),
                ControlPoint = new Float2(lcx, lcy),
                EndPoint = new Float2(midX, midY),
                IsFirst = firstIsContourStart
            });

            // Right half cubic: midX,midY -> mx123,my123 -> mx23,my23 -> x1,y1
            // Quadratic approx: control point = (3*(mx123+mx23) - midX - x1) / 4
            float rcx = (3.0f * (mx123 + mx23) - midX - x1) * 0.25f;
            float rcy = (3.0f * (my123 + my23) - midY - y1) * 0.25f;

            _scratchCurves.Add(new SlugCurve
            {
                StartPoint = new Float2(midX, midY),
                ControlPoint = new Float2(rcx, rcy),
                EndPoint = new Float2(x1, y1),
                IsFirst = false
            });
        }

        public SlugFontData Build(SlugFontMetrics metrics)
        {
            SlugTextureData curveTexture = FinalizeCurveTexture();
            SlugTextureData bandTexture = FinalizeBandTexture();

            Dictionary<int, SlugGlyph> glyphs = new Dictionary<int, SlugGlyph>(_glyphs.Count);
            foreach (SlugGlyph g in _glyphs)
            {
                glyphs[g.CodePoint] = g;
            }

            return new SlugFontData(metrics, glyphs, curveTexture, bandTexture);
        }

        private void FixDegenerateControlPoints()
        {
            for (int i = 0; i < _scratchCurves.Count; i++)
            {
                SlugCurve c = _scratchCurves[i];

                bool controlEqualStart = c.ControlPoint == c.StartPoint;
                bool controlEqualEnd = c.ControlPoint == c.EndPoint;

                if (controlEqualStart || controlEqualEnd)
                {
                    c.ControlPoint = (c.StartPoint + c.EndPoint) * 0.5f;
                    _scratchCurves[i] = c;
                }
            }
        }

        private void AppendCurveTexture()
        {
            for (int i = 0; i < _scratchCurves.Count; i++)
            {
                SlugCurve c = _scratchCurves[i];

                int nextTexel = _curveTexData.Count / 4;

                // Ensure both texels of this curve fit in the same row.
                if (nextTexel % TextureWidth == TextureWidth - 1)
                {
                    _curveTexData.Add(0f);
                    _curveTexData.Add(0f);
                    _curveTexData.Add(0f);
                    _curveTexData.Add(0f);
                    nextTexel++;
                }

                c.TexelIndex = nextTexel;
                _scratchCurves[i] = c;

                _curveTexData.Add((float)c.StartPoint.X);
                _curveTexData.Add((float)c.StartPoint.Y);
                _curveTexData.Add((float)c.ControlPoint.X);
                _curveTexData.Add((float)c.ControlPoint.Y);

                _curveTexData.Add((float)c.EndPoint.X);
                _curveTexData.Add((float)c.EndPoint.Y);
                _curveTexData.Add(0f);
                _curveTexData.Add(0f);
            }
        }

        private void AppendBandData(int bandCount, int sizeX, int sizeY, int originX, int originY)
        {
            List<BandTexelCoord> localLocs = new List<BandTexelCoord>(_scratchCurves.Count * 2);

            int bandDimY = (sizeY + bandCount - 1) / bandCount;
            int bandDimX = (sizeX + bandCount - 1) / bandCount;

            // Horizontal bands: sorted by max-x descending for early-exit
            _scratchCurves.Sort((a, b) =>
            {
                float maxA = Math.Max(Math.Max((float)a.StartPoint.X, (float)a.ControlPoint.X), (float)a.EndPoint.X);
                float maxB = Math.Max(Math.Max((float)b.StartPoint.X, (float)b.ControlPoint.X), (float)b.EndPoint.X);
                return maxB.CompareTo(maxA);
            });

            for (int band = 0; band < bandCount; band++)
            {
                float minY = originY + band * bandDimY;
                float maxY = minY + bandDimY;

                int localOffset = localLocs.Count;
                ushort count = 0;

                foreach (SlugCurve c in _scratchCurves)
                {
                    if ((float)c.StartPoint.Y == (float)c.ControlPoint.Y && (float)c.ControlPoint.Y == (float)c.EndPoint.Y)
                        continue;

                    float cMinY = Math.Min(Math.Min((float)c.StartPoint.Y, (float)c.ControlPoint.Y), (float)c.EndPoint.Y);
                    float cMaxY = Math.Max(Math.Max((float)c.StartPoint.Y, (float)c.ControlPoint.Y), (float)c.EndPoint.Y);

                    if (cMinY > maxY || cMaxY < minY)
                        continue;

                    localLocs.Add(new BandTexelCoord((ushort)(c.TexelIndex % TextureWidth), (ushort)(c.TexelIndex / TextureWidth)));
                    count++;
                }

                _bandHeaderCurveCount.Add(count);
                _bandHeaderOffset.Add(localOffset);
            }

            // Vertical bands: sorted by max-y descending for early-exit
            _scratchCurves.Sort((a, b) =>
            {
                float maxA = Math.Max(Math.Max((float)a.StartPoint.Y, (float)a.ControlPoint.Y), (float)a.EndPoint.Y);
                float maxB = Math.Max(Math.Max((float)b.StartPoint.Y, (float)b.ControlPoint.Y), (float)b.EndPoint.Y);
                return maxB.CompareTo(maxA);
            });

            for (int band = 0; band < bandCount; band++)
            {
                float minX = originX + band * bandDimX;
                float maxX = minX + bandDimX;

                int localOffset = localLocs.Count;
                ushort count = 0;

                foreach (SlugCurve c in _scratchCurves)
                {
                    if ((float)c.StartPoint.X == (float)c.ControlPoint.X && (float)c.ControlPoint.X == (float)c.EndPoint.X)
                        continue;

                    float cMinX = Math.Min(Math.Min((float)c.StartPoint.X, (float)c.ControlPoint.X), (float)c.EndPoint.X);
                    float cMaxX = Math.Max(Math.Max((float)c.StartPoint.X, (float)c.ControlPoint.X), (float)c.EndPoint.X);

                    if (cMinX > maxX || cMaxX < minX)
                        continue;

                    localLocs.Add(new BandTexelCoord((ushort)(c.TexelIndex % TextureWidth), (ushort)(c.TexelIndex / TextureWidth)));
                    count++;
                }

                _bandHeaderCurveCount.Add(count);
                _bandHeaderOffset.Add(localOffset);
            }

            foreach (BandTexelCoord loc in localLocs)
            {
                _bandCurveLocs.Add(loc);
            }
        }

        private SlugTextureData FinalizeBandTexture()
        {
            int totalHeaderTexels = _bandHeaderCurveCount.Count;

            for (int gi = 0; gi < _glyphBandRanges.Count; gi++)
            {
                GlyphBandRange range = _glyphBandRanges[gi];

                for (int hi = range.HeaderStart; hi < range.HeaderStart + range.HeaderCount; hi++)
                {
                    _bandHeaderOffset[hi] = _bandHeaderOffset[hi] + totalHeaderTexels + range.CurveStart;
                }
            }

            int totalBandTexels = totalHeaderTexels + _bandCurveLocs.Count;
            int bandTexWidth = TextureWidth;
            int bandTexHeight = Math.Max(1, (totalBandTexels + TextureWidth - 1) / TextureWidth);

            float[] bandTexData = new float[bandTexWidth * bandTexHeight * 2];

            for (int i = 0; i < totalHeaderTexels; i++)
            {
                bandTexData[i * 2 + 0] = _bandHeaderCurveCount[i];
                bandTexData[i * 2 + 1] = _bandHeaderOffset[i];
            }

            for (int i = 0; i < _bandCurveLocs.Count; i++)
            {
                bandTexData[(totalHeaderTexels + i) * 2 + 0] = _bandCurveLocs[i].X;
                bandTexData[(totalHeaderTexels + i) * 2 + 1] = _bandCurveLocs[i].Y;
            }

            return new SlugTextureData(bandTexData, bandTexWidth, bandTexHeight);
        }

        private SlugTextureData FinalizeCurveTexture()
        {
            int totalCurveTexels = (_curveTexData.Count + 3) / 4;
            int curveTexWidth = TextureWidth;
            int curveTexHeight = Math.Max(1, (totalCurveTexels + TextureWidth - 1) / TextureWidth);

            float[] curveTexData = new float[curveTexWidth * curveTexHeight * 4];
            _curveTexData.CopyTo(curveTexData);

            return new SlugTextureData(curveTexData, curveTexWidth, curveTexHeight);
        }

        private struct SlugCurve
        {
            internal Float2 StartPoint;
            internal Float2 ControlPoint;
            internal Float2 EndPoint;
            internal int TexelIndex;
            internal bool IsFirst;
        }

        private readonly struct BandTexelCoord
        {
            internal ushort X { get; }
            internal ushort Y { get; }

            internal BandTexelCoord(ushort x, ushort y)
            {
                X = x;
                Y = y;
            }
        }

        private readonly struct GlyphBandRange
        {
            internal int HeaderStart { get; }
            internal int CurveStart { get; }
            internal int HeaderCount { get; }

            internal GlyphBandRange(int headerStart, int curveStart, int headerCount)
            {
                HeaderStart = headerStart;
                CurveStart = curveStart;
                HeaderCount = headerCount;
            }
        }
    }
}
