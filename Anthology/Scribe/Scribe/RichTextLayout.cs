using Prowl.Scribe.Internal;
using Prowl.Vector;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Prowl.Scribe
{
    /// <summary>
    /// A single laid-out glyph in a <see cref="RichTextLayout"/>. Stores everything the draw step
    /// needs to apply effects and emit a textured quad: the resolved atlas glyph, its baseline
    /// pen position, the pixel size used to lay it out, the visible char index, the per-glyph
    /// color, the static style flags (used for decorations), and the index of the line it
    /// belongs to.
    /// </summary>
    public struct RichGlyph
    {
        public AtlasGlyph Glyph;
        public Float2 Position;       // pen position at glyph's drawing origin (post-offset, baseline-relative)
        public float PixelSize;
        public float Advance;
        public char Character;
        public int CharIndex;          // index into VisibleText
        public int LineIndex;
        public FontColor Color;
        public RichStyleFlags Flags;
        public string LinkHref;        // null unless Flags has Link
    }

    public struct RichLine
    {
        public int FirstGlyph;
        public int GlyphCount;
        public int CharStart;
        public int CharEnd;
        public float Y;          // line-top y
        public float MaxAscent;  // baseline = Y + MaxAscent
        public float MaxDescent;
        public float Height;     // (MaxAscent + MaxDescent) * settings.LineHeight
        public float Width;
    }

    /// <summary>
    /// A renderable rich-text block. Parses a Unity-style tag string, lays out glyphs (with
    /// per-glyph font/size/color), and draws with time-driven animation effects.
    ///
    /// Lifecycle:
    ///   var rt = new RichTextLayout(source, settings);
    ///   rt.Update(fontSystem);           // parse + layout (call after settings/text changes)
    ///   rt.Draw(fontSystem, renderer, pos, time);  // first call anchors animation start time
    ///   rt.Reset();                      // re-anchor on next Draw (replays animations)
    /// </summary>
    public class RichTextLayout
    {
        private string _source;
        private RichTextLayoutSettings _settings;

        private RichTextParseResult _parsed;
        private List<RichGlyph> _glyphs = new List<RichGlyph>();
        private List<RichLine> _lines = new List<RichLine>();
        private Float2 _size;

        private double? _startTime;
        private int _atlasVersion = -1;

        // Reusable scratch buffers — keep DrawLayout-style allocation behavior.
        private readonly List<IFontRenderer.Vertex> _drawVertices = new List<IFontRenderer.Vertex>(1024);
        private readonly List<int> _drawIndices = new List<int>(1536);

        public string Source => _source;
        public string VisibleText => _parsed?.VisibleText ?? string.Empty;
        public RichTextLayoutSettings Settings => _settings;
        public IReadOnlyList<RichGlyph> Glyphs => _glyphs;
        public IReadOnlyList<RichLine> Lines => _lines;
        public IReadOnlyList<RichStyleSpan> Styles => _parsed?.Styles ?? (IReadOnlyList<RichStyleSpan>)Array.Empty<RichStyleSpan>();
        public IReadOnlyList<RichEffectSpan> Effects => _parsed?.Effects ?? (IReadOnlyList<RichEffectSpan>)Array.Empty<RichEffectSpan>();
        public IReadOnlyList<string> Warnings => _parsed?.Warnings ?? (IReadOnlyList<string>)Array.Empty<string>();
        public Float2 Size => _size;

        public RichTextLayout(string source, RichTextLayoutSettings settings)
        {
            _source = source ?? string.Empty;
            _settings = settings ?? RichTextLayoutSettings.Default;
        }

        public void SetSource(string source)
        {
            _source = source ?? string.Empty;
            _parsed = null;
        }

        public void SetSettings(RichTextLayoutSettings settings)
        {
            _settings = settings ?? RichTextLayoutSettings.Default;
            _parsed = null;
        }

        /// <summary>
        /// Call after the next <see cref="Draw"/> should treat itself as t=0 again. Use to
        /// replay typewriter-style intro effects.
        /// </summary>
        public void Reset() => _startTime = null;

        /// <summary>
        /// Parse tags and build glyphs/lines against the given font system.
        /// Safe to call repeatedly — only does work when text/settings/atlas have changed.
        /// </summary>
        public void Update(FontSystem fontSystem)
        {
            if (fontSystem == null) throw new ArgumentNullException(nameof(fontSystem));

            if (_parsed == null)
                _parsed = RichTextParser.Parse(_source);

            BuildGlyphs(fontSystem);
            _atlasVersion = fontSystem.AtlasVersion;
        }

        private bool IsStale(FontSystem fs) => _atlasVersion != fs.AtlasVersion || _parsed == null;

        // -----------------------------------------------------------------------------------
        // Layout
        // -----------------------------------------------------------------------------------

        private struct CharStyle
        {
            public RichStyleFlags Flags;
            public float PixelSize;   // resolved
            public FontColor Color;
            public string LinkHref;
        }

        private CharStyle ResolveCharStyle(int charIndex)
        {
            var basePx = _settings.PixelSize;
            var cs = new CharStyle {
                Flags = RichStyleFlags.None,
                PixelSize = basePx,
                Color = _settings.DefaultColor,
                LinkHref = null,
            };

            // The parser emits style spans in close order — innermost is closed first, so the
            // list is ordered innermost → outermost. Walk in reverse so outer overrides apply
            // first and inner spans override them last (matches Unity's nesting semantics).
            // Flags use OR so order doesn't affect them.
            var styles = _parsed.Styles;
            for (int i = styles.Count - 1; i >= 0; i--)
            {
                var s = styles[i];
                if (charIndex < s.Start || charIndex >= s.End) continue;
                cs.Flags |= s.Flags;
                if (s.Color.HasValue) cs.Color = s.Color.Value;
                if (!float.IsNaN(s.PixelSize))
                {
                    // Negative value encodes "percent of base".
                    cs.PixelSize = s.PixelSize >= 0 ? s.PixelSize : basePx * (-s.PixelSize) * 0.01f;
                }
                if (s.LinkHref != null) cs.LinkHref = s.LinkHref;
            }
            return cs;
        }

        private FontFile ResolveFont(RichStyleFlags flags)
        {
            bool bold = (flags & RichStyleFlags.Bold) != 0;
            bool italic = (flags & RichStyleFlags.Italic) != 0;
            bool mono = (flags & RichStyleFlags.Mono) != 0;
            if (mono && _settings.MonoFont != null) return _settings.MonoFont;
            if (bold && italic && _settings.BoldItalicFont != null) return _settings.BoldItalicFont;
            if (bold && _settings.BoldFont != null) return _settings.BoldFont;
            if (italic && _settings.ItalicFont != null) return _settings.ItalicFont;
            return _settings.RegularFont;
        }

        private void BuildGlyphs(FontSystem fontSystem)
        {
            _glyphs.Clear();
            _lines.Clear();
            _size = Float2.Zero;

            string text = _parsed.VisibleText;
            int len = text.Length;
            if (len == 0) return;

            bool wrap = _settings.WrapMode == TextWrapMode.Wrap && _settings.MaxWidth > 0f;
            float maxWidth = _settings.MaxWidth;
            float lineHeightMul = _settings.LineHeight;

            // Per-line accumulator
            int lineFirstGlyph = 0;
            int lineCharStart = 0;
            float lineMaxAsc = 0f, lineMaxDesc = 0f;
            float pen = 0f;
            float yTop = 0f;

            // Cross-glyph kerning state — only kern within the same font and same pixel size.
            int prevGlyphIdx = 0;
            FontFile prevFont = null;
            float prevPixelSize = 0f;

            int i = 0;
            while (i < len)
            {
                char ch = text[i];

                if (ch == '\n')
                {
                    FinishLine(ref lineFirstGlyph, lineCharStart, i, ref lineMaxAsc, ref lineMaxDesc,
                               pen, ref yTop, lineHeightMul);
                    lineCharStart = i + 1;
                    pen = 0f;
                    prevGlyphIdx = 0; prevFont = null; prevPixelSize = 0f;
                    i++;
                    continue;
                }

                if (ch == '\t')
                {
                    // Tabs use the active span's size so a <size=64> tab matches that size.
                    float tabAdvance = MeasureSpaceAdvanceAt(fontSystem, i) * Math.Max(1, _settings.TabSize);
                    int stops = (int)(pen / MathF.Max(tabAdvance, 0.0001f)) + 1;
                    pen = stops * tabAdvance;
                    prevGlyphIdx = 0; prevFont = null; prevPixelSize = 0f;
                    i++;
                    continue;
                }

                if (ch == ' ')
                {
                    // Coalesce spaces and sum their widths using each space's active size.
                    int s = i;
                    float run = 0f;
                    while (i < len && text[i] == ' ')
                    {
                        run += MeasureSpaceAdvanceAt(fontSystem, i) + _settings.WordSpacing;
                        i++;
                    }

                    if (wrap && pen + run > maxWidth && _glyphs.Count > lineFirstGlyph)
                    {
                        FinishLine(ref lineFirstGlyph, lineCharStart, s, ref lineMaxAsc, ref lineMaxDesc,
                                   pen, ref yTop, lineHeightMul);
                        lineCharStart = i; // spaces consumed by the wrap
                        pen = 0f;
                    }
                    else pen += run;

                    prevGlyphIdx = 0; prevFont = null; prevPixelSize = 0f;
                    continue;
                }

                // Word [i..wordEnd)
                int wordStart = i;
                int wordEnd = i;
                while (wordEnd < len)
                {
                    char wc = text[wordEnd];
                    if (wc == ' ' || wc == '\t' || wc == '\n') break;
                    wordEnd++;
                }

                if (wrap)
                {
                    // Measure the word's width to decide whether to wrap before placing.
                    float wordWidth = MeasureWordWidth(fontSystem, wordStart, wordEnd);
                    if (pen + wordWidth > maxWidth && _glyphs.Count > lineFirstGlyph)
                    {
                        FinishLine(ref lineFirstGlyph, lineCharStart, wordStart, ref lineMaxAsc, ref lineMaxDesc,
                                   pen, ref yTop, lineHeightMul);
                        lineCharStart = wordStart;
                        pen = 0f;
                        prevGlyphIdx = 0; prevFont = null; prevPixelSize = 0f;
                    }

                    // If the word itself is wider than maxWidth, hard-break inside it.
                    if (wordWidth > maxWidth)
                    {
                        i = LayoutWordChar(fontSystem, wordStart, wordEnd, ref pen, ref lineMaxAsc, ref lineMaxDesc,
                                          ref lineFirstGlyph, ref lineCharStart, ref yTop, lineHeightMul,
                                          maxWidth);
                        prevGlyphIdx = 0; prevFont = null; prevPixelSize = 0f;
                        continue;
                    }
                }

                // Place the entire word as a unit (fits on the current line).
                for (int j = wordStart; j < wordEnd; j++)
                {
                    PlaceGlyph(fontSystem, j, ref pen, ref lineMaxAsc, ref lineMaxDesc,
                               ref prevGlyphIdx, ref prevFont, ref prevPixelSize);
                }
                i = wordEnd;
            }

            // Final line — always emit even if empty so trailing newlines reserve a line.
            FinishLine(ref lineFirstGlyph, lineCharStart, len, ref lineMaxAsc, ref lineMaxDesc,
                       pen, ref yTop, lineHeightMul, forceEmit: true);

            // Compute layout size + apply alignment
            ApplyAlignment();
            float maxLineWidth = 0f;
            foreach (var ln in _lines) if (ln.Width > maxLineWidth) maxLineWidth = ln.Width;
            float totalH = _lines.Count > 0 ? _lines[_lines.Count - 1].Y + _lines[_lines.Count - 1].Height : 0f;
            _size = new Float2(maxLineWidth, totalH);
        }

        // Place a single visible-character glyph onto the current line, updating per-line metrics.
        private void PlaceGlyph(FontSystem fontSystem, int charIndex, ref float pen,
            ref float lineMaxAsc, ref float lineMaxDesc,
            ref int prevGlyphIdx, ref FontFile prevFont, ref float prevPixelSize)
        {
            char c = _parsed.VisibleText[charIndex];
            var cs = ResolveCharStyle(charIndex);
            var font = ResolveFont(cs.Flags);
            if (font == null) return;

            var g = fontSystem.GetOrCreateGlyph(c, cs.PixelSize, font);
            if (g == null) return;

            // Kerning — only meaningful within same font & same pixel size.
            if (prevGlyphIdx != 0 && ReferenceEquals(prevFont, g.Font) && prevPixelSize == cs.PixelSize)
            {
                pen += fontSystem.GetKerningByGlyph(g.Font, prevGlyphIdx, g.GlyphIndex, cs.PixelSize);
            }

            // Per-glyph ascender / descender for line metrics
            fontSystem.GetScaledVMetrics(g.Font, cs.PixelSize, out var asc, out var desc, out var lg);
            float descPositive = -desc; // stb returns negative descent
            if (asc > lineMaxAsc) lineMaxAsc = asc;
            if (descPositive > lineMaxDesc) lineMaxDesc = descPositive;

            float advance = g.Metrics.AdvanceWidth + _settings.LetterSpacing;

            _glyphs.Add(new RichGlyph {
                Glyph = g,
                Position = new Float2(pen + g.Metrics.OffsetX, g.Metrics.OffsetY), // baseline-relative; line baseline added in FinishLine
                PixelSize = cs.PixelSize,
                Advance = advance,
                Character = c,
                CharIndex = charIndex,
                LineIndex = _lines.Count,
                Color = cs.Color,
                Flags = cs.Flags,
                LinkHref = cs.LinkHref,
            });
            pen += advance;

            prevGlyphIdx = g.GlyphIndex;
            prevFont = g.Font;
            prevPixelSize = cs.PixelSize;
        }

        // Char-by-char layout of a long word that exceeds maxWidth, breaking at any glyph.
        private int LayoutWordChar(FontSystem fontSystem, int start, int end,
            ref float pen, ref float lineMaxAsc, ref float lineMaxDesc,
            ref int lineFirstGlyph, ref int lineCharStart, ref float yTop,
            float lineHeightMul, float maxWidth)
        {
            int prevGlyphIdx = 0; FontFile prevFont = null; float prevPixelSize = 0f;
            for (int j = start; j < end; j++)
            {
                char c = _parsed.VisibleText[j];
                var cs = ResolveCharStyle(j);
                var font = ResolveFont(cs.Flags);
                if (font == null) continue;
                var g = fontSystem.GetOrCreateGlyph(c, cs.PixelSize, font);
                if (g == null) continue;

                float adv = g.Metrics.AdvanceWidth + _settings.LetterSpacing;
                float k = 0f;
                if (prevGlyphIdx != 0 && ReferenceEquals(prevFont, g.Font) && prevPixelSize == cs.PixelSize)
                    k = fontSystem.GetKerningByGlyph(g.Font, prevGlyphIdx, g.GlyphIndex, cs.PixelSize);

                if (pen + k + adv > maxWidth && _glyphs.Count > lineFirstGlyph)
                {
                    FinishLine(ref lineFirstGlyph, lineCharStart, j, ref lineMaxAsc, ref lineMaxDesc,
                               pen, ref yTop, lineHeightMul);
                    lineCharStart = j;
                    pen = 0f;
                    prevGlyphIdx = 0; prevFont = null; prevPixelSize = 0f;
                    k = 0f;
                }
                pen += k;

                fontSystem.GetScaledVMetrics(g.Font, cs.PixelSize, out var asc, out var desc, out var _);
                float descPositive = -desc;
                if (asc > lineMaxAsc) lineMaxAsc = asc;
                if (descPositive > lineMaxDesc) lineMaxDesc = descPositive;

                _glyphs.Add(new RichGlyph {
                    Glyph = g,
                    Position = new Float2(pen + g.Metrics.OffsetX, g.Metrics.OffsetY),
                    PixelSize = cs.PixelSize,
                    Advance = adv,
                    Character = c,
                    CharIndex = j,
                    LineIndex = _lines.Count,
                    Color = cs.Color,
                    Flags = cs.Flags,
                    LinkHref = cs.LinkHref,
                });
                pen += adv;

                prevGlyphIdx = g.GlyphIndex;
                prevFont = g.Font;
                prevPixelSize = cs.PixelSize;
            }
            return end;
        }

        private float MeasureWordWidth(FontSystem fontSystem, int start, int end)
        {
            float w = 0f;
            int prevGlyphIdx = 0; FontFile prevFont = null; float prevPixelSize = 0f;
            for (int j = start; j < end; j++)
            {
                char c = _parsed.VisibleText[j];
                var cs = ResolveCharStyle(j);
                var font = ResolveFont(cs.Flags);
                if (font == null) continue;
                var g = fontSystem.GetOrCreateGlyph(c, cs.PixelSize, font);
                if (g == null) continue;

                if (prevGlyphIdx != 0 && ReferenceEquals(prevFont, g.Font) && prevPixelSize == cs.PixelSize)
                    w += fontSystem.GetKerningByGlyph(g.Font, prevGlyphIdx, g.GlyphIndex, cs.PixelSize);

                w += g.Metrics.AdvanceWidth + _settings.LetterSpacing;
                prevGlyphIdx = g.GlyphIndex;
                prevFont = g.Font;
                prevPixelSize = cs.PixelSize;
            }
            return w;
        }

        private float MeasureSpaceAdvanceAt(FontSystem fontSystem, int charIndex)
        {
            // Use the active span's font/size at this char so <size=N> spaces scale too.
            var cs = ResolveCharStyle(charIndex);
            var font = ResolveFont(cs.Flags) ?? _settings.RegularFont;
            if (font == null) return cs.PixelSize * 0.25f;
            var g = fontSystem.GetOrCreateGlyph(' ', cs.PixelSize, font);
            return g?.Metrics.AdvanceWidth ?? cs.PixelSize * 0.25f;
        }

        // Finalize the in-progress line: assign baseline Y to all its glyphs, record the line, and
        // advance yTop. forceEmit emits even when the line has no glyphs (trailing newline).
        private void FinishLine(ref int lineFirstGlyph, int charStart, int charEnd,
            ref float lineMaxAsc, ref float lineMaxDesc, float pen, ref float yTop,
            float lineHeightMul, bool forceEmit = false)
        {
            int glyphCount = _glyphs.Count - lineFirstGlyph;

            // Default line metrics (empty line still occupies a row).
            float asc = lineMaxAsc, desc = lineMaxDesc;
            if (asc == 0f && desc == 0f)
            {
                if (_settings.RegularFont != null)
                {
                    asc = _settings.RegularFont.Ascent * _settings.RegularFont.ScaleForPixelHeight(_settings.PixelSize);
                    desc = -_settings.RegularFont.Descent * _settings.RegularFont.ScaleForPixelHeight(_settings.PixelSize);
                }
                else
                {
                    asc = _settings.PixelSize * 0.8f;
                    desc = _settings.PixelSize * 0.2f;
                }
            }
            if (!forceEmit && glyphCount == 0) { /* still emit so blank lines reserve space */ }

            float baseHeight = asc + desc;
            float lineHeight = baseHeight * lineHeightMul;
            float baselineY = yTop + asc + (lineHeight - baseHeight) * 0.5f;

            // Bake baseline into each glyph's Y position.
            for (int g = lineFirstGlyph; g < _glyphs.Count; g++)
            {
                var rg = _glyphs[g];
                rg.Position = new Float2(rg.Position.X, baselineY + rg.Position.Y);
                _glyphs[g] = rg;
            }

            float lineWidth = pen;

            _lines.Add(new RichLine {
                FirstGlyph = lineFirstGlyph,
                GlyphCount = glyphCount,
                CharStart = charStart,
                CharEnd = charEnd,
                Y = yTop,
                MaxAscent = asc,
                MaxDescent = desc,
                Height = lineHeight,
                Width = lineWidth,
            });

            lineFirstGlyph = _glyphs.Count;
            yTop += lineHeight;
            lineMaxAsc = 0f;
            lineMaxDesc = 0f;
        }

        private void ApplyAlignment()
        {
            if (_settings.Alignment == TextAlignment.Left) return;
            float refWidth = _settings.MaxWidth > 0 ? _settings.MaxWidth : ComputeMaxLineWidth();

            for (int li = 0; li < _lines.Count; li++)
            {
                var ln = _lines[li];
                float offset = _settings.Alignment switch {
                    TextAlignment.Center => (refWidth - ln.Width) * 0.5f,
                    TextAlignment.Right => refWidth - ln.Width,
                    _ => 0f,
                };
                if (offset == 0f) continue;
                int end = ln.FirstGlyph + ln.GlyphCount;
                for (int g = ln.FirstGlyph; g < end; g++)
                {
                    var rg = _glyphs[g];
                    rg.Position = new Float2(rg.Position.X + offset, rg.Position.Y);
                    _glyphs[g] = rg;
                }
            }
        }

        private float ComputeMaxLineWidth()
        {
            float w = 0f;
            for (int i = 0; i < _lines.Count; i++) if (_lines[i].Width > w) w = _lines[i].Width;
            return w;
        }

        // -----------------------------------------------------------------------------------
        // Draw
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Render the layout. <paramref name="currentTime"/> is in seconds; the first draw after
        /// construction or <see cref="Reset"/> anchors animation start to that value.
        /// Re-runs <see cref="Update"/> if the atlas has been rebuilt since the last layout.
        /// </summary>
        public void Draw(FontSystem fontSystem, IFontRenderer renderer, Float2 position, double currentTime)
        {
            if (fontSystem == null) throw new ArgumentNullException(nameof(fontSystem));
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));

            if (IsStale(fontSystem)) Update(fontSystem);
            if (_glyphs.Count == 0) return;

            if (!_startTime.HasValue) _startTime = currentTime;
            float t = (float)(currentTime - _startTime.Value);

            var verts = _drawVertices;
            var indices = _drawIndices;
            verts.Clear();
            indices.Clear();
            int vbase = 0;

            for (int gi = 0; gi < _glyphs.Count; gi++)
            {
                var g = _glyphs[gi];
                var atlas = g.Glyph;
                if (!atlas.IsInAtlas || atlas.AtlasWidth <= 0 || atlas.AtlasHeight <= 0)
                    continue;

                // Apply effects: position offset, color tint, scale, visibility (typewriter).
                var fx = RichTextEffects.Evaluate(g, _parsed.Effects, t, _settings);
                if (!fx.Visible) continue;

                // Quad corners around glyph's atlas drawing rect, with per-glyph scale around the
                // glyph center.
                float w = atlas.Metrics.Width;
                float h = atlas.Metrics.Height;
                float cx = position.X + g.Position.X + w * 0.5f + fx.OffsetX;
                float cy = position.Y + g.Position.Y + h * 0.5f + fx.OffsetY;
                float hw = w * 0.5f * fx.Scale;
                float hh = h * 0.5f * fx.Scale;

                float x0 = cx - hw, y0 = cy - hh;
                float x1 = cx + hw, y1 = cy + hh;

                var c = fx.Color;

                verts.Add(new IFontRenderer.Vertex(new Float3(x0, y0, 0), c, new Float2(atlas.U0, atlas.V0)));
                verts.Add(new IFontRenderer.Vertex(new Float3(x1, y0, 0), c, new Float2(atlas.U1, atlas.V0)));
                verts.Add(new IFontRenderer.Vertex(new Float3(x0, y1, 0), c, new Float2(atlas.U0, atlas.V1)));
                verts.Add(new IFontRenderer.Vertex(new Float3(x1, y1, 0), c, new Float2(atlas.U1, atlas.V1)));

                indices.Add(vbase);
                indices.Add(vbase + 1);
                indices.Add(vbase + 2);
                indices.Add(vbase + 1);
                indices.Add(vbase + 3);
                indices.Add(vbase + 2);
                vbase += 4;
            }

            // Decorations (underline / strike) — solid quads sampled from the atlas's white texel.
            EmitDecorations(verts, indices, ref vbase, position);

            if (verts.Count > 0)
            {
#if NET5_0_OR_GREATER
                renderer.DrawQuads(fontSystem.Texture,
                    CollectionsMarshal.AsSpan(verts),
                    CollectionsMarshal.AsSpan(indices));
#else
                renderer.DrawQuads(fontSystem.Texture, verts.ToArray(), indices.ToArray());
#endif
            }
        }

        private void EmitDecorations(List<IFontRenderer.Vertex> verts, List<int> indices, ref int vbase, Float2 position)
        {
            // Walk lines; within each line, scan glyph runs of contiguous Underline/Strike spans
            // (sharing color) and emit a single quad per run. Decorations use the layout's white
            // texel — which the FontSystem reserves at U=0,V=0 in the atlas (we sample U0/V0 of
            // the first non-empty glyph as a stand-in if needed).
            for (int li = 0; li < _lines.Count; li++)
            {
                var ln = _lines[li];
                EmitDecorationRun(verts, indices, ref vbase, position, ln, RichStyleFlags.Underline);
                EmitDecorationRun(verts, indices, ref vbase, position, ln, RichStyleFlags.Strike);
            }
        }

        private void EmitDecorationRun(List<IFontRenderer.Vertex> verts, List<int> indices, ref int vbase,
            Float2 position, RichLine line, RichStyleFlags which)
        {
            int end = line.FirstGlyph + line.GlyphCount;
            bool inRun = false;
            FontColor runColor = default;
            float runX0 = 0f, runX1 = 0f, runMaxSize = 0f;
            float baselineY = position.Y + line.Y + line.MaxAscent;

            for (int g = line.FirstGlyph; g <= end; g++)
            {
                bool active = false;
                FontColor color = default;
                float size = 0f;
                float gx0 = 0f, gx1 = 0f;

                if (g < end)
                {
                    var rg = _glyphs[g];
                    if ((rg.Flags & which) != 0)
                    {
                        active = true;
                        color = rg.Color;
                        size = rg.PixelSize;
                        gx0 = position.X + rg.Position.X - rg.Glyph.Metrics.OffsetX;
                        gx1 = gx0 + rg.Advance;
                    }
                }

                // Decide whether to extend the current run, start a new one, or close.
                if (active && inRun && ColorEquals(color, runColor))
                {
                    runX1 = gx1;
                    if (size > runMaxSize) runMaxSize = size;
                    continue;
                }

                if (inRun)
                {
                    EmitDecorationQuad(verts, indices, ref vbase, runX0, runX1, runMaxSize, runColor, baselineY, which);
                    inRun = false;
                }

                if (active)
                {
                    inRun = true;
                    runColor = color; runX0 = gx0; runX1 = gx1; runMaxSize = size;
                }
            }
        }

        private static bool ColorEquals(FontColor a, FontColor b)
            => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;

        private static void EmitDecorationQuad(List<IFontRenderer.Vertex> verts, List<int> indices, ref int vbase,
            float x0, float x1, float maxSize, FontColor color, float baselineY, RichStyleFlags which)
        {
            float thickness = MathF.Max(1f, maxSize * 0.06f);
            float y = which == RichStyleFlags.Underline
                ? baselineY + maxSize * 0.12f
                : baselineY - maxSize * 0.30f;

            // Solid quad uses the atlas's white texel at (0,0). Requires FontSystem was created
            // with includeWhiteRect: true (the default).
            var uv = new Float2(0f, 0f);
            verts.Add(new IFontRenderer.Vertex(new Float3(x0, y, 0), color, uv));
            verts.Add(new IFontRenderer.Vertex(new Float3(x1, y, 0), color, uv));
            verts.Add(new IFontRenderer.Vertex(new Float3(x0, y + thickness, 0), color, uv));
            verts.Add(new IFontRenderer.Vertex(new Float3(x1, y + thickness, 0), color, uv));
            indices.Add(vbase); indices.Add(vbase + 1); indices.Add(vbase + 2);
            indices.Add(vbase + 1); indices.Add(vbase + 3); indices.Add(vbase + 2);
            vbase += 4;
        }

        // -----------------------------------------------------------------------------------
        // Hit testing
        // -----------------------------------------------------------------------------------

        /// <summary>Glyph index at a position (relative to the layout origin), or -1.</summary>
        public int HitGlyph(Float2 localPosition)
        {
            for (int li = 0; li < _lines.Count; li++)
            {
                var ln = _lines[li];
                if (localPosition.Y < ln.Y || localPosition.Y > ln.Y + ln.Height) continue;
                int end = ln.FirstGlyph + ln.GlyphCount;
                for (int g = ln.FirstGlyph; g < end; g++)
                {
                    var rg = _glyphs[g];
                    float gx0 = rg.Position.X - rg.Glyph.Metrics.OffsetX;
                    float gx1 = gx0 + rg.Advance;
                    if (localPosition.X >= gx0 && localPosition.X < gx1) return g;
                }
            }
            return -1;
        }

        /// <summary>Returns the link href under the position (relative to layout origin), or null.</summary>
        public string HitLink(Float2 localPosition)
        {
            int g = HitGlyph(localPosition);
            if (g < 0) return null;
            return _glyphs[g].LinkHref;
        }
    }
}
