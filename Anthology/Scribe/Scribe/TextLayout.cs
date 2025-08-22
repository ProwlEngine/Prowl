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
            float currentX = 0;
            float currentY = 0;
            int charIndex = 0;
            var currentLine = new Line(new Vector2(0, currentY), charIndex);

            float spaceWidth = GetSpaceWidth(fontSystem);
            float tabWidth = spaceWidth * Settings.TabSize;
            float lineHeight = Settings.PixelSize * Settings.LineHeight;

            while (charIndex < Text.Length)
            {
                char c = Text[charIndex];

                // Handle newlines
                if (c == '\n')
                {
                    FinalizeLine(ref currentLine, currentY, lineHeight);
                    currentX = 0;
                    currentY += lineHeight;
                    charIndex++;
                    currentLine = new Line(new Vector2(0, currentY), charIndex);
                    continue;
                }

                // Handle tabs
                if (c == '\t')
                {
                    float tabStop = ((int)(currentX / tabWidth) + 1) * tabWidth;
                    currentX = tabStop;
                    charIndex++;
                    continue;
                }

                // Handle spaces
                if (char.IsWhiteSpace(c))
                {
                    float spaceAdvance = spaceWidth + Settings.WordSpacing;

                    // Check if we need to wrap
                    if (Settings.WrapMode == TextWrapMode.Wrap && Settings.MaxWidth > 0 &&
                        currentX + spaceAdvance > Settings.MaxWidth && currentLine.Glyphs.Count > 0)
                    {
                        FinalizeLine(ref currentLine, currentY, lineHeight);
                        currentX = 0;
                        currentY += lineHeight;
                        currentLine = new Line(new Vector2(0, currentY), charIndex + 1);
                    }
                    else
                    {
                        currentX += spaceAdvance;
                    }
                    charIndex++;
                    continue;
                }

                // Handle regular characters and words
                int wordEnd = FindWordEnd(charIndex);
                float wordWidth = MeasureWord(fontSystem, charIndex, wordEnd);

                // Check if word needs wrapping
                if (Settings.WrapMode == TextWrapMode.Wrap && Settings.MaxWidth > 0 &&
                    currentX + wordWidth > Settings.MaxWidth)
                {
                    // If current line has content and word doesn't fit, wrap to next line
                    if (currentLine.Glyphs.Count > 0)
                    {
                        FinalizeLine(ref currentLine, currentY, lineHeight);
                        currentX = 0;
                        currentY += lineHeight;
                        currentLine = new Line(new Vector2(0, currentY), charIndex);
                    }

                    // If word is still too long, split it
                    if (Settings.MaxWidth > 0 && wordWidth > Settings.MaxWidth)
                    {
                        charIndex = LayoutLongWord(fontSystem, ref currentLine, ref currentX, ref currentY,
                            lineHeight, charIndex, wordEnd);
                        continue;
                    }
                }

                // Layout the word
                charIndex = LayoutWord(fontSystem, ref currentLine, ref currentX, charIndex, wordEnd, lineHeight);
            }

            // Finalize the last line
            if (currentLine.Glyphs.Count > 0 || Lines.Count == 0)
            {
                FinalizeLine(ref currentLine, currentY, lineHeight);
            }
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
