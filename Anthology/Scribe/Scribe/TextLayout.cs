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

        /// <summary>
        /// Snapshot of <see cref="FontSystem.AtlasVersion"/> taken when the layout was built.
        /// If the atlas grows or fallback fonts change later, this will be less than the font
        /// system's current version — meaning any <see cref="AtlasGlyph"/> references held by
        /// this layout point at stale UVs / a destroyed texture slot.
        /// Use <see cref="EnsureUpToDate"/> (or just call <c>DrawLayout</c>) to re-stamp.
        /// </summary>
        public int AtlasVersion { get; private set; } = -1;

        public TextLayout()
        {
            Lines = new List<Line>();
        }

        internal void UpdateLayout(string text, TextLayoutSettings settings, FontSystem fontSystem)
        {
            Text = text;
            Settings = settings;
            Lines.Clear();

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
        /// every frame — no-op when up-to-date. Call this before reading UV-dependent data from
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
            float lineHeight = pixelSize * Settings.LineHeight;
            float spaceWidth = GetSpaceWidth(fontSystem);
            float spaceAdvance = spaceWidth + Settings.WordSpacing;
            float tabWidth = spaceWidth * Settings.TabSize;

            bool wrapEnabled = Settings.WrapMode == TextWrapMode.Wrap && Settings.MaxWidth > 0f;
            float maxWidth = Settings.MaxWidth;

            // Kerning baseline: do NOT kern across whitespace.
            // Track the previous glyph's index + font so we can call the by-glyph kerning
            // overload (no FindGlyphIndex re-lookups) and only kern within a single font.
            int lastGlyphForKerning = 0;
            FontFile lastFontForKerning = null;

            // Ascender cache per font object; we only need 'a' to place the glyph vertically
            var ascenderCache = new Dictionary<object, float>(8);
            float GetAscender(FontFile font)
            {
                if (ascenderCache.TryGetValue(font, out var a)) return a;
                fontSystem.GetScaledVMetrics(font, pixelSize, out var asc, out _, out _);
                ascenderCache[font] = asc;
                return asc;
            }

            // Local to place a single glyph (no cross-line kerning)
            void EmitGlyph(AtlasGlyph glyph, FontFile font, char c, float offsetX, float offsetY, float advanceBase, ref float x, List<GlyphInstance> outList, int charIndex)
            {
                float a = GetAscender(font);
                var gi = new GlyphInstance(glyph, new Float2(x + offsetX, offsetY + a), c, advanceBase, charIndex);
                outList.Add(gi);
                x += advanceBase;
                lastGlyphForKerning = glyph.GlyphIndex; // kerning only continues within the current word/run
                lastFontForKerning = font;
            }

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
                    lastGlyphForKerning = 0;
                    lastFontForKerning = null;
                    hasTrailingNewline = true;
                    continue;
                }

                // Tabs
                if (ch == '\t')
                {
                    float tabStop = ((int)(currentX / tabWidth) + 1) * tabWidth;
                    currentX = tabStop;
                    i++;
                    lastGlyphForKerning = 0; // do not kern across whitespace
                    lastFontForKerning = null;
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

                    lastGlyphForKerning = 0; // break kerning chain
                    lastFontForKerning = null;
                    continue;
                }

                // Word [i..wordEnd)
                int wordStart = i;
                int wordEnd = FindWordEnd(i);
                
                // We're processing actual content, so clear the trailing newline flag
                hasTrailingNewline = false;

                // -------- First pass: measure the word (only when wrapping is enabled) --------
                // When wrap is off the per-word width is unused, so we skip the measure pass
                // entirely and let the place pass resolve glyphs once.
                float wordWidthNoLeadingKerning = 0f;
                float firstLeadingKerning = 0f;
                bool measured = false;
                bool hadFirstGlyph = false;

                if (wrapEnabled)
                {
                    int prevGlyphIndex = 0;
                    FontFile prevGlyphFont = null;

                    for (int j = wordStart; j < wordEnd; j++)
                    {
                        char c = text[j];

                        FontFile font = Settings.Font;
                        if (Settings.FontSelector != null)
                            font = Settings.FontSelector(j);
                        var g = fontSystem.GetOrCreateGlyph(c, pixelSize, font);

                        if (g == null) continue;

                        var adv = g.Metrics.AdvanceWidth + Settings.LetterSpacing;

                        // internal kerning (within the word) — only kern within a single font
                        if (prevGlyphIndex != 0 && ReferenceEquals(prevGlyphFont, g.Font))
                            wordWidthNoLeadingKerning += fontSystem.GetKerningByGlyph(g.Font, prevGlyphIndex, g.GlyphIndex, pixelSize);

                        wordWidthNoLeadingKerning += adv;

                        if (!hadFirstGlyph)
                        {
                            hadFirstGlyph = true;

                            // kerning between previous run (if any) and first glyph of this word
                            if (lastGlyphForKerning != 0 && ReferenceEquals(lastFontForKerning, g.Font))
                                firstLeadingKerning = fontSystem.GetKerningByGlyph(g.Font, lastGlyphForKerning, g.GlyphIndex, pixelSize);
                        }

                        prevGlyphIndex = g.GlyphIndex;
                        prevGlyphFont = g.Font;
                    }

                    measured = true;

                    // Word may be empty if all codepoints were missing; just skip it
                    if (!hadFirstGlyph)
                    {
                        i = wordEnd;
                        lastGlyphForKerning = 0;
                        lastFontForKerning = null;
                        continue;
                    }

                    // -------- Wrapping decisions --------
                    float prospective = currentX + (line.Glyphs.Count > 0 ? firstLeadingKerning : 0f) + wordWidthNoLeadingKerning;
                    if (prospective > maxWidth)
                    {
                        // If current line has content, wrap before placing the word
                        if (line.Glyphs.Count > 0)
                        {
                            FinalizeLine(ref line, currentY, lineHeight, wordStart, currentX);
                            currentX = 0f;
                            currentY += lineHeight;
                            line = new Line(new Float2(0, currentY), wordStart);
                            lastGlyphForKerning = 0; // new line: no leading kerning
                            lastFontForKerning = null;
                            firstLeadingKerning = 0f;
                        }

                        // If the word itself is too long for an empty line, split it (char-level)
                        if (wordWidthNoLeadingKerning > maxWidth)
                        {
                            i = LayoutLongWordFast(fontSystem, ref line, ref currentX, ref currentY, lineHeight,
                                                   wordStart, wordEnd, tabWidth, spaceAdvance, wrapEnabled, maxWidth, GetAscender);
                            lastGlyphForKerning = 0;
                            lastFontForKerning = null;
                            continue;
                        }
                    }
                }

                // -------- Second pass for the word: actually place glyphs --------
                // When measure ran, leading kerning was already computed (and reset to 0 if we wrapped).
                // When measure was skipped, defer leading kerning to the first emitted glyph.
                if (measured && firstLeadingKerning != 0f && line.Glyphs.Count > 0)
                    currentX += firstLeadingKerning;

                bool placedAnyThisWord = false;
                int prevForKern = 0;
                FontFile prevFontForKern = null;
                for (int j = wordStart; j < wordEnd; j++)
                {
                    char c = text[j];

                    FontFile font = Settings.Font;
                    if (Settings.FontSelector != null)
                        font = Settings.FontSelector(j);
                    var g = fontSystem.GetOrCreateGlyph(c, pixelSize, font);

                    if (g == null) continue;

                    // Cross-word leading kerning (computed lazily when measure pass was skipped)
                    if (!placedAnyThisWord && !measured && line.Glyphs.Count > 0
                        && lastGlyphForKerning != 0 && ReferenceEquals(lastFontForKerning, g.Font))
                    {
                        currentX += fontSystem.GetKerningByGlyph(g.Font, lastGlyphForKerning, g.GlyphIndex, pixelSize);
                    }

                    if (prevForKern != 0 && ReferenceEquals(prevFontForKern, g.Font))
                    {
                        float k = fontSystem.GetKerningByGlyph(g.Font, prevForKern, g.GlyphIndex, pixelSize);
                        if (k != 0f) currentX += k;
                    }

                    EmitGlyph(g, g.Font, c, g.Metrics.OffsetX, g.Metrics.OffsetY,
                              g.Metrics.AdvanceWidth + Settings.LetterSpacing,
                              ref currentX, line.Glyphs, j);

                    prevForKern = g.GlyphIndex;
                    prevFontForKern = g.Font;
                    placedAnyThisWord = true;
                }

                // If we skipped the measure pass and the word turned out to be all-missing, behave
                // the same as the wrap-enabled path so cross-run kerning resets.
                if (!measured && !placedAnyThisWord)
                {
                    lastGlyphForKerning = 0;
                    lastFontForKerning = null;
                }

                // Move i past this word
                i = wordEnd;
            }

            // Finalize last line
            // Always finalize if: has glyphs, is the first line, or was created by a trailing newline
            if (line.Glyphs.Count > 0 || Lines.Count == 0 || hasTrailingNewline)
                FinalizeLine(ref line, currentY, lineHeight, i, currentX);
        }

        // Split a too-long word across lines, char by char, with minimal overhead.
        // Note: we do not kern across line starts; inside a run we keep kerning.
        private int LayoutLongWordFast(
            FontSystem fontSystem,
            ref Line line,
            ref float currentX,
            ref float currentY,
            float lineHeight,
            int start, int end,
            float tabWidth,
            float spaceAdvance,
            bool wrapEnabled,
            float maxWidth,
            Func<FontFile, float> getAscender)
        {
            float pixelSize = Settings.PixelSize;

            int lastKernGlyph = 0;
            FontFile lastKernFont = null;

            for (int i = start; i < end; i++)
            {
                char c = Text[i];

                FontFile font = Settings.Font;
                if (Settings.FontSelector != null)
                    font = Settings.FontSelector(i);
                var g = fontSystem.GetOrCreateGlyph(c, pixelSize, font);

                if (g == null) continue;

                float adv = g.Metrics.AdvanceWidth + Settings.LetterSpacing;

                // If next char doesn't fit, wrap (but not before placing at least one glyph)
                float k = 0f;
                if (lastKernGlyph != 0 && ReferenceEquals(lastKernFont, g.Font))
                    k = fontSystem.GetKerningByGlyph(g.Font, lastKernGlyph, g.GlyphIndex, pixelSize);

                if (wrapEnabled && line.Glyphs.Count > 0 && currentX + k + adv > maxWidth)
                {
                    FinalizeLine(ref line, currentY, lineHeight, i, currentX);
                    currentX = 0f;
                    currentY += lineHeight;
                    line = new Line(new Float2(0, currentY), i);
                    lastKernGlyph = 0; // break kerning across lines
                    lastKernFont = null;
                }
                else if (wrapEnabled && line.Glyphs.Count == 0 && currentX + k + adv > maxWidth)
                {
                    // Single glyph doesn't fit on an empty line — still place it to avoid infinite loops.
                    // (Alternatively, clamp to maxWidth.)
                }
                else
                {
                    if (k != 0f) currentX += k;
                }

                // Emit glyph
                float a = getAscender(g.Font);
                var gi = new GlyphInstance(g, new Float2(currentX + g.Metrics.OffsetX, g.Metrics.OffsetY + a), c, adv, i);
                line.Glyphs.Add(gi);
                currentX += adv;
                lastKernGlyph = g.GlyphIndex;
                lastKernFont = g.Font;
            }

            return end;
        }


        private float GetSpaceWidth(FontSystem fontSystem)
        {
            var spaceGlyph = fontSystem.GetOrCreateGlyph(' ', Settings.PixelSize, Settings.Font);
            return spaceGlyph?.Metrics.AdvanceWidth ?? Settings.PixelSize * 0.25f;
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
                        float glyphStart = glyph.Position.X - glyph.Glyph.Metrics.OffsetX;
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

                        currentX = glyphStart + glyph.AdvanceWidth;
                        currentIndex = glyph.CharIndex + 1;
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
            for (int li = 0; li < Lines.Count; li++)
            {
                var l = Lines[li];
                if (position.Y < l.Position.Y + l.Height)
                {
                    line = l;
                    break;
                }
                line = l;
            }

            float currentX = 0f;
            int currentIndex = line.StartIndex;

            foreach (var glyph in line.Glyphs)
            {
                float glyphStart = glyph.Position.X - glyph.Glyph.Metrics.OffsetX;
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
                    float mid = glyphStart + glyph.AdvanceWidth * 0.5f;
                    return position.X < mid ? glyph.CharIndex : glyph.CharIndex + 1;
                }

                currentX = glyphEnd;
                currentIndex = glyph.CharIndex + 1;
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
                bool isLastLine = Lines.IndexOf(line) == Lines.Count - 1;
                if (isLastLine && position.X >= line.Width)
                {
                    return Math.Max(result, Text.Length);
                }
                
                return result;
            }

            // If no trailing spaces but we're past the end of visible content on the last line
            bool isLastLine2 = Lines.IndexOf(line) == Lines.Count - 1;
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
