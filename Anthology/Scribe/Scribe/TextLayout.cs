using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Scribe;

public class TextLayout
{
    public List<Line> Lines { get; private set; }
    public Float2 Size { get; private set; }
    public TextLayoutSettings Settings { get; private set; }
    public string Text { get; private set; } = string.Empty;

    // The font whose metrics set the current line's box, and the largest size placed on it.
    // Glyphs from a fallback font share the primary font's baseline rather than each sitting on
    // their own, which is what keeps mixed-font text on one line straight. A line is not
    // measured until it closes, since a glyph later on it can be taller than anything before it.
    private FontFile? _lineFont;
    private float _lineMaxSize;

    // Last glyph placed on the current line, so the pair it forms with a following space can be
    // kerned. Cleared at every line start, where there is nothing to its left.
    private FontFile? _prevFont;
    private int _prevGlyph;
    private float _prevSize;

    // Pen of the last character that took up width, which is where a combining mark goes.
    private float _basePenX;

    // One resolved style per character, built once per layout and only when there is a
    // customizer to consult. The list is reused, so re-laying out the same text allocates
    // nothing. A surrogate pair's two indices hold the same entry.
    private readonly List<GlyphStyle> _charStyles = [];
    private bool _customized;

    /// <summary>
    /// How far past the wrap width something may reach and still be kept on the line. A browser
    /// lays out on a 1/64 pixel grid, so its advances land a hair either side of ours and a line
    /// can otherwise break a character earlier than the same text in a browser does. Four of
    /// those grid units is enough slack to absorb that and still far too little to see.
    /// </summary>
    private const float FitTolerance = 4f / 64f;

    private static bool Exceeds(float extent, float maxWidth) => extent > maxWidth + FitTolerance;

    // Kerning between two glyphs of the same font. Different fonts have no pair to look up, and
    // glyph 0 is .notdef, which never kerns.
    private static float PairKern(FontSystem fontSystem, FontFile? a, int ga, FontFile? b, int gb, float pixelSize)
    {
        if (a == null || b == null || ga <= 0 || gb <= 0 || !ReferenceEquals(a, b)) return 0f;
        return fontSystem.GetKerningByGlyph(a, ga, gb, pixelSize);
    }

    // The font system this layout was last built against. Hit-testing methods need it to fetch
    // per-glyph metrics (offsets) at draw time, since AtlasGlyph is now size-independent.
    private FontSystem? _fontSystem;

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
    /// UVs), built lazily by <see cref="FontSystem.DrawLayout(TextLayout, Prowl.Vector.Float2, FontColor)"/> and reused across frames. Cleared
    /// whenever the layout is rebuilt (which includes atlas-version changes via
    /// <see cref="EnsureUpToDate"/>), so it never carries stale UVs. Colour and draw position are
    /// applied at emit time, so they are not baked in here.
    /// </summary>
    internal readonly List<DrawQuad> _drawQuads = [];
    internal bool _drawQuadsBuilt;

    internal struct DrawQuad
    {
        public float X0, Y0, X1, Y1; // corner offsets relative to the draw origin
        public float U0, V0, U1, V1; // atlas UVs
        public int CharIndex;        // so a GlyphModifier can map a quad back to the source text
        public float PixelSize;
        public bool Decoration;      // an underline or strike bar; CharIndex is its run's first character
    }

    public TextLayout()
    {
        Lines = [];
    }

