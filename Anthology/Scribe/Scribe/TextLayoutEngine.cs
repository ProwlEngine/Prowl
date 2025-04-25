//using StbTrueTypeSharp;
//using System.Numerics;
//using System.Text;
//
//namespace Prowl.Scribe
//{
//    // =============================
//    // Settings & Data Contracts
//    // =============================
//
//
//    public enum TextAlignment { Left, Right, Center, Justify }
//    public enum WrapMode { NoWrap, Word, Character }
//
//
//    public sealed class TextLayoutSettings
//    {
//        public float MaxWidth { get; set; } = float.PositiveInfinity; // layout width in pixels
//        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
//        public WrapMode Wrap { get; set; } = WrapMode.Word;
//
//
//        // Line height: if > 0, used as absolute pixels; otherwise multiplier with font line metrics
//        public float LineHeight { get; set; } = 0f; // 0 => use multiplier
//        public float LineHeightMultiplier { get; set; } = 1.0f; // used when LineHeight == 0
//
//
//        // Spacing
//        public float LetterSpacing { get; set; } = 0f; // additional space per-glyph
//        public float WordSpacing { get; set; } = 0f; // additional space for spaces
//
//
//        // Tabs
//        public int TabWidthInSpaces { get; set; } = 4; // width of a tab in spaces
//
//        // Paragraph
//        public float ParagraphSpacing { get; set; } = 0f; // extra pixels after a paragraph (\n\n)
//
//        // Drawing origin baseline snap
//        public bool PixelSnap { get; set; } = true;
//    }
//
//
//    public sealed class TextSpan
//    {
//        public string Text { get; set; } = string.Empty;
//        public FontInfo? Font { get; set; } = null; // optional preferred font for this span
//        public float PixelSize { get; set; } = 16f; // px size for this span
//        public FontColor Color { get; set; } = new FontColor(255, 255, 255, 255);
//        public float? LetterSpacing { get; set; } = null; // overrides global
//        public float? WordSpacing { get; set; } = null; // overrides global
//        public bool Underline { get; set; } = false;
//        public bool Strikethrough { get; set; } = false;
//
//
//        public TextSpan() { }
//        public TextSpan(string text) { Text = text; }
//        public TextSpan(string text, FontInfo font, float size, FontColor color)
//        {
//            Text = text; Font = font; PixelSize = size; Color = color;
//        }
//    }
//
//
//    public sealed class StyledText
//    {
//        public List<TextSpan> Spans { get; } = new();
//        public StyledText Add(TextSpan span) { Spans.Add(span); return this; }
//
//
//        public static StyledText FromPlain(string text, FontInfo font, float size, FontColor color)
//        {
//            var s = new StyledText();
//            s.Spans.Add(new TextSpan(text, font, size, color));
//            return s;
//        }
//    }
//
//
//    // =============================
//    // Output structures
//    // =============================
//
//
//    public sealed class TextGlyph
//    {
//        public AtlasGlyph AtlasGlyph; // glyph + atlas placement
//        public Vector2 Pos; // top-left position where the bitmap quad should be drawn (already includes OffsetX/Y)
//        public Vector2 Size; // width/height
//        public FontColor Color;
//        public FontInfo Font;
//        public float PixelSize;
//        public int SourceTextIndex; // index into original concatenated text for hit-testing
//        public char Codepoint; // original char (approx; surrogate pairs will keep last unit)
//        public bool IsWhitespace; // for justification
//        public bool IsVisible = true; // false for e.g. soft hyphen when not broken
//    }
//
//
//    public sealed class TextLayoutLine
//    {
//        public List<TextGlyph> Glyphs { get; } = new();
//        public float Width; // ink width (sum of advances) before alignment/justification
//        public float Ascent; // max ascent among runs on this line
//        public float Descent; // max descent among runs on this line (positive value)
//        public float LineGap; // max line gap among runs
//        public float Baseline; // y from line top to baseline
//        public float Height; // final line box height
//        public bool IsLastInParagraph;
//        public int StartTextIndex; // range for hit-testing
//        public int EndTextIndex;
//    }
//
//
//    public sealed class TextLayout
//    {
//        public List<TextLayoutLine> Lines { get; } = new();
//        public Vector2 Size; // overall layout size
//        public RectangleF Bounds; // origin-based bounds
//        public int TextLength; // original text length
//
//
//        // For caret & selection helpers
//        public (int line, int charIndex) HitTest(Vector2 point)
//        {
//            // naive: find the line by y, then iterate glyphs by x
//            float y = 0;
//            for (int i = 0; i < Lines.Count; i++)
//            {
//                var ln = Lines[i];
//                float h = ln.Height;
//                if (point.Y < y + h)
//                {
//                    // within this line
//                    float x = 0;
//                    for (int g = 0; g < ln.Glyphs.Count; g++)
//                    {
//                        var gg = ln.Glyphs[g];
//                        float nextX = gg.Pos.X + gg.Size.X; // approximate advance by glyph width
//                        if (point.X < nextX) return (i, Math.Max(ln.StartTextIndex, gg.SourceTextIndex));
//                        x = nextX;
//                    }
//                    return (i, ln.EndTextIndex);
//                }
//                y += h;
//            }
//            return (Lines.Count - 1, Lines.LastOrDefault()?.EndTextIndex ?? 0);
//        }
//    }
//
//
//    // =============================
//    // Layout Engine
//    // =============================
//
//
//    public sealed class TextLayoutEngine
//    {
//        private readonly FontSystem _fontSystem;
//        private readonly IFontRenderer _renderer;
//
//
//        public TextLayoutEngine(FontSystem fontSystem)
//        {
//            _fontSystem = fontSystem ?? throw new ArgumentNullException(nameof(fontSystem));
//            _renderer = GetRenderer(fontSystem);
//        }
//
//
//        private static IFontRenderer GetRenderer(FontSystem fs)
//        {
//            // FontSystem already owns the renderer internally; reflect it if needed.
//            // If that’s not available in your version, plumb it through the constructor.
//            var f = typeof(FontSystem).GetField("renderer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
//            return (IFontRenderer)(f?.GetValue(fs) ?? throw new InvalidOperationException("FontSystem renderer not accessible. Pass it explicitly."));
//        }
//
//
//        public TextLayout Layout(StyledText styled, TextLayoutSettings settings)
//        {
//            if (styled.Spans.Count == 0)
//                return new TextLayout { Size = Vector2.Zero, Bounds = RectangleF.Empty, TextLength = 0 };
//
//
//            // Prepare paragraphs by splitting spans on \n
//            var paragraphs = ExplodeParagraphs(styled.Spans);
//
//
//            var layout = new TextLayout();
//            float penY = 0f;
//            int globalIndex = 0; // character index in original text
//
//
//            foreach (var para in paragraphs)
//            {
//                var lines = LayoutParagraph(para, settings, ref globalIndex);
//                for (int i = 0; i < lines.Count; i++)
//                {
//                    var line = lines[i];
//                    bool last = (i == lines.Count - 1);
//                    line.IsLastInParagraph = last;
//
//
//                    // Apply alignment & justification
//                    ApplyAlignmentAndJustify(line, settings);
//
//
//                    // Move line vertically
//                    float top = penY;
//                    float baseline = top + line.Baseline;
//                    foreach (var g in line.Glyphs)
//                    {
//                        var p = g.Pos; p.Y += top; g.Pos = p;
//                    }
//
//
//                    layout.Lines.Add(line);
//
//
//                    // advance penY by line height (absolute or multiplier)
//                    float nextAdvance = ResolveLineHeight(settings, line);
//                    penY += nextAdvance;
//                }
//
//
//                penY += settings.ParagraphSpacing; // extra gap after paragraph
//            }
//
//            // Compute overall size
//            float width = 0f;
//            float height = 0f;
//            foreach (var ln in layout.Lines)
//            {
//                float w = 0f;
//                if (ln.Glyphs.Count > 0)
//                {
//                    var last = ln.Glyphs[ln.Glyphs.Count - 1];
//                    w = last.Pos.X + last.Size.X; // right-most pixel
//                }
//                width = MathF.Max(width, w);
//                height += ln.Height;
//            }
//
//
//            if (settings.PixelSnap) { width = MathF.Round(width); height = MathF.Round(height); }
//
//
//            layout.Size = new Vector2(width, height);
//            layout.Bounds = new RectangleF(0, 0, width, height);
//            return layout;
//        }
//
//
//        public void Draw(TextLayout layout, Vector2 origin)
//        {
//            if (layout.Lines.Count == 0) return;
//
//
//            var vertices = new List<IFontRenderer.Vertex>(layout.Lines.Sum(l => l.Glyphs.Count * 4));
//            var indices = new List<int>(layout.Lines.Sum(l => l.Glyphs.Count * 6));
//
//
//            int vtx = 0;
//            foreach (var line in layout.Lines)
//            {
//                foreach (var g in line.Glyphs)
//                {
//                    if (!g.IsVisible || g.AtlasGlyph == null || !g.AtlasGlyph.IsInAtlas) continue;
//
//
//                    float x = origin.X + g.Pos.X;
//                    float y = origin.Y + g.Pos.Y;
//                    float w = g.Size.X;
//                    float h = g.Size.Y;
//
//
//                    vertices.Add(new IFontRenderer.Vertex(new Vector3(x, y, 0), g.Color, new Vector2(g.AtlasGlyph.U0, g.AtlasGlyph.V0)));
//                    vertices.Add(new IFontRenderer.Vertex(new Vector3(x + w, y, 0), g.Color, new Vector2(g.AtlasGlyph.U1, g.AtlasGlyph.V0)));
//                    vertices.Add(new IFontRenderer.Vertex(new Vector3(x, y + h, 0), g.Color, new Vector2(g.AtlasGlyph.U0, g.AtlasGlyph.V1)));
//                    vertices.Add(new IFontRenderer.Vertex(new Vector3(x + w, y + h, 0), g.Color, new Vector2(g.AtlasGlyph.U1, g.AtlasGlyph.V1)));
//
//
//                    indices.Add(vtx + 0); indices.Add(vtx + 1); indices.Add(vtx + 2);
//                    indices.Add(vtx + 1); indices.Add(vtx + 3); indices.Add(vtx + 2);
//                    vtx += 4;
//                }
//            }
//
//
//            // Draw in one batch against the atlas texture owned by FontSystem
//            var texProp = typeof(FontSystem).GetProperty("Texture");
//            var atlasTexture = texProp!.GetValue(_fontSystem);
//            _renderer.DrawQuads(atlasTexture!, vertices.ToArray(), indices.ToArray());
//        }
//
//
//        // -----------------------------
//        // Paragraph Layout
//        // -----------------------------
//
//
//        private sealed class ParaSpan
//        {
//            public TextSpan Span = null!;
//            public string Text = string.Empty; // text for this paragraph (no trailing \n)
//        }
//
//
//        private List<List<ParaSpan>> ExplodeParagraphs(List<TextSpan> spans)
//        {
//            var paragraphs = new List<List<ParaSpan>>();
//            var current = new List<ParaSpan>();
//
//
//            foreach (var sp in spans)
//            {
//                if (string.IsNullOrEmpty(sp.Text)) continue;
//
//
//                int start = 0;
//                for (int i = 0; i < sp.Text.Length; i++)
//                {
//                    if (sp.Text[i] == '\n')
//                    {
//                        string chunk = sp.Text.Substring(start, i - start);
//                        if (chunk.Length > 0) current.Add(new ParaSpan { Span = sp, Text = chunk });
//                        paragraphs.Add(current);
//                        current = new List<ParaSpan>();
//                        start = i + 1;
//                    }
//                }
//                if (start < sp.Text.Length)
//                {
//                    current.Add(new ParaSpan { Span = sp, Text = sp.Text.Substring(start) });
//                }
//            }
//            if (current.Count > 0) paragraphs.Add(current);
//            return paragraphs;
//        }
//
//
//        private List<TextLayoutLine> LayoutParagraph(List<ParaSpan> para, TextLayoutSettings settings, ref int globalTextIndex)
//        {
//            var lines = new List<TextLayoutLine>();
//            if (para.Count == 0) return lines;
//
//
//            // Tokenize at word boundaries to enable word-wrapping.
//            var tokens = Tokenize(para);
//
//
//            // Build lines by filling tokens until width exceed.
//            var line = NewEmptyLine();
//            float penX = 0f;
//
//
//            float spaceWidthCache = -1f;
//            float lastAdvance = 0f;
//
//
//            foreach (var tk in tokens)
//            {
//                // Measure token width and glyphs (without placing yet), so we can decide wrapping.
//                var probe = ShapeToken(tk, settings);
//                float tokenWidth = probe.width;
//
//
//                bool needsWrap = settings.Wrap != WrapMode.NoWrap && penX > 0 && penX + tokenWidth > settings.MaxWidth && !tk.IsHardBreak;
//                if (needsWrap)
//                {
//                    // try to wrap before this token
//                    FinalizeLine(line);
//                    lines.Add(line);
//                    line = NewEmptyLine();
//                    penX = 0f;
//                }
//
//
//                // Place token's glyphs on this line
//                foreach (var g in probe.glyphs)
//                {
//                    if (g.IsWhitespace)
//                    {
//                        // Track word-space for justification
//                        line.Width += g.Size.X;
//                    }
//                    else
//                    {
//                        line.Width += g.Size.X;
//                    }
//
//
//                    var pos = g.Pos; pos.X += penX; g.Pos = pos;
//                    line.Glyphs.Add(g);
//
//
//                    // Update line vertical metrics
//                    var m = GetVMetrics(g.Font, g.PixelSize);
//                    line.Ascent = MathF.Max(line.Ascent, m.ascent);
//                    line.Descent = MathF.Max(line.Descent, -m.descent); // descent returned negative in stb; store positive amount
//                    line.LineGap = MathF.Max(line.LineGap, m.lineGap);
//                }
//
//
//                penX += tokenWidth;
//
//
//                if (tk.IsHardBreak)
//                {
//                    FinalizeLine(line);
//                    lines.Add(line);
//                    line = NewEmptyLine();
//                    penX = 0f;
//                }
//            }
//
//
//            if (line.Glyphs.Count > 0)
//            {
//                FinalizeLine(line);
//                lines.Add(line);
//            }
//
//
//            // Assign source indices & compute Start/EndTextIndex per line
//            int idx = globalTextIndex;
//            foreach (var ln in lines)
//            {
//                ln.StartTextIndex = idx;
//                foreach (var g in ln.Glyphs)
//                {
//                    g.SourceTextIndex = idx++;
//                }
//                ln.EndTextIndex = Math.Max(ln.StartTextIndex, idx - 1);
//            }
//            globalTextIndex = idx;
//
//
//            return lines;
//
//
//            static TextLayoutLine NewEmptyLine() => new TextLayoutLine { Width = 0, Ascent = 0, Descent = 0, LineGap = 0, Baseline = 0, Height = 0 };
//
//
//            void FinalizeLine(TextLayoutLine ln)
//            {
//                // baseline = max ascent; height = ascent + descent + lineGap (loosely matching stb/vmetrics semantics)
//                ln.Baseline = ln.Ascent;
//                ln.Height = MathF.Max(1, ln.Ascent + ln.Descent + ln.LineGap);
//            }
//        }
//
//        private (float width, List<TextGlyph> glyphs) ShapeToken(Token tk, TextLayoutSettings settings)
//        {
//            var glyphs = new List<TextGlyph>(tk.Text.Length);
//            float x = 0f;
//            FontInfo? currentFont = null;
//            AtlasGlyph? prevAtlas = null;
//            int prevGlyphIndex = 0;
//
//
//            float letterSpacing = tk.Span.LetterSpacing ?? settings.LetterSpacing;
//            float wordSpacing = tk.Span.WordSpacing ?? settings.WordSpacing;
//
//
//            // We iterate runes to correctly handle surrogate pairs and emojis.
//            var runeEnum = tk.Text.EnumerateRunes();
//            foreach (var rune in runeEnum)
//            {
//                int cp = rune.Value;
//                bool isSpace = rune.IsWhiteSpace();
//                bool isTab = rune.Value == '\t';
//                bool isSoftHyphen = rune.Value == 0x00AD; // \u00AD
//                bool isZeroWidthSpace = rune.Value == 0x200B;
//
//
//                if (isZeroWidthSpace)
//                {
//                    // break opportunity with no width; skip
//                    continue;
//                }
//
//
//                // Resolve font (preferred first, then fallbacks from FontSystem)
//                var font = tk.Span.Font != null && _fontSystem.HasGlyph(tk.Span.Font, cp)
//                ? tk.Span.Font
//                : ResolveFallbackFont(cp);
//
//
//                if (font == null)
//                {
//                    // missing glyph entirely -> skip
//                    continue;
//                }
//
//
//                float px = tk.Span.PixelSize;
//                var atlasGlyph = _fontSystem.GetOrCreateGlyph(cp, px, font);
//                if (atlasGlyph == null)
//                    continue;
//
//
//                // Kerning (same font only)
//                if (prevAtlas != null && currentFont == font && prevGlyphIndex != 0 && atlasGlyph.GlyphIndex != 0)
//                {
//                    float kern = _fontSystem.GetKerning(font, prevAtlas.Codepoint, cp, px);
//                    x += kern;
//                }
//
//
//                // Tab handling => advance to next tab stop or N spaces equivalent
//                if (isTab)
//                {
//                    float spaceAdvance = MeasureSpaceAdvance(font, px);
//                    float tabWidth = NextTabStopUsingSpaces(x, spaceAdvance, settings.TabWidthInSpaces);
//
//
//                    x = tabWidth;
//                    prevAtlas = null; prevGlyphIndex = 0; currentFont = font;
//                    continue;
//                }
//
//
//                // Soft hyphen (invisible unless wrap). We encode it as zero-advance/invisible glyph.
//                if (isSoftHyphen)
//                {
//                    // Represent as an invisible glyph; if wrap triggers, caller may insert visible hyphen.
//                    var gsh = new TextGlyph {
//                        AtlasGlyph = atlasGlyph,
//                        Pos = new Vector2(x + atlasGlyph.Metrics.OffsetX, atlasGlyph.Metrics.OffsetY),
//                        Size = new Vector2(atlasGlyph.Metrics.Width, atlasGlyph.Metrics.Height),
//                        Color = tk.Span.Color,
//                        Font = font,
//                        PixelSize = px,
//                        Codepoint = (char)cp,
//                        IsWhitespace = false,
//                        IsVisible = false,
//                    };
//                    glyphs.Add(gsh);
//                    // no advance
//                    continue;
//                }
//
//
//                // Normal glyph (including spaces)
//                var g = new TextGlyph {
//                    AtlasGlyph = atlasGlyph,
//                    Pos = new Vector2(x + atlasGlyph.Metrics.OffsetX, atlasGlyph.Metrics.OffsetY),
//                    Size = new Vector2(atlasGlyph.Metrics.Width, atlasGlyph.Metrics.Height),
//                    Color = tk.Span.Color,
//                    Font = font,
//                    PixelSize = px,
//                    Codepoint = (char)cp,
//                    IsWhitespace = isSpace && !Rune.IsControl(rune),
//                    IsVisible = true,
//                };
//
//
//                glyphs.Add(g);
//
//
//                float advance = atlasGlyph.Metrics.AdvanceWidth + letterSpacing;
//                if (g.IsWhitespace) advance += wordSpacing;
//                x += advance;
//
//
//                prevAtlas = atlasGlyph;
//                prevGlyphIndex = atlasGlyph.GlyphIndex;
//                currentFont = font;
//            }
//
//
//            return (x, glyphs);
//
//
//            static float NextTabStop(float currentX, List<float> stops)
//            {
//                foreach (var s in stops)
//                {
//                    if (s > currentX + 0.01f) return s;
//                }
//                return currentX; // no movement if past last stop
//            }
//            static float NextTabStopUsingSpaces(float currentX, float spaceAdvance, int spaces)
//            {
//                float tab = spaceAdvance * Math.Max(1, spaces);
//                if (tab <= 0.001f) return currentX;
//                float n = MathF.Floor(currentX / tab) + 1f;
//                return n * tab;
//            }
//        }
//
//
//        private (float ascent, float descent, float lineGap) GetVMetrics(FontInfo font, float px)
//        {
//            _fontSystem.GetScaledVMetrics(font, px, out var a, out var d, out var g);
//            return (a, d, g);
//        }
//
//
//        private float MeasureSpaceAdvance(FontInfo font, float px)
//        {
//            int space = ' ';
//            var g = _fontSystem.GetOrCreateGlyph(space, px, font);
//            return g?.Metrics.AdvanceWidth ?? (px * 0.5f);
//        }
//
//
//        private FontInfo? ResolveFallbackFont(int codepoint)
//        {
//            // Preferred order: first font that has this glyph.
//            foreach (var f in _fontSystem.Fonts)
//                if (_fontSystem.HasGlyph(f, codepoint)) return f;
//            return null;
//        }
//
//
//        private void ApplyAlignmentAndJustify(TextLayoutLine line, TextLayoutSettings settings)
//        {
//            if (line.Glyphs.Count == 0) return;
//
//
//            float inkWidth = line.Glyphs.Count > 0 ? (line.Glyphs.Last().Pos.X + line.Glyphs.Last().Size.X) : line.Width;
//            float extra = MathF.Max(0, settings.MaxWidth - inkWidth);
//
//
//            // Baseline and vertical metrics are already computed; this method adjusts x-positions only.
//            if (settings.Alignment == TextAlignment.Left || settings.Wrap == WrapMode.NoWrap)
//            {
//                // nothing to do
//                return;
//            }
//
//
//            if (settings.Alignment == TextAlignment.Center)
//            {
//                float shift = extra * 0.5f;
//                foreach (var g in line.Glyphs) { var p = g.Pos; p.X += shift; g.Pos = p; }
//                return;
//            }
//
//
//            if (settings.Alignment == TextAlignment.Right)
//            {
//                float shift = extra;
//                foreach (var g in line.Glyphs) { var p = g.Pos; p.X += shift; g.Pos = p; }
//                return;
//            }
//
//
//            if (settings.Alignment == TextAlignment.Justify)
//            {
//                if (line.IsLastInParagraph) return; // common typesetting rule: do not justify last line
//
//
//                // Count adjustable gaps (spaces)
//                int spaceCount = 0;
//                foreach (var g in line.Glyphs) if (g.IsWhitespace) spaceCount++;
//                if (spaceCount == 0) return;
//
//
//                float addPerSpace = extra / spaceCount;
//                float running = 0f;
//                foreach (var g in line.Glyphs)
//                {
//                    var p = g.Pos; p.X += running; g.Pos = p;
//                    if (g.IsWhitespace) running += addPerSpace;
//                }
//            }
//        }
//
//        private sealed class Token
//        {
//            public TextSpan Span = null!;
//            public string Text = string.Empty; // text for this token
//            public bool IsHardBreak; // true for explicit \n (handled earlier) – kept for safety
//        }
//
//        private List<Token> Tokenize(List<ParaSpan> para)
//        {
//            // Word-aware tokenizer:
//            // - Splits on spaces and tabs but keeps them as tokens (for justification)
//            // - Keeps punctuation attached to words ("word,")
//            // - Soft hyphen stays in token
//            // - Zero width space creates a break opportunity but is removed during shaping
//            var result = new List<Token>();
//            foreach (var ps in para)
//            {
//                string s = ps.Text;
//                if (s.Length == 0) continue;
//
//
//                int start = 0;
//                for (int i = 0; i < s.Length; i++)
//                {
//                    char c = s[i];
//                    if (char.IsWhiteSpace(c))
//                    {
//                        if (i > start)
//                        {
//                            result.Add(new Token { Span = ps.Span, Text = s.Substring(start, i - start) });
//                        }
//                        // collect the whole whitespace run as a single token
//                        int j = i;
//                        while (j < s.Length && char.IsWhiteSpace(s[j]) && s[j] != '\n') j++;
//                        string ws = s.Substring(i, j - i);
//                        result.Add(new Token { Span = ps.Span, Text = ws });
//                        i = j - 1; start = j;
//                    }
//                }
//                if (start < s.Length)
//                {
//                    result.Add(new Token { Span = ps.Span, Text = s.Substring(start) });
//                }
//            }
//            return result;
//        }
//
//
//        private float ResolveLineHeight(TextLayoutSettings settings, TextLayoutLine line)
//        {
//            if (settings.LineHeight > 0) return settings.LineHeight;
//            float natural = line.Ascent + line.Descent + line.LineGap;
//            return MathF.Max(1, natural * MathF.Max(0.1f, settings.LineHeightMultiplier));
//        }
//    }
//
//
//    // =============================
//    // Helpers & Extensions
//    // =============================
//
//
//    public static class RuneExtensions
//    {
//        public static bool IsWhiteSpace(this Rune r)
//        {
//            // treat standard spaces and tabs as whitespace; NBSP should not break lines but visually is space
//            if (r.Value == '\t' || r.Value == ' ') return true;
//            // Some Unicode spaces
//            switch (r.Value)
//            {
//                case 0x00A0: // NBSP
//                case 0x1680:
//                case 0x2000:
//                case 0x2001:
//                case 0x2002:
//                case 0x2003:
//                case 0x2004:
//                case 0x2005:
//                case 0x2006:
//                case 0x2007:
//                case 0x2008:
//                case 0x2009:
//                case 0x200A:
//                case 0x202F:
//                case 0x205F:
//                case 0x3000:
//                    return true;
//            }
//            return char.IsWhiteSpace((char)r.Value);
//        }
//    }
//
//
//    // RectangleF for convenience without pulling in System.Drawing
//    public readonly struct RectangleF
//    {
//        public readonly float X, Y, Width, Height;
//        public static readonly RectangleF Empty = new RectangleF(0, 0, 0, 0);
//        public RectangleF(float x, float y, float w, float h) { X = x; Y = y; Width = w; Height = h; }
//    }
//}
