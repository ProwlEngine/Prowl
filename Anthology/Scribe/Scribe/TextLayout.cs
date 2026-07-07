using System;
using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Scribe
{
    public class TextLayout
    {
        public List<Line> Lines { get; private set; }
        public Float2 Size { get; private set; }
        public TextLayoutSettings Settings { get; private set; }
        public string Text { get; private set; }

        // Per-layout ascender cache - instance fields so LayoutText / LayoutLongWordFast can
        // share state without allocating a closure or a Func<,> per call. Reset at the top of
        // UpdateLayout. Most layouts only ever touch one font, so we keep two single-slot caches
        // and only fall back to a Dictionary if a third font appears.
        private FontFile _asc1Font;
        private float _asc1Value;
        private FontFile _asc2Font;
        private float _asc2Value;
        private Dictionary<FontFile, float> _ascenderCache;

        // The font system this layout was last built against. Hit-testing methods need it to fetch
        // per-glyph metrics (offsets) at draw time, since AtlasGlyph is now size-independent.
        private FontSystem _fontSystem;

        private float GetAscender(FontSystem fontSystem, FontFile font, float pixelSize)
        {
            if (ReferenceEquals(font, _asc1Font)) return _asc1Value;
            if (ReferenceEquals(font, _asc2Font)) return _asc2Value;
            if (_ascenderCache != null && _ascenderCache.TryGetValue(font, out var hit)) return hit;

            fontSystem.GetScaledVMetrics(font, pixelSize, out var asc, out _, out _);
            if (_asc1Font == null) { _asc1Font = font; _asc1Value = asc; }
            else if (_asc2Font == null) { _asc2Font = font; _asc2Value = asc; }
            else
            {
                if (_ascenderCache == null) _ascenderCache = new Dictionary<FontFile, float>(4);
                _ascenderCache[font] = asc;
            }
            return asc;
        }

        /// <summary>
        /// Snapshot of <see cref="FontSystem.AtlasVersion"/> taken when the layout was built.
        /// If the atlas grows or fallback fonts change later, this will be less than the font
        /// system's current version - meaning any <see cref="AtlasGlyph"/> references held by
        /// this layout point at stale UVs / a destroyed texture slot.
        /// Use <see cref="EnsureUpToDate"/> (or just call <c>DrawLayout</c>) to re-stamp.
        /// </summary>
        public int AtlasVersion { get; private set; } = -1;

        /// <summary>
        /// Position-independent glyph quads (corner offsets relative to the draw origin, plus atlas
        /// UVs), built lazily by <see cref="FontSystem.DrawLayout"/> and reused across frames. Cleared
        /// whenever the layout is rebuilt (which includes atlas-version changes via
        /// <see cref="EnsureUpToDate"/>), so it never carries stale UVs. Colour and draw position are
        /// applied at emit time, so they are not baked in here.
        /// </summary>
        internal readonly List<DrawQuad> _drawQuads = new List<DrawQuad>();
        internal bool _drawQuadsBuilt;

        internal struct DrawQuad
        {
            public float X0, Y0, X1, Y1; // corner offsets relative to the draw origin
            public float U0, V0, U1, V1; // atlas UVs
        }

        public TextLayout()
        {
            Lines = new List<Line>();
        }

        internal void UpdateLayout(string text, TextLayoutSettings settings, FontSystem fontSystem)
        {
            Text = text;
            Settings = settings;
            _fontSystem = fontSystem;
            Lines.Clear();
            _drawQuadsBuilt = false; // layout changed -> any cached quads are stale

            if (string.IsNullOrEmpty(text))
            {
                Size = Float2.Zero;
                AtlasVersion = fontSystem.AtlasVersion;
                return;
            }

            LayoutText(fontSystem);
            ApplyAlignment();
            CalculateSize();

            AtlasVersion = fontSystem.AtlasVersion;
        }

        /// <summary>
        /// Returns true if the atlas has been rebuilt since this layout was last built.
        /// </summary>
        public bool IsStale(FontSystem fontSystem) => AtlasVersion != fontSystem.AtlasVersion;

        /// <summary>
        /// Re-layouts this instance against the current atlas state if it's stale. Safe to call
        /// every frame - no-op when up-to-date. Call this before reading UV-dependent data from
        /// the layout's glyphs, or before any direct rendering path that doesn't go through
        /// <see cref="FontSystem.DrawLayout"/>.
        /// </summary>
        public void EnsureUpToDate(FontSystem fontSystem)
        {
            if (AtlasVersion != fontSystem.AtlasVersion && Text != null)
                UpdateLayout(Text, Settings, fontSystem);
        }

        private void LayoutText(FontSystem fontSystem)
        {
            float currentX = 0f;
            float currentY = 0f;
            int i = 0;
            bool hasTrailingNewline = false;

            Lines.Clear();

            var line = new Line(new Float2(0, currentY), 0);

            // Hoist Settings & constants
            var text = Text;
            int len = text.Length;

            float pixelSize = Settings.PixelSize;
            // Real font line height (ascent + |descent| + line gap) rather than a flat multiple of the
            // pixel size, so spacing matches the font's design and stays consistent with RichTextLayout.
            float lineHeight = GetLineHeight(fontSystem) * Settings.LineHeight;
            float spaceWidth = GetSpaceWidth(fontSystem);
            float spaceAdvance = spaceWidth + Settings.WordSpacing;
            float tabWidth = spaceWidth * Settings.TabSize;

            bool wrapEnabled = Settings.WrapMode == TextWrapMode.Wrap && Settings.MaxWidth > 0f;
            float maxWidth = Settings.MaxWidth;

            // Reset the per-layout ascender cache (instance fields - see top of class).
            _asc1Font = null; _asc1Value = 0f;
            _asc2Font = null; _asc2Value = 0f;
            _ascenderCache?.Clear();

            // Reusable shaped-glyph buffer for the current content word (kerning/ligatures folded in).
            var wordGlyphs = _wordGlyphs ??= new List<ShapedGlyph>();

            while (i < len)
            {
                char ch = text[i];

                // Explicit newline
                if (ch == '\n')
                {
                    FinalizeLine(ref line, currentY, lineHeight, i, currentX);
                    currentX = 0f;
                    currentY += lineHeight;
                    i++;
                    line = new Line(new Float2(0, currentY), i);
                    hasTrailingNewline = true;
                    continue;
                }

                // Tabs
                if (ch == '\t')
                {
                    float tabStop = ((int)(currentX / tabWidth) + 1) * tabWidth;
                    currentX = tabStop;
                    i++;
                    continue;
                }

                // Spaces (coalesce runs)
                if (char.IsWhiteSpace(ch))
                {
                    int s = i;
                    while (i < len && char.IsWhiteSpace(text[i]) && text[i] != '\n' && text[i] != '\t') i++;
                    int count = i - s;

                    float runAdvance = spaceAdvance * count;

                    if (wrapEnabled && currentX + runAdvance > maxWidth && line.Glyphs.Count > 0)
                    {
                        // wrap before the run
                        FinalizeLine(ref line, currentY, lineHeight, s, currentX);
                        currentX = 0f;
                        currentY += lineHeight;
                        line = new Line(new Float2(0, currentY), i);
                    }
                    else
                    {
                        currentX += runAdvance;
                    }

                    continue;
                }

                // Content word [wordStart, wordEnd) - shaped once (kerning + ligatures folded in).
                int wordStart = i;
                int wordEnd = FindWordEnd(i);
                hasTrailingNewline = false;

                fontSystem.ShapeRun(text, wordStart, wordEnd, Settings.Font, Settings.FontSelector,
                                    pixelSize, Settings.Quality, wordGlyphs);
                if (wordGlyphs.Count == 0)
                {
                    i = wordEnd;
                    continue;
                }

                // Word width: advance (kerning included) plus letter spacing per cluster.
                float wordWidth = 0f;
                for (int g = 0; g < wordGlyphs.Count; g++)
                    wordWidth += wordGlyphs[g].Advance + Settings.LetterSpacing;

                if (wrapEnabled && currentX + wordWidth > maxWidth)
                {
                    // Wrap before the word when the line already has content.
                    if (line.Glyphs.Count > 0)
                    {
                        FinalizeLine(ref line, currentY, lineHeight, wordStart, currentX);
                        currentX = 0f;
                        currentY += lineHeight;
                        line = new Line(new Float2(0, currentY), wordStart);
                    }

                    // A word too wide for a whole line is split at cluster boundaries.
                    if (wordWidth > maxWidth)
                    {
                        PlaceLongWord(fontSystem, ref line, ref currentX, ref currentY, lineHeight,
                                      wordGlyphs, pixelSize, Settings.LetterSpacing, maxWidth);
                        i = wordEnd;
                        continue;
                    }
                }

                for (int g = 0; g < wordGlyphs.Count; g++)
                    EmitShaped(fontSystem, line, wordGlyphs[g], ref currentX, pixelSize, Settings.LetterSpacing);

                i = wordEnd;
            }

            // Finalize last line
            // Always finalize if: has glyphs, is the first line, or was created by a trailing newline
            if (line.Glyphs.Count > 0 || Lines.Count == 0 || hasTrailingNewline)
                FinalizeLine(ref line, currentY, lineHeight, i, currentX);
        }

        // Reusable shaped-glyph buffer for the current content word.
        private List<ShapedGlyph> _wordGlyphs;

        // Places an already-shaped word that is too wide for one line, breaking at cluster boundaries
        // (never inside a ligature).
        private void PlaceLongWord(FontSystem fontSystem, ref Line line, ref float currentX,
            ref float currentY, float lineHeight, List<ShapedGlyph> glyphs, float pixelSize,
            float letterSpacing, float maxWidth)
        {
            for (int g = 0; g < glyphs.Count; g++)
            {
                var sg = glyphs[g];
                float adv = sg.Advance + letterSpacing;

                if (line.Glyphs.Count > 0 && currentX + adv > maxWidth)
                {
                    int clusterIndex = sg.Cluster;
                    FinalizeLine(ref line, currentY, lineHeight, clusterIndex, currentX);
                    currentX = 0f;
                    currentY += lineHeight;
                    line = new Line(new Float2(0, currentY), clusterIndex);
                }

                EmitShaped(fontSystem, line, sg, ref currentX, pixelSize, letterSpacing);
            }
        }

        // Emits one shaped glyph at the current pen X, advancing the pen. (Line.Glyphs is a reference,
        // so passing the struct by value is fine - only its list is mutated here.)
        private void EmitShaped(FontSystem fontSystem, Line line, ShapedGlyph sg, ref float currentX,
            float pixelSize, float letterSpacing)
        {
            float advance = sg.Advance + letterSpacing;
            var atlas = sg.Glyph;
            if (atlas == null)
            {
                currentX += advance;
                return;
            }

            var gm = fontSystem.GetGlyphMetricsByIndex(atlas.Font, atlas.GlyphIndex, pixelSize) ?? default;
            float a = GetAscender(fontSystem, atlas.Font, pixelSize);
            char ch = sg.Cluster >= 0 && sg.Cluster < Text.Length ? Text[sg.Cluster] : '\0';

            line.Glyphs.Add(new GlyphInstance(
                atlas,
                new Float2(currentX + gm.OffsetX, gm.OffsetY + a),
                ch, advance, pixelSize, sg.Cluster, sg.CharCount));
            currentX += advance;
        }


        // Pen-origin X of a placed glyph. Position.X is pen + glyph offset, so subtract the offset
        // (fetched per-instance now that AtlasGlyph is size-independent) to recover the pen origin.
        private float GlyphOffsetX(GlyphInstance gi)
        {
            if (_fontSystem == null || gi.Glyph == null) return 0f;
            var gm = _fontSystem.GetGlyphMetricsByIndex(gi.Glyph.Font, gi.Glyph.GlyphIndex, gi.PixelSize);
            return gm?.OffsetX ?? 0f;
        }

        // Natural line height of the primary font at the current size (ascent + |descent| + lineGap).
        // Falls back to the pixel size when there is no font.
        private float GetLineHeight(FontSystem fontSystem)
        {
            var font = Settings.Font;
            if (font == null)
                return Settings.PixelSize;
            fontSystem.GetScaledVMetrics(font, Settings.PixelSize, out float asc, out float desc, out float gap);
            float h = asc - desc + gap; // descent is negative
            return h > 0f ? h : Settings.PixelSize;
        }

        private float GetSpaceWidth(FontSystem fontSystem)
        {
            var spaceGlyph = fontSystem.GetOrCreateGlyph(' ', Settings.Font, Settings.Quality);
            if (spaceGlyph == null) return Settings.PixelSize * 0.25f;
            var gm = fontSystem.GetGlyphMetricsByIndex(spaceGlyph.Font, spaceGlyph.GlyphIndex, Settings.PixelSize);
            return gm?.AdvanceWidth ?? Settings.PixelSize * 0.25f;
        }

        private int FindWordEnd(int startIndex)
        {
            int index = startIndex;
            while (index < Text.Length && !char.IsWhiteSpace(Text[index]) && Text[index] != '\n')
            {
                index++;
            }
            return index;
        }

        private void FinalizeLine(ref Line line, float y, float lineHeight, int endIndex, float currentX)
        {
            line.Position = new Float2(0, y);
            line.Height = lineHeight;
            // Use the maximum of glyph-based width and currentX to account for trailing whitespace
            float glyphWidth = line.Glyphs.Count > 0 ? line.Glyphs[^1].Position.X + line.Glyphs[^1].AdvanceWidth : 0;
            line.Width = Math.Max(glyphWidth, currentX);
            line.EndIndex = endIndex;
            Lines.Add(line);
        }

        private void ApplyAlignment()
        {
            if (Settings.Alignment == TextAlignment.Left) return;

            float maxWidth = Settings.MaxWidth > 0 ? Settings.MaxWidth : GetMaxLineWidth();

            foreach (var line in Lines)
            {
                float offset = Settings.Alignment switch {
                    TextAlignment.Center => (maxWidth - line.Width) * 0.5f,
                    TextAlignment.Right => maxWidth - line.Width,
                    //TextAlignment.Justify => 0, // Handle separately
                    _ => 0
                };

                //if (Settings.Alignment == TextAlignment.Justify)
                //{
                //    ApplyJustification(line, maxWidth);
                //}
                //else
                //{
                    // Apply horizontal offset to all glyphs in the line
                    for (int i = 0; i < line.Glyphs.Count; i++)
                    {
                        var glyph = line.Glyphs[i];
                        glyph.Position = new Float2(glyph.Position.X + offset, glyph.Position.Y);
                        line.Glyphs[i] = glyph;
                    }
                //}
            }
        }

        private float GetMaxLineWidth()
        {
            float maxWidth = 0;
            foreach (var line in Lines)
            {
                maxWidth = Math.Max(maxWidth, line.Width);
            }
            return maxWidth;
        }

        private void CalculateSize()
        {
            if (Lines.Count == 0)
            {
                Size = Float2.Zero;
                return;
            }

            float maxWidth = GetMaxLineWidth();
            float totalHeight = Lines[^1].Position.Y + Lines[^1].Height;
            Size = new Float2(maxWidth, totalHeight);
        }

        public Line GetLineForIndex(int index)
        {
            if (Lines.Count == 0)
                return default;

            foreach (var line in Lines)
            {
                if (index < line.EndIndex)
                    return line;
            }

            return Lines[^1];
        }

        public Float2 GetCursorPosition(int index)
        {
            if (Lines.Count == 0)
                return Float2.Zero;

            index = Math.Clamp(index, 0, Text.Length);

            foreach (var line in Lines)
            {
                if (index < line.StartIndex)
                    return new Float2(0, line.Position.Y);

                if (index <= line.EndIndex)
                {
                    float currentX = 0f;
                    int currentIndex = line.StartIndex;

                    foreach (var glyph in line.Glyphs)
                    {
                        float glyphStart = glyph.Position.X - GlyphOffsetX(glyph);
                        if (index <= glyph.CharIndex)
                        {
                            int spaces = glyph.CharIndex - currentIndex;
                            if (spaces > 0)
                            {
                                float spaceWidth = (glyphStart - currentX) / spaces;
                                float offset = index - currentIndex;
                                return new Float2(line.Position.X + currentX + spaceWidth * offset, line.Position.Y);
                            }
                            return new Float2(line.Position.X + glyphStart, line.Position.Y);
                        }

                        // Inside a multi-character cluster (e.g. a ligature): interpolate within it.
                        if (index < glyph.CharIndex + glyph.CharCount)
                        {
                            float frac = (index - glyph.CharIndex) / (float)glyph.CharCount;
                            return new Float2(line.Position.X + glyphStart + glyph.AdvanceWidth * frac, line.Position.Y);
                        }

                        currentX = glyphStart + glyph.AdvanceWidth;
                        currentIndex = glyph.CharIndex + glyph.CharCount;
                    }

                    int trailing = line.EndIndex - currentIndex;
                    if (trailing > 0)
                    {
                        float spaceWidth = trailing > 0 ? (line.Width - currentX) / trailing : 0f;
                        float offset = index - currentIndex;
                        return new Float2(line.Position.X + currentX + spaceWidth * offset, line.Position.Y);
                    }

                    return new Float2(line.Position.X + line.Width, line.Position.Y);
                }
            }

            var last = Lines[^1];
            return new Float2(last.Width, last.Position.Y);
        }

        public int GetCursorIndex(Float2 position)
        {
            if (Lines.Count == 0)
                return 0;

            Line line = Lines[0];
            int lineIndex = 0;
            for (int li = 0; li < Lines.Count; li++)
            {
                var l = Lines[li];
                if (position.Y < l.Position.Y + l.Height)
                {
                    line = l;
                    lineIndex = li;
                    break;
                }
                line = l;
                lineIndex = li;
            }

            float currentX = 0f;
            int currentIndex = line.StartIndex;

            foreach (var glyph in line.Glyphs)
            {
                float glyphStart = glyph.Position.X - GlyphOffsetX(glyph);
                if (position.X < glyphStart)
                {
                    int spaces = glyph.CharIndex - currentIndex;
                    if (spaces > 0)
                    {
                        float spaceWidth = (glyphStart - currentX) / spaces;
                        float rel = position.X - currentX;
                        int offset = spaceWidth > 0 ? (int)Math.Clamp(MathF.Round(rel / spaceWidth), 0, spaces) : 0;
                        return currentIndex + offset;
                    }
                    return currentIndex;
                }

                float glyphEnd = glyphStart + glyph.AdvanceWidth;
                if (position.X < glyphEnd)
                {
                    if (glyph.CharCount <= 1)
                    {
                        float mid = glyphStart + glyph.AdvanceWidth * 0.5f;
                        return position.X < mid ? glyph.CharIndex : glyph.CharIndex + 1;
                    }
                    // Multi-character cluster: snap to the nearest character boundary inside it.
                    float rel = glyph.AdvanceWidth > 0f ? (position.X - glyphStart) / glyph.AdvanceWidth : 0f;
                    int within = (int)Math.Clamp(MathF.Round(rel * glyph.CharCount), 0, glyph.CharCount);
                    return glyph.CharIndex + within;
                }

                currentX = glyphEnd;
                currentIndex = glyph.CharIndex + glyph.CharCount;
            }

            int trailingSpaces = line.EndIndex - currentIndex;
            if (trailingSpaces > 0)
            {
                float spaceWidth = trailingSpaces > 0 ? (line.Width - currentX) / trailingSpaces : 0f;
                float rel = position.X - currentX;
                int offset = spaceWidth > 0 ? (int)Math.Clamp(MathF.Round(rel / spaceWidth), 0, trailingSpaces) : 0;
                int result = currentIndex + offset;
                
                // Special case: if this is the last line and we're hitting at/after the line width,
                // return the text length to handle trailing special characters properly
                bool isLastLine = lineIndex == Lines.Count - 1;
                if (isLastLine && position.X >= line.Width)
                {
                    return Math.Max(result, Text.Length);
                }
                
                return result;
            }

            // If no trailing spaces but we're past the end of visible content on the last line
            bool isLastLine2 = lineIndex == Lines.Count - 1;
            if (isLastLine2 && position.X >= currentX)
            {
                return Text.Length;
            }

            return line.EndIndex;
        }

        public RectangleF GetCharacterRect(int index)
        {
            if (Lines.Count == 0 || index < 0 || index >= Text.Length)
                return new RectangleF(0, 0, 0, 0);

            var line = GetLineForIndex(index);
            var start = GetCursorPosition(index);
            var end = GetCursorPosition(index + 1);
            return new RectangleF(start.X, line.Position.Y, end.X - start.X, line.Height);
        }
    }
}