    internal void UpdateLayout(string text, TextLayoutSettings settings, FontSystem fontSystem)
    {
        Text = text;
        Settings = settings;
        _fontSystem = fontSystem;
        RecycleLines();
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
    /// <see cref="FontSystem.DrawLayout(TextLayout, Prowl.Vector.Float2, FontColor)"/>.
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

        _open = fontSystem._lineScratch;
        _open.Clear();
        _scaleFont = null;

        Line line = OpenLine(currentY, 0);

        // Hoist Settings & constants
        string text = Text;
        int len = text.Length;

        float pixelSize = Settings.PixelSize;
        BuildCharStyles();

        float spaceWidth = GetSpaceWidth(fontSystem, Settings.Font, pixelSize, Settings.Quality);
        float spaceAdvance = spaceWidth + Settings.WordSpacing;

        // Words are shaped one at a time, so the pairs that straddle a space are not covered by
        // that shaping and have to be kerned here. Without this every space in a line is a
        // fraction of a pixel too wide, and the error accumulates across the line.
        AtlasGlyph? spaceGlyph = fontSystem.GetOrCreateGlyph(' ', Settings.Font!, Settings.Quality);
        FontFile? spaceFont = spaceGlyph?.Font;
        int spaceIndex = spaceGlyph?.GlyphIndex ?? 0;
        bool afterSpace = false;
        float tabWidth = spaceWidth * Settings.TabSize;

        bool wrapEnabled = Settings.WrapMode == TextWrapMode.Wrap && Settings.MaxWidth > 0f;
        float maxWidth = Settings.MaxWidth;

        _basePenX = 0f;
        _spaceSize = 0f; // the memoised space width belongs to the previous font and quality
        StartLine();

        // Reusable shaped-glyph buffer for the current content word (kerning/ligatures folded in).
        List<ShapedGlyph> wordGlyphs = _wordGlyphs ??= [];

        while (i < len)
        {
            int ch = CodepointAt(i);

            // Explicit newline. A carriage return is one too, and a CRLF pair is a single break
            // rather than two, which is what any text that has been near Windows looks like.
            if (ch == '\n' || ch == '\r')
            {
                currentY += FinalizeLine(fontSystem, ref line, currentY, i, currentX);
                currentX = 0f;
                i += ch == '\r' && i + 1 < len && CodepointAt(i + 1) == '\n' ? 2 : 1;
                line = OpenLine(currentY, i);
                StartLine();
                hasTrailingNewline = true;
                continue;
            }

            // Tabs
            if (ch == '\t')
            {
                float tabStop = ((int)(currentX / tabWidth) + 1) * tabWidth;

                // A tab that would advance almost nothing reads as no tab at all, so a stop
                // closer than half a space is passed over for the one after it. Measured out of
                // Chrome, which uses the same half-space rule at every font and size.
                if (tabStop - currentX < spaceWidth * 0.5f) tabStop += tabWidth;

                currentX = tabStop;
                i++;
                continue;
            }

            // Spaces (coalesce runs)
            if (IsBreakingSpace(ch))
            {
                int s = i;
                while (i < len && IsBreakingSpace(CodepointAt(i)))
                {
                    int w = CodepointAt(i);
                    if (w == '\n' || w == '\r' || w == '\t') break;
                    i++;
                }

                float runAdvance = SpaceRunAdvance(fontSystem, s, i, spaceAdvance)
                                 + PairKern(fontSystem, _prevFont, _prevGlyph, spaceFont, spaceIndex, SizeAt(s, pixelSize));
                afterSpace = true;

                if (wrapEnabled && Exceeds(currentX + runAdvance, maxWidth) && _open.Count > 0)
                {
                    // wrap before the run
                    currentY += FinalizeLine(fontSystem, ref line, currentY, s, currentX);
                    currentX = 0f;
                    line = OpenLine(currentY, i);
                    StartLine();
                    afterSpace = false;
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

            fontSystem.ShapeRun(text, wordStart, wordEnd, Settings, _charStyles, wordGlyphs);
            if (wordGlyphs.Count == 0)
            {
                i = wordEnd;
                continue;
            }

            // Characters that occupy no width of their own: the ones that are only there to
            // offer a break, and combining marks, which sit on the character before them. Fonts
            // do not reliably give either a zero advance, so it is taken away here.
            for (int g = 0; g < wordGlyphs.Count; g++)
            {
                ShapedGlyph sg = wordGlyphs[g];
                if (sg.Cluster < 0 || sg.Cluster >= len) continue;

                int c = CodepointAt(sg.Cluster);
                bool invisible = IsInvisible(c);
                if (!invisible && !IsNonSpacing(c)) continue;

                sg.Advance = 0f;
                if (invisible) sg.Glyph = null;
                wordGlyphs[g] = sg;
            }

            // Word width: advance (kerning included) plus letter spacing per cluster.
            float wordWidth = 0f;
            for (int g = 0; g < wordGlyphs.Count; g++)
                wordWidth += wordGlyphs[g].Advance + wordGlyphs[g].LetterSpacing;

            // The pair that straddles the boundary this run starts at, which shaping the run on
            // its own cannot see. Across a space that is the space and the first glyph; where a
            // run was split at a dash the two glyphs simply touch. Dropped if the run wraps,
            // since there is then nothing to its left.
            AtlasGlyph? firstGlyph = wordGlyphs[0].Glyph;
            FontFile? leftFont = afterSpace ? spaceFont : _prevFont;
            int leftGlyph = afterSpace ? spaceIndex : _prevGlyph;
            float leadKern = PairKern(fontSystem, leftFont, leftGlyph,
                                      firstGlyph?.Font, firstGlyph?.GlyphIndex ?? 0, wordGlyphs[0].PixelSize);
            afterSpace = false;

            if (wrapEnabled && Exceeds(currentX + leadKern + wordWidth, maxWidth))
            {
                // Wrap before the word when the line already has content.
                if (_open.Count > 0)
                {
                    // A soft hyphen is invisible right up until the line breaks at it, which is
                    // this moment, and then it is a hyphen like any other.
                    if (wordStart > 0 && CodepointAt(wordStart - 1) == '­')
                        EmitHyphen(fontSystem, ref currentX, SizeAt(wordStart - 1, pixelSize), wordStart - 1);

                    currentY += FinalizeLine(fontSystem, ref line, currentY, wordStart, currentX);
                    currentX = 0f;
                    line = OpenLine(currentY, wordStart);
                    StartLine();
                    leadKern = 0f;
                }

                // A word too wide for a whole line is split at cluster boundaries.
                if (Exceeds(wordWidth, maxWidth))
                {
                    PlaceLongWord(fontSystem, ref line, ref currentX, ref currentY, wordGlyphs, maxWidth);
                    i = wordEnd;
                    continue;
                }
            }

            currentX += leadKern;
            for (int g = 0; g < wordGlyphs.Count; g++)
                EmitShaped(fontSystem, wordGlyphs[g], ref currentX);

            i = wordEnd;
        }

        // Finalize last line
        // Always finalize if: has glyphs, is the first line, or was created by a trailing newline
        if (_open.Count > 0 || Lines.Count == 0 || hasTrailingNewline)
            FinalizeLine(fontSystem, ref line, currentY, i, currentX);

        _open.Clear();
        _open = null!;
    }

    // Glyphs of the line being built. They move into the line's own list, sized to fit, when it closes.
    private List<GlyphInstance> _open = null!;

    // Glyph lists of the previous layout, kept so rebuilding in place allocates nothing.
    private List<List<GlyphInstance>>? _spare;

    private void RecycleLines()
    {
        if (Lines.Count > 0)
        {
            _spare ??= [];
            for (int l = 0; l < Lines.Count; l++)
            {
                List<GlyphInstance> glyphs = Lines[l].Glyphs;
                if (glyphs == null) continue;
                glyphs.Clear();
                _spare.Add(glyphs);
            }
        }

        Lines.Clear();
    }

    private List<GlyphInstance> TakeList(int count)
    {
        if (_spare == null || _spare.Count == 0)
            return [with(count)];

        List<GlyphInstance> list = _spare[_spare.Count - 1];
        _spare.RemoveAt(_spare.Count - 1);
        if (list.Capacity < count) list.Capacity = count;
        return list;
    }

    private static Line OpenLine(float y, int startIndex)
    {
        Line line = default;
        line.Position = new Float2(0, y);
        line.StartIndex = startIndex;
        line.EndIndex = startIndex;
        return line;
    }

    // Pixel scale of the font and size the last glyph was placed with, which rarely changes.
    private FontFile? _scaleFont;
    private float _scaleSize;
    private float _scale;

    private float ScaleFor(FontSystem fontSystem, FontFile font, float pixelSize)
    {
        if (!ReferenceEquals(font, _scaleFont) || pixelSize != _scaleSize)
        {
            _scaleFont = font;
            _scaleSize = pixelSize;
            _scale = fontSystem.GetScale(font, Settings.Font, pixelSize);
        }

        return _scale;
    }

    // Reusable shaped-glyph buffer for the current content word.
    private List<ShapedGlyph>? _wordGlyphs;

    // Places an already-shaped word that is too wide for one line, breaking at cluster boundaries
    // (never inside a ligature).
    private void PlaceLongWord(FontSystem fontSystem, ref Line line, ref float currentX,
        ref float currentY, List<ShapedGlyph> glyphs, float maxWidth)
    {
        for (int g = 0; g < glyphs.Count; g++)
        {
            ShapedGlyph sg = glyphs[g];
            float adv = sg.Advance + sg.LetterSpacing;

            if (_open.Count > 0 && Exceeds(currentX + adv, maxWidth))
            {
                int clusterIndex = sg.Cluster;
                currentY += FinalizeLine(fontSystem, ref line, currentY, clusterIndex, currentX);
                currentX = 0f;
                line = OpenLine(currentY, clusterIndex);
                StartLine();
            }

            EmitShaped(fontSystem, sg, ref currentX);
        }
    }

    // Draws the hyphen a soft hyphen turns into once a line breaks at it.
    private void EmitHyphen(FontSystem fontSystem, ref float currentX, float pixelSize, int charIndex)
    {
        AtlasGlyph? glyph = fontSystem.GetOrCreateGlyph('-', Settings.Font!, Settings.Quality);
        if (glyph == null) return;

        GlyphMetrics gm = fontSystem.GetGlyphMetricsByIndex(glyph.Font, glyph.GlyphIndex, pixelSize, Settings.Font) ?? default;
        NoteLineGlyph(glyph.Font, pixelSize);
        _open.Add(new GlyphInstance(glyph, new Float2(currentX + gm.OffsetX, gm.OffsetY),
                                          '-', gm.AdvanceWidth, pixelSize, charIndex, 1)
        {
            Decoration = DecorationAt(charIndex)
        });
        currentX += gm.AdvanceWidth;
    }

    // Emits one shaped glyph at the current pen X, advancing the pen.
    private void EmitShaped(FontSystem fontSystem, in ShapedGlyph sg, ref float currentX)
    {
        float advance = sg.Advance + sg.LetterSpacing;
        AtlasGlyph? atlas = sg.Glyph;
        if (atlas == null)
        {
            currentX += advance;
            return;
        }

        float pixelSize = sg.PixelSize;
        atlas.GetOffset(ScaleFor(fontSystem, atlas.Font, pixelSize), out float offsetX, out float offsetY);
        NoteLineGlyph(atlas.Font, pixelSize);
        int ch = sg.Cluster >= 0 && sg.Cluster < Text.Length ? CodepointAt(sg.Cluster) : '\0';

        // A combining mark belongs over the character it follows. The pen has already moved past
        // that character, so drawing at the pen would land the accent on the next letter along.
        bool nonSpacing = IsNonSpacing(ch);
        float penX = nonSpacing ? _basePenX : currentX;

        _open.Add(new GlyphInstance(
            atlas,
            new Float2(penX + offsetX, offsetY),
            ch <= 0xFFFF ? (char)ch : '\0', advance, pixelSize, sg.Cluster, sg.CharCount)
        {
            // Looked up after shaping rather than splitting runs on it, so a<u>b</u> still kerns.
            Decoration = DecorationAt(sg.Cluster)
        });

        if (!nonSpacing)
        {
            _basePenX = currentX;
            _prevFont = atlas.Font;
            _prevGlyph = atlas.GlyphIndex;
            _prevSize = pixelSize;
        }

        currentX += advance;
    }


    // Pen-origin X of a placed glyph. Position.X is pen + glyph offset, so subtract the offset
    // (fetched per-instance now that AtlasGlyph is size-independent) to recover the pen origin.
    private float GlyphOffsetX(GlyphInstance gi)
    {
        if (_fontSystem == null || gi.Glyph == null) return 0f;
        GlyphMetrics? gm = _fontSystem.GetGlyphMetricsByIndex(gi.Glyph.Font, gi.Glyph.GlyphIndex, gi.PixelSize, Settings.Font);
        return gm?.OffsetX ?? 0f;
    }

    // Natural line height of the primary font at the current size (ascent + |descent| + lineGap).
    // Falls back to the pixel size when there is no font.
    private float GetSpaceWidth(FontSystem fontSystem, FontFile? font, float pixelSize, FontQuality quality)
    {
        if (font == null) return pixelSize * 0.25f;
        AtlasGlyph? spaceGlyph = fontSystem.GetOrCreateGlyph(' ', font, quality);
        if (spaceGlyph == null) return pixelSize * 0.25f;
        GlyphMetrics? gm = fontSystem.GetGlyphMetricsByIndex(spaceGlyph.Font, spaceGlyph.GlyphIndex, pixelSize, Settings.Font);
        return gm?.AdvanceWidth ?? pixelSize * 0.25f;
    }

    /// <summary>The codepoint laid out at a character index, which a customizer may have changed.</summary>
    private int CodepointAt(int index) => _customized ? _charStyles[index].Codepoint : Text[index];

    private float SizeAt(int index, float fallback) => _customized ? _charStyles[index].PixelSize : fallback;

    private TextDecoration DecorationAt(int index)
    {
        bool underline, strike;
        if (_customized && index >= 0 && index < _charStyles.Count)
        {
            underline = _charStyles[index].Underline;
            strike = _charStyles[index].Strikethrough;
        }
        else
        {
            underline = Settings.Underline;
            strike = Settings.Strikethrough;
        }

        return (underline ? TextDecoration.Underline : TextDecoration.None)
             | (strike ? TextDecoration.Strikethrough : TextDecoration.None);
    }

    // Resolves every character up front, so the customizer is asked once each and the rest of
    // layout simply reads what it decided.
    private void BuildCharStyles()
    {
        _charStyles.Clear();
        _customized = Settings.Customizer != null;
        if (!_customized) return;

        string text = Text;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            int codepoint = c;
            int charCount = 1;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(c, text[i + 1]);
                charCount = 2;
            }

            GlyphStyle style = Settings.StyleFor(i, codepoint);
            for (int k = 0; k < charCount; k++) _charStyles.Add(style);
            i += charCount;
        }
    }

