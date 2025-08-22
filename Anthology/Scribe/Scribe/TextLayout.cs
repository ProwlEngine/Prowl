using StbTrueTypeSharp;
using System.Numerics;

namespace Prowl.Scribe
{
    public class TextLayout
    {
        public List<Line> Lines { get; private set; }
        public Vector2 Size { get; private set; }
        public TextLayoutSettings Settings { get; private set; }
        public string Text { get; private set; }

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
                Size = Vector2.Zero;
                return;
            }

            LayoutText(fontSystem);
            ApplyAlignment();
            CalculateSize();
        }

        private void LayoutText(FontSystem fontSystem)
        {
            float currentX = 0f;
            float currentY = 0f;
            int i = 0;

            Lines.Clear();

            var line = new Line(new Vector2(0, currentY), 0);

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

            // Kerning baseline: do NOT kern across whitespace
            int lastCodepointForKerning = 0;

            // Ascender cache per font object; we only need 'a' to place the glyph vertically
            var ascenderCache = new Dictionary<object, float>(8);
            float GetAscender(FontInfo font)
            {
                if (ascenderCache.TryGetValue(font, out var a)) return a;
                fontSystem.GetScaledVMetrics(font, pixelSize, out var asc, out _, out _);
                ascenderCache[font] = asc;
                return asc;
            }

            // Local to place a single glyph (no cross-line kerning)
            void EmitGlyph(AtlasGlyph glyph, FontInfo font, char c, float offsetX, float offsetY, float advanceBase, ref float x, List<GlyphInstance> outList)
            {
                float a = GetAscender(font);
                var gi = new GlyphInstance(glyph, new Vector2(x + offsetX, offsetY + a), c, advanceBase);
                outList.Add(gi);
                x += advanceBase;
                lastCodepointForKerning = c; // kerning only continues within the current word/run
            }

            while (i < len)
            {
                char ch = text[i];

                // Explicit newline
                if (ch == '\n')
                {
                    FinalizeLine(ref line, currentY, lineHeight);
                    currentX = 0f;
                    currentY += lineHeight;
                    i++;
                    line = new Line(new Vector2(0, currentY), i);
                    lastCodepointForKerning = 0;
                    continue;
                }

                // Tabs
                if (ch == '\t')
                {
                    float tabStop = ((int)(currentX / tabWidth) + 1) * tabWidth;
                    currentX = tabStop;
                    i++;
                    lastCodepointForKerning = 0; // do not kern across whitespace
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
                        FinalizeLine(ref line, currentY, lineHeight);
                        currentX = 0f;
                        currentY += lineHeight;
                        line = new Line(new Vector2(0, currentY), i);
                    }
                    else
                    {
                        currentX += runAdvance;
                    }

                    lastCodepointForKerning = 0; // break kerning chain
                    continue;
                }

                // Word [i..wordEnd)
                int wordStart = i;
                int wordEnd = FindWordEnd(i);

                // -------- First pass for the word: measure cheaply (no vmetrics per glyph) --------
                float wordWidthNoLeadingKerning = 0f;
                float firstLeadingKerning = 0f;   // kerning against previous non-whitespace glyph (we'll apply only if not at line start)
                bool hadFirstGlyph = false;
                int firstGlyphCodepoint = 0;
                int prevCodepoint = 0;

                for (int j = wordStart; j < wordEnd; j++)
                {
                    char c = text[j];
                    var g = fontSystem.GetOrCreateGlyph(c, pixelSize, Settings.PreferredFont);
                    if (g == null) continue;

                    var adv = g.Metrics.AdvanceWidth + Settings.LetterSpacing;

                    // internal kerning (within the word)
                    if (prevCodepoint != 0)
                        wordWidthNoLeadingKerning += fontSystem.GetKerning(g.Font, prevCodepoint, c, pixelSize);

                    wordWidthNoLeadingKerning += adv;

                    if (!hadFirstGlyph)
                    {
                        hadFirstGlyph = true;
                        firstGlyphCodepoint = c;

                        // kerning between previous run (if any) and first glyph of this word
                        if (lastCodepointForKerning != 0)
                            firstLeadingKerning = fontSystem.GetKerning(g.Font, lastCodepointForKerning, c, pixelSize);
                    }

                    prevCodepoint = c;
                }

                // Word may be empty if all codepoints were missing; just skip it
                if (!hadFirstGlyph)
                {
                    i = wordEnd;
                    lastCodepointForKerning = 0;
                    continue;
                }

                float prospective = currentX + (line.Glyphs.Count > 0 ? firstLeadingKerning : 0f) + wordWidthNoLeadingKerning;

                // -------- Wrapping decisions --------
                if (wrapEnabled && prospective > maxWidth)
                {
                    // If current line has content, wrap before placing the word
                    if (line.Glyphs.Count > 0)
                    {
                        FinalizeLine(ref line, currentY, lineHeight);
                        currentX = 0f;
                        currentY += lineHeight;
                        line = new Line(new Vector2(0, currentY), wordStart);
                        lastCodepointForKerning = 0; // new line: no leading kerning
                    }

                    // If the word itself is too long for an empty line, split it (char-level)
                    if (wordWidthNoLeadingKerning > maxWidth)
                    {
                        i = LayoutLongWordFast(fontSystem, ref line, ref currentX, ref currentY, lineHeight,
                                               wordStart, wordEnd, tabWidth, spaceAdvance, wrapEnabled, maxWidth, GetAscender);
                        lastCodepointForKerning = 0;
                        continue;
                    }
                }

                // -------- Second pass for the word: actually place glyphs --------
                // leading kerning only if not at line start
                float leading = (line.Glyphs.Count > 0) ? firstLeadingKerning : 0f;

                if (leading != 0f) currentX += leading;

                // We still fetch glyphs here, but (a) only once total for the common no-wrap path,
                // and (b) we cache ascender per font, not per glyph.
                int prevForKern = 0;
                for (int j = wordStart; j < wordEnd; j++)
                {
                    char c = text[j];
                    var g = fontSystem.GetOrCreateGlyph(c, pixelSize, Settings.PreferredFont);
                    if (g == null) continue;

                    if (prevForKern != 0)
                    {
                        float k = fontSystem.GetKerning(g.Font, prevForKern, c, pixelSize);
                        if (k != 0f) currentX += k;
                    }

                    EmitGlyph(g, g.Font, c, g.Metrics.OffsetX, g.Metrics.OffsetY,
                              g.Metrics.AdvanceWidth + Settings.LetterSpacing,
                              ref currentX, line.Glyphs);

                    prevForKern = c;
                }

                // Move i past this word
                i = wordEnd;
            }

            // Finalize last line
            if (line.Glyphs.Count > 0 || Lines.Count == 0)
                FinalizeLine(ref line, currentY, lineHeight);
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
            Func<FontInfo, float> getAscender)
        {
            float pixelSize = Settings.PixelSize;

            int lastKernCode = 0;

            for (int i = start; i < end; i++)
            {
                char c = Text[i];
                var g = fontSystem.GetOrCreateGlyph(c, pixelSize, Settings.PreferredFont);
                if (g == null) continue;

                float adv = g.Metrics.AdvanceWidth + Settings.LetterSpacing;

                // If next char doesn't fit, wrap (but not before placing at least one glyph)
                float k = 0f;
                if (lastKernCode != 0)
                    k = fontSystem.GetKerning(g.Font, lastKernCode, c, pixelSize);

                if (wrapEnabled && line.Glyphs.Count > 0 && currentX + k + adv > maxWidth)
                {
                    FinalizeLine(ref line, currentY, lineHeight);
                    currentX = 0f;
                    currentY += lineHeight;
                    line = new Line(new Vector2(0, currentY), i);
                    lastKernCode = 0; // break kerning across lines
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
                var gi = new GlyphInstance(g, new Vector2(currentX + g.Metrics.OffsetX, g.Metrics.OffsetY + a), c, adv);
                line.Glyphs.Add(gi);
                currentX += adv;
                lastKernCode = c;
            }

            return end;
        }


        private float GetSpaceWidth(FontSystem fontSystem)
        {
            var spaceGlyph = fontSystem.GetOrCreateGlyph(' ', Settings.PixelSize, Settings.PreferredFont);
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

        private float MeasureWord(FontSystem fontSystem, int start, int end)
        {
            float width = 0;
            int lastCodepoint = 0;

            for (int i = start; i < end; i++)
            {
                char c = Text[i];
                var glyph = fontSystem.GetOrCreateGlyph(c, Settings.PixelSize, Settings.PreferredFont);
                if (glyph == null) continue;

                if (lastCodepoint != 0)
                {
                    width += fontSystem.GetKerning(glyph.Font, lastCodepoint, c, Settings.PixelSize);
                }

                width += glyph.Metrics.AdvanceWidth + Settings.LetterSpacing;
                lastCodepoint = c;
            }

            return width;
        }

        private int LayoutWord(FontSystem fontSystem, ref Line currentLine, ref float currentX, int start, int end, float lineHeight)
        {
            int lastCodepoint = currentLine.Glyphs.Count > 0 ? currentLine.Glyphs[^1].Character : 0;


            for (int i = start; i < end; i++)
            {
                char c = Text[i];
                var glyph = fontSystem.GetOrCreateGlyph(c, Settings.PixelSize, Settings.PreferredFont);
                if (glyph == null) continue;

                // Apply kerning
                if (lastCodepoint != 0)
                {
                    float kerning = fontSystem.GetKerning(glyph.Font, lastCodepoint, c, Settings.PixelSize);
                    currentX += kerning;
                }

                fontSystem.GetScaledVMetrics(glyph.Font, Settings.PixelSize, out var a, out var d, out var l);

                // Create glyph instance
                var glyphInstance = new GlyphInstance(
                    glyph,
                    new Vector2(currentX + glyph.Metrics.OffsetX, glyph.Metrics.OffsetY + a),
                    c,
                    glyph.Metrics.AdvanceWidth + Settings.LetterSpacing
                );

                currentLine.Glyphs.Add(glyphInstance);
                currentX += glyphInstance.AdvanceWidth;
                lastCodepoint = c;
            }

            return end;
        }

        private int LayoutLongWord(FontSystem fontSystem, ref Line currentLine, ref float currentX,
            ref float currentY, float lineHeight, int start, int end)
        {
            int i = start;
            while (i < end)
            {
                char c = Text[i];
                var glyph = fontSystem.GetOrCreateGlyph(c, Settings.PixelSize, Settings.PreferredFont);
                if (glyph == null)
                {
                    i++;
                    continue;
                }

                float charWidth = glyph.Metrics.AdvanceWidth + Settings.LetterSpacing;

                // Check if character fits on current line
                if (Settings.MaxWidth > 0 && currentX + charWidth > Settings.MaxWidth && currentLine.Glyphs.Count > 0)
                {
                    FinalizeLine(ref currentLine, currentY, lineHeight);
                    currentX = 0;
                    currentY += lineHeight;
                    currentLine = new Line(new Vector2(0, currentY), i);
                }

                fontSystem.GetScaledVMetrics(glyph.Font, Settings.PixelSize, out var a, out var d, out var l);

                // Add character to current line
                var glyphInstance = new GlyphInstance(
                    glyph,
                    new Vector2(currentX + glyph.Metrics.OffsetX, glyph.Metrics.OffsetY + a),
                    c,
                    charWidth
                );

                currentLine.Glyphs.Add(glyphInstance);
                currentX += charWidth;
                i++;
            }

            return i;
        }

        private void FinalizeLine(ref Line line, float y, float lineHeight)
        {
            line.Position = new Vector2(0, y);
            line.Height = lineHeight;
            line.Width = line.Glyphs.Count > 0 ? line.Glyphs[^1].Position.X + line.Glyphs[^1].AdvanceWidth : 0;
            line.EndIndex = line.StartIndex + line.Glyphs.Count;
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
                        glyph.Position = new Vector2(glyph.Position.X + offset, glyph.Position.Y);
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
                Size = Vector2.Zero;
                return;
            }

            float maxWidth = GetMaxLineWidth();
            float totalHeight = Lines[^1].Position.Y + Lines[^1].Height;
            Size = new Vector2(maxWidth, totalHeight);
        }
    }
}