    // Width of a run of spaces. Uniform text multiplies one width by the count; only a customizer
    // makes it worth measuring them one at a time.
    private float SpaceRunAdvance(FontSystem fontSystem, int start, int end, float spaceAdvance)
    {
        if (!_customized) return spaceAdvance * (end - start);

        float total = 0f;
        for (int k = start; k < end; k++)
        {
            GlyphStyle style = _charStyles[k];
            if (style.PixelSize != _spaceSize || !ReferenceEquals(style.Font, _spaceFont)
                || style.Quality != _spaceQuality)
            {
                _spaceSize = style.PixelSize;
                _spaceFont = style.Font;
                _spaceQuality = style.Quality;
                _spaceWidth = GetSpaceWidth(fontSystem, style.Font, style.PixelSize, style.Quality);
            }

            total += _spaceWidth + style.WordSpacing;
        }

        return total;
    }

    private float _spaceSize;
    private float _spaceWidth;
    private FontFile? _spaceFont;
    private FontQuality _spaceQuality;

    // Opens a line. Nothing to its left, and no glyph on it yet to set its box.
    private void StartLine()
    {
        _prevFont = null;
        _prevGlyph = 0;
        _prevSize = 0f;
        _lineFont = Settings.Font;
        // With a customizer the line box comes from what is actually on the line, so text that
        // is all small gets a small line rather than one sized for the base.
        _lineMaxSize = _customized ? 0f : Settings.PixelSize;
    }

    // Records a glyph against the line box. The tallest glyph wins, and with no primary font the
    // first one placed decides whose metrics the line is measured with.
    private void NoteLineGlyph(FontFile font, float pixelSize)
    {
        _lineFont ??= font;
        if (pixelSize > _lineMaxSize) _lineMaxSize = pixelSize;
    }

    // Height and baseline of the line about to be closed. Anything a line is given beyond the
    // font's own ascent-to-descent box is leading, and half of it belongs above the text, so a
    // raised LineHeight centres the line rather than pushing it down.
    private void LineMetrics(FontSystem fontSystem, out float height, out float baseline)
    {
        float size = _lineMaxSize > 0f ? _lineMaxSize : Settings.PixelSize;
        if (_lineFont == null)
        {
            height = size * Settings.LineHeight;
            baseline = 0f;
            return;
        }

        fontSystem.GetScaledVMetrics(_lineFont, size, out float asc, out float desc, out float gap);
        float natural = asc - desc + gap; // descent is negative
        height = (natural > 0f ? natural : size) * Settings.LineHeight;
        baseline = (height - (asc - desc)) * 0.5f + asc;
    }

    /// <summary>
    /// Whether a space is one a line may break at. A no-break space is a space to look at and a
    /// letter to lay out, so it belongs inside the word rather than between words.
    /// </summary>
    private static bool IsBreakingSpace(int c)
        => c <= 0xFFFF && char.IsWhiteSpace((char)c) && c != ' ' && c != ' ' && c != ' ';

    /// <summary>Characters that take up no room and are only there to offer a break.</summary>
    private static bool IsInvisible(int c) => c == '​' || c == '­';

    /// <summary>
    /// A combining mark belongs on top of the character before it, so it takes no width of its
    /// own. Some fonts, monospace ones especially, still give their mark glyphs a full advance,
    /// which would push the rest of the line along by a cell per accent.
    /// </summary>
    private static bool IsNonSpacing(int c)
        => c >= 0x300 && c <= 0xFFFF
           && System.Globalization.CharUnicodeInfo.GetUnicodeCategory((char)c)
              == System.Globalization.UnicodeCategory.NonSpacingMark;

    /// <summary>A line may break straight after these, with no space involved.</summary>
    private static bool BreaksAfter(int c)
        => c == '-' || c == '‐' || c == '‒' || c == '–' || c == '—'
           || c == '​' || c == '­';

    /// <summary>A line may break just before these.</summary>
    private static bool BreaksBefore(int c) => c == '—';

    // A run is the most a line can hold without a break inside it: up to the next space, the next
    // line break, or the next dash-like character a line is allowed to break around.
    private int FindWordEnd(int startIndex)
    {
        int index = startIndex;
        while (index < Text.Length)
        {
            int c = CodepointAt(index);
            if (IsBreakingSpace(c) || c == '\n' || c == '\r') break;
            if (index > startIndex && BreaksBefore(c)) break;

            index++;
            if (BreaksAfter(c)) break;
        }
        return index;
    }

    private float FinalizeLine(FontSystem fontSystem, ref Line line, float y, int endIndex, float currentX)
    {
        LineMetrics(fontSystem, out float lineHeight, out float baseline);

        // Glyphs are placed relative to the baseline, which is not known until the line closes
        // and the tallest glyph on it has been seen.
        List<GlyphInstance> open = _open;
        List<GlyphInstance> glyphs = TakeList(open.Count);
        for (int g = 0; g < open.Count; g++)
        {
            GlyphInstance gi = open[g];
            gi.Position.Y += baseline;
            glyphs.Add(gi);
        }
        open.Clear();
        line.Glyphs = glyphs;

        line.Position = new Float2(0, y);
        line.Height = lineHeight;
        // The pen has already advanced past every glyph and space on the line, so it is the
        // line's width. A glyph's Position.X carries its left side bearing, which is ink rather
        // than advance and would over-report the width by that bearing.
        line.Width = currentX;
        line.EndIndex = endIndex;
        Lines.Add(line);
        return lineHeight;
    }

    private void ApplyAlignment()
    {
        if (Settings.Alignment == TextAlignment.Left) return;

        float maxWidth = Settings.MaxWidth > 0 ? Settings.MaxWidth : GetMaxLineWidth();

        foreach (Line line in Lines)
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
                GlyphInstance glyph = line.Glyphs[i];
                    glyph.Position = new Float2(glyph.Position.X + offset, glyph.Position.Y);
                    line.Glyphs[i] = glyph;
                }
            //}
        }
    }

    private float GetMaxLineWidth()
    {
        float maxWidth = 0;
        foreach (Line line in Lines)
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

        foreach (Line line in Lines)
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

        foreach (Line line in Lines)
        {
            if (index < line.StartIndex)
                return new Float2(0, line.Position.Y);

            if (index <= line.EndIndex)
            {
                float currentX = 0f;
                int currentIndex = line.StartIndex;

                foreach (GlyphInstance glyph in line.Glyphs)
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

        Line last = Lines[^1];
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
            Line l = Lines[li];
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

        foreach (GlyphInstance glyph in line.Glyphs)
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

        Line line = GetLineForIndex(index);
        Float2 start = GetCursorPosition(index);
        Float2 end = GetCursorPosition(index + 1);
        return new RectangleF(start.X, line.Position.Y, end.X - start.X, line.Height);
    }
}
