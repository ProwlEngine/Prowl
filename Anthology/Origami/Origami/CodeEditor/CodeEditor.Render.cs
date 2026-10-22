// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;

using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.OrigamiUI;

public sealed partial class CodeEditor
{
    // Everything measured during the last draw, in logical units relative to the screen.
    internal struct ViewLayout
    {
        public float X, Y, W, H;
        public float GlyphX, GlyphW, NumbersX, NumbersW, FoldX, FoldW, GutterW;
        public float TextX, TextY, TextW, TextH;
        public float MinimapX, MinimapW;
        public float VBarX, VBarW;
        public float HBarY, HBarH;
        public float StatusY, StatusH;
        public float LineH, CharW, GlyphH;
        public bool Valid;
    }

    internal ViewLayout _view;

    private double _scrollY, _scrollYTarget;
    private float _scrollX, _scrollXTarget;
    private bool _revealCaret;
    private bool _centerCaret, _centerOnlyIfOffscreen;
    private double _caretMoveTime;

    private FontFile? _measuredFont;
    private float _measuredSize;
    private float _measuredScale;
    private float _charW = 8f;
    private float _glyphH = 14f;
    private float _fontSize = -1f;

    // Rows drawn last frame keyed by line string, swapped each frame so rows that scrolled away are dropped.
    private Dictionary<string, RowRender> _rows = new(ReferenceEqualityComparer.Instance);
    private Dictionary<string, RowRender> _rowsNext = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, TextLayout> _numberLayouts = new();
    private readonly Dictionary<(float, string), TextLayout> _miscLayouts = new();
    private readonly Dictionary<string, MinimapLine> _minimap = new(ReferenceEqualityComparer.Instance);
    private readonly List<CodeToken> _tokens = new();
    private readonly List<(int Start, int Length)> _matches = new();
    private CodeEditorColors? _themeColors;
    private OrigamiTheme? _themeColorsFor;


    // A row as words: each run is one word of one colour at a screen column, drawn with a layout shared by
    // every run with the same text. Editing a line only lays out the word that changed.
    private sealed class RowRender
    {
        public int State;
        public CodeToken[]? Semantic;
        public CodeEditorColors? Palette;
        public float FontKey;
        public int TabSize;
        public FontFile? Font;
        public bool Ligatures;
        public int BracketDepth;
        public bool Built;
        public int TokenState = -1;
        public CodeToken[] Tokens = Array.Empty<CodeToken>();

        // Some words were drawn glyph by glyph because laying them out would have cost too much this frame.
        public bool Pending;
        public Run[] Runs = Array.Empty<Run>();
    }

    private struct Run
    {
        public int Column;
        public string Text;
        public TextLayout? Layout;
        public FontColor Color;
    }

    private sealed class MinimapLine
    {
        public int State;
        public CodeToken[] Tokens = Array.Empty<CodeToken>();
    }

    public float FontSize => _fontSize > 0 ? _fontSize : Options.FontSize;

    /// <summary>Changes the font size, as Ctrl+wheel does.</summary>
    public void Zoom(float delta)
    {
        _fontSize = Math.Clamp(FontSize + delta, Options.MinFontSize, Options.MaxFontSize);
        _revealCaret = true;
    }

    public void ResetZoom() => _fontSize = Options.FontSize;

    internal float LineHeightPx => _view.Valid ? _view.LineH : MathF.Round(FontSize * Options.LineHeight);

    internal int VisibleRowCapacity => _view.Valid ? Math.Max(1, (int)(_view.TextH / _view.LineH)) : 20;

    public void ScrollBy(double pixels)
    {
        _scrollYTarget = Math.Clamp(_scrollYTarget + pixels, 0, MaxScrollY());
        _scrollY = _scrollYTarget;
    }

    /// <summary>Scrolls so <paramref name="line"/> is the top visible row.</summary>
    public void ScrollToLine(int line)
    {
        _scrollYTarget = Math.Clamp(LineToDisplayRow(Math.Clamp(line, 0, Document.LineCount - 1)) * (double)LineHeightPx, 0, MaxScrollY());
        _scrollY = _scrollYTarget;
    }

    public int FirstVisibleLine => DisplayRowToLine((int)(_scrollY / LineHeightPx));

    public void CenterOnCaret(bool onlyIfOffscreen = false)
    {
        _centerCaret = true;
        _centerOnlyIfOffscreen = onlyIfOffscreen;
    }

    private double MaxScrollY()
    {
        float lh = LineHeightPx;
        double content = DisplayRowCount * (double)lh;
        double view = _view.Valid ? _view.TextH : 0;
        if (Options.ScrollBeyondLastLine) return Math.Max(0, content - lh);
        return Math.Max(0, content - view);
    }

    private float MaxScrollX()
    {
        if (!_view.Valid) return 0;
        float content = (Document.LongestLineLength + 4) * _view.CharW;
        return MathF.Max(0, content - _view.TextW);
    }

    private CodeEditorColors Palette()
    {
        if (Colors != null) return Colors;
        var theme = Origami.Current;
        if (_themeColors == null || !ReferenceEquals(theme, _themeColorsFor))
        {
            _themeColors = CodeEditorColors.FromTheme(theme);
            _themeColorsFor = theme;
        }
        return _themeColors;
    }

    private FontFile? EditorFont => Options.Font ?? Origami.Current.Mono;

    #region Layout

    private TextLayoutSettings TextSettings(Canvas canvas, FontFile font, float size)
    {
        var s = TextLayoutSettings.Default;
        s.Font = font;
        s.PixelSize = size * canvas.FramebufferScale;
        s.Quality = FontQuality.High;
        s.TabSize = Math.Max(1, Options.TabSize);
        s.NoLigatures = !Options.Ligatures;
        return s;
    }

    private TextLayout CreateLayout(Canvas canvas, string text, FontFile font, float size)
    {
        var layout = new TextLayout();
        canvas.Text.FontEngine.UpdateLayout(layout, text, TextSettings(canvas, font, size));
        return layout;
    }

    private void Measure(Canvas canvas, FontFile font)
    {
        float size = FontSize;
        float scale = canvas.FramebufferScale;
        if (ReferenceEquals(font, _measuredFont) && size == _measuredSize && scale == _measuredScale) return;
        _measuredFont = font;
        _measuredSize = size;
        _measuredScale = scale;

        var probe = CreateLayout(canvas, "MMMMMMMMMMMMMMMMMMMM", font, size);
        _charW = probe.Size.X / 20f / scale;
        _glyphH = probe.Lines.Count > 0 ? probe.Lines[0].Height / scale : size * 1.2f;
        _rows.Clear();
        _layoutCache.Clear();
        _glyphLayouts.Clear();
        _sizedGlyphs.Clear();
        _numberLayouts.Clear();
        _miscLayouts.Clear();
    }

    private void ComputeLayout(Rect rect)
    {
        var v = new ViewLayout { Valid = true };
        v.X = rect.Min.X;
        v.Y = rect.Min.Y;
        v.W = rect.Size.X;
        v.H = rect.Size.Y;
        v.LineH = MathF.Max(1, MathF.Round(FontSize * Options.LineHeight));
        v.CharW = _charW;
        v.GlyphH = _glyphH;

        v.StatusH = Options.ShowStatusBar ? MathF.Round(MathF.Max(20, FontSize * 1.6f)) : 0;
        v.StatusY = v.Y + v.H - v.StatusH;

        float x = v.X;
        v.GlyphX = x;
        v.GlyphW = Options.ShowGlyphMargin ? MathF.Round(v.LineH * 0.9f) : 4;
        x += v.GlyphW;

        int digits = Math.Max(3, (int)Math.Floor(Math.Log10(Math.Max(1, Document.LineCount))) + 1);
        v.NumbersX = x;
        v.NumbersW = Options.ShowLineNumbers ? digits * v.CharW + v.CharW : 0;
        x += v.NumbersW;

        v.FoldX = x;
        v.FoldW = Options.ShowFolding ? MathF.Round(v.LineH * 0.9f) : 0;
        x += v.FoldW;
        v.GutterW = x - v.X;

        v.VBarW = 12;
        v.VBarX = v.X + v.W - v.VBarW;
        v.MinimapW = Options.ShowMinimap && v.W > 420 ? Options.MinimapWidth : 0;
        v.MinimapX = v.VBarX - v.MinimapW;

        v.TextX = x + 6;
        v.TextY = v.Y;
        v.TextW = MathF.Max(0, v.MinimapX - v.TextX);
        v.HBarH = 10;
        v.TextH = MathF.Max(0, v.StatusY - v.Y);
        v.HBarY = v.StatusY - v.HBarH;
        _view = v;
    }

    private void UpdateScroll()
    {
        var v = _view;
        float lh = v.LineH;

        if (_centerCaret)
        {
            _centerCaret = false;
            double caretY = LineToDisplayRow(Caret.Line) * (double)lh;
            bool offscreen = caretY < _scrollYTarget || caretY + lh > _scrollYTarget + v.TextH;
            if (!_centerOnlyIfOffscreen || offscreen)
                _scrollYTarget = _scrollY = Math.Clamp(caretY - v.TextH * 0.4, 0, MaxScrollY());
            _revealCaret = true;
        }

        if (_revealCaret)
        {
            _revealCaret = false;
            double caretY = LineToDisplayRow(Caret.Line) * (double)lh;
            double margin = Math.Min(Options.ScrollMargin * lh, Math.Max(0, (v.TextH - lh) / 2));
            if (caretY - margin < _scrollYTarget) _scrollYTarget = caretY - margin;
            else if (caretY + lh + margin > _scrollYTarget + v.TextH) _scrollYTarget = caretY + lh + margin - v.TextH;
            _scrollYTarget = Math.Clamp(_scrollYTarget, 0, MaxScrollY());
            _scrollY = _scrollYTarget;

            float caretX = VisualColumn(Caret.Line, Caret.Column) * v.CharW;
            float marginX = v.CharW * 4;
            if (caretX - marginX < _scrollXTarget) _scrollXTarget = MathF.Max(0, caretX - marginX);
            else if (caretX + marginX > _scrollXTarget + v.TextW) _scrollXTarget = caretX + marginX - v.TextW;
            _scrollX = _scrollXTarget;
        }

        _scrollYTarget = Math.Clamp(_scrollYTarget, 0, MaxScrollY());
        _scrollXTarget = Math.Clamp(_scrollXTarget, 0, MaxScrollX());

        if (Options.SmoothScrolling)
        {
            float k = 1f - MathF.Exp(-(float)_frameDelta * 18f);
            _scrollY += (_scrollYTarget - _scrollY) * k;
            _scrollX += (_scrollXTarget - _scrollX) * k;
            if (Math.Abs(_scrollYTarget - _scrollY) < 0.5) _scrollY = _scrollYTarget;
            if (MathF.Abs(_scrollXTarget - _scrollX) < 0.5f) _scrollX = _scrollXTarget;
        }
        else
        {
            _scrollY = _scrollYTarget;
            _scrollX = _scrollXTarget;
        }
    }

    #endregion

    #region Coordinates

    /// <summary>Document position under a screen point, or null outside the text.</summary>
    internal TextPosition PositionFromPoint(Float2 point, bool round = true)
    {
        var v = _view;
        double y = point.Y - v.TextY + _scrollY;
        int row = (int)Math.Floor(y / v.LineH);
        if (row < 0) return TextPosition.Zero;
        if (row >= DisplayRowCount) return Document.EndPosition;
        int line = DisplayRowToLine(row);
        float x = point.X - v.TextX + _scrollX;
        float visual = x / v.CharW;
        int col = round ? ColumnFromVisual(line, (int)MathF.Round(visual), false) : ColumnFromVisual(line, (int)MathF.Floor(visual));
        return new TextPosition(line, col);
    }

    internal Float2 PointFromPosition(TextPosition p)
    {
        var v = _view;
        float x = v.TextX - _scrollX + VisualColumn(p.Line, p.Column) * v.CharW;
        float y = (float)(v.TextY + LineToDisplayRow(p.Line) * (double)v.LineH - _scrollY);
        return new Float2(x, y);
    }

    #endregion

    #region Drawing

    private void DrawEditor(Canvas canvas, Rect rect)
    {
        if (_paper != null) _pointer = _paper.PointerPosIn(_handle);
        var font = EditorFont;
        var pal = Palette();
        canvas.RectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, pal.Background);
        if (font == null) return;

        _layoutBudget = _time - _lastEditTime < 0.3 ? LayoutBudgetMs : LayoutBudgetMs * 4;
        Measure(canvas, font);
        ComputeLayout(rect);
        UpdateScroll();
        var v = _view;

        int firstRow = Math.Max(0, (int)Math.Floor(_scrollY / v.LineH));
        int rowCount = (int)Math.Ceiling(v.TextH / v.LineH) + 1;
        int lastRow = Math.Min(DisplayRowCount - 1, firstRow + rowCount);
        int firstLine = DisplayRowToLine(firstRow);
        int lastLine = DisplayRowToLine(Math.Max(firstRow, lastRow));
        GetLineState(Math.Min(lastLine + 1, Document.LineCount - 1));
        TickSemantic(firstLine, lastLine);
        TickCodeLens(firstLine, lastLine);
        _lensHits.Clear();

        canvas.SaveState();
        canvas.IntersectScissor(v.X, v.Y, v.W, v.TextH);

        DrawLineBackgrounds(canvas, pal, firstRow, lastRow);

        canvas.SaveState();
        canvas.IntersectScissor(v.GutterW + v.X, v.Y, v.TextW + (v.TextX - v.X - v.GutterW), v.TextH);
        DrawRulers(canvas, pal);
        DrawHighlights(canvas, pal, firstRow, lastRow);
        DrawSelections(canvas, pal, firstRow, lastRow);
        if (Options.ShowIndentGuides) DrawIndentGuides(canvas, pal, firstRow, lastRow);
        if (Options.ShowTrailingWhitespace) DrawTrailingWhitespace(canvas, pal, firstRow, lastRow);
        DrawText(canvas, pal, font, firstRow, lastRow);
        DrawSquiggles(canvas, pal, firstLine, lastLine);
        DrawCarets(canvas, pal, firstRow, lastRow);
        canvas.RestoreState();

        DrawGutter(canvas, pal, font, firstRow, lastRow);
        if (v.MinimapW > 0) DrawMinimap(canvas, pal, firstRow, lastRow);
        DrawScrollbars(canvas, pal);
        canvas.RestoreState();

        if (v.StatusH > 0) DrawStatusBar(canvas, pal, font);

        (_rows, _rowsNext) = (_rowsNext, _rows);
        _rowsNext.Clear();
    }

    private float RowY(int row) => (float)(_view.TextY + row * (double)_view.LineH - _scrollY);

    private float ColX(int visualCol) => _view.TextX - _scrollX + visualCol * _view.CharW;

    private void DrawLineBackgrounds(Canvas canvas, CodeEditorColors pal, int firstRow, int lastRow)
    {
        var v = _view;
        if (Options.HighlightCurrentLine && IsFocused)
        {
            var seen = new HashSet<int>();
            foreach (var s in _selections)
            {
                if (!s.IsEmpty || !seen.Add(s.Caret.Line) || IsLineHidden(s.Caret.Line)) continue;
                int row = LineToDisplayRow(s.Caret.Line);
                if (row < firstRow || row > lastRow) continue;
                canvas.RectFilled(v.X, RowY(row), v.MinimapX - v.X, v.LineH, pal.CurrentLine);
            }
        }

        foreach (var m in _markers)
        {
            if (m.Kind != CodeLineMarkerKind.Execution && m.Kind != CodeLineMarkerKind.LineHighlight) continue;
            if (m.Line < 0 || m.Line >= Document.LineCount || IsLineHidden(m.Line)) continue;
            int row = LineToDisplayRow(m.Line);
            if (row < firstRow || row > lastRow) continue;
            var c = m.Color ?? (m.Kind == CodeLineMarkerKind.Execution ? new Color32(255, 214, 102, 40) : new Color32(255, 255, 255, 18));
            canvas.RectFilled(v.TextX - 4, RowY(row), v.MinimapX - v.TextX + 4, v.LineH, c);
        }
    }

    private void DrawRulers(Canvas canvas, CodeEditorColors pal)
    {
        foreach (int col in Options.Rulers)
            canvas.RectFilled(MathF.Round(ColX(col)), _view.Y, 1, _view.TextH, pal.Ruler);
    }

    private void DrawHighlights(Canvas canvas, CodeEditorColors pal, int firstRow, int lastRow)
    {
        var v = _view;

        if (_findBarOpen && FindQuery.Length > 0)
        {
            var sel = Selection.Range;
            for (int row = firstRow; row <= lastRow; row++)
            {
                int line = DisplayRowToLine(row, out bool lensRow);
                if (lensRow) continue;
                LineMatches(line, _matches);
                foreach (var (start, len) in _matches)
                {
                    float x0 = ColX(VisualColumn(line, start)), x1 = ColX(VisualColumn(line, start + len));
                    bool current = sel.Start.Line == line && sel.Start.Column == start && sel.End.Column == start + len;
                    canvas.RoundedRectFilled(x0, RowY(row) + 1, x1 - x0, v.LineH - 2, 2, current ? pal.FindMatchCurrent : pal.FindMatch);
                }
            }
        }

        foreach (var h in _highlights)
        {
            if (h.Start.Line != h.End.Line) continue;
            int line = h.Start.Line;
            if (line >= Document.LineCount || IsLineHidden(line)) continue;
            int row = LineToDisplayRow(line);
            if (row < firstRow || row > lastRow) continue;
            float x0 = ColX(VisualColumn(line, h.Start.Column)), x1 = ColX(VisualColumn(line, h.End.Column));
            canvas.RoundedRectFilled(x0, RowY(row) + 1, x1 - x0, v.LineH - 2, 2, pal.OccurrenceHighlight);
        }

        if (Options.HighlightMatchingBrackets && IsFocused && TryGetBracketPair(out var open, out var close))
        {
            foreach (var p in new[] { open, close })
            {
                if (IsLineHidden(p.Line)) continue;
                int row = LineToDisplayRow(p.Line);
                if (row < firstRow || row > lastRow) continue;
                float x0 = ColX(VisualColumn(p.Line, p.Column));
                canvas.RoundedRectFilled(x0, RowY(row) + 1, v.CharW, v.LineH - 2, 2, pal.BracketMatch);
            }
        }
    }

    private TextPosition _bracketFor = new(-1, -1);
    private int _bracketVersion = -1;
    private TextPosition _bracketOpen, _bracketClose;
    private bool _bracketFound;

    private bool TryGetBracketPair(out TextPosition open, out TextPosition close)
    {
        if (_bracketFor != Caret || _bracketVersion != Document.Version)
        {
            _bracketFor = Caret;
            _bracketVersion = Document.Version;
            _bracketFound = TryFindMatchingBracket(Caret, out _bracketOpen, out _bracketClose);
        }
        open = _bracketOpen;
        close = _bracketClose;
        return _bracketFound;
    }

    private void DrawSelections(Canvas canvas, CodeEditorColors pal, int firstRow, int lastRow)
    {
        var v = _view;
        var color = IsFocused ? pal.Selection : pal.SelectionInactive;
        foreach (var s in _selections)
        {
            if (s.IsEmpty) continue;
            int startRow = Math.Max(firstRow, LineToDisplayRow(s.Start.Line));
            int endRow = Math.Min(lastRow, LineToDisplayRow(s.End.Line));
            for (int row = startRow; row <= endRow; row++)
            {
                int line = DisplayRowToLine(row, out bool lensRow);
                if (lensRow) continue;
                int c0 = line == s.Start.Line ? VisualColumn(line, s.Start.Column) : 0;
                int c1 = line == s.End.Line ? VisualColumn(line, s.End.Column) : VisualColumn(line, Document.GetLineLength(line));
                float x0 = ColX(c0);
                float x1 = ColX(c1) + (line != s.End.Line ? v.CharW * 0.6f : 0);
                if (x1 <= x0) continue;
                canvas.RectFilled(x0, RowY(row), x1 - x0, v.LineH, color);
            }
        }
    }

    private void DrawIndentGuides(Canvas canvas, CodeEditorColors pal, int firstRow, int lastRow)
    {
        var v = _view;
        int tab = Math.Max(1, Options.TabSize);
        int prevIndent = 0;
        for (int row = firstRow; row <= lastRow; row++)
        {
            int line = DisplayRowToLine(row, out bool lensRow);
            string s = Document[line];
            int indent;
            // A lens row continues the guides of the declaration below it, like a blank line would.
            if (lensRow) indent = VisualColumn(line, FirstNonBlank(s));
            else if (s.Trim().Length == 0)
            {
                int next = NextNonBlankIndent(line);
                indent = Math.Max(Math.Min(prevIndent, next), 0);
            }
            else indent = VisualColumn(line, FirstNonBlank(s));
            prevIndent = indent;

            float y = RowY(row);
            for (int level = tab; level <= indent && level < 400; level += tab)
            {
                float x = MathF.Round(ColX(level - tab)) + 0.5f;
                if (x < v.TextX - 1) continue;
                Color32 color = Options.RainbowIndentGuides && pal.RainbowGuides.Length > 0
                    ? pal.RainbowGuides[(level / tab - 1) % pal.RainbowGuides.Length]
                    : pal.IndentGuide;
                canvas.RectFilled(x, y, 1, v.LineH, color);
            }
        }
    }

    private void DrawTrailingWhitespace(Canvas canvas, CodeEditorColors pal, int firstRow, int lastRow)
    {
        var v = _view;
        for (int row = firstRow; row <= lastRow; row++)
        {
            int line = DisplayRowToLine(row, out bool lensRow);
            if (lensRow) continue;
            string s = Document[line];
            int end = s.Length;
            while (end > 0 && (s[end - 1] == ' ' || s[end - 1] == '\t')) end--;
            if (end == s.Length || end == 0) continue;
            float x0 = ColX(VisualColumn(line, end)), x1 = ColX(VisualColumn(line, s.Length));
            canvas.RectFilled(x0, RowY(row) + 1, x1 - x0, v.LineH - 2, pal.TrailingWhitespace);
        }
    }

    private int NextNonBlankIndent(int line)
    {
        for (int i = line + 1, n = 0; i < Document.LineCount && n < 200; i++, n++)
        {
            string s = Document[i];
            if (s.Trim().Length > 0) return VisualColumn(i, FirstNonBlank(s));
        }
        return 0;
    }

    private void DrawText(Canvas canvas, CodeEditorColors pal, FontFile font, int firstRow, int lastRow)
    {
        var v = _view;
        float scale = canvas.FramebufferScale;
        var engine = canvas.Text.FontEngine;

        int firstCol = Math.Max(0, (int)(_scrollX / v.CharW) - 1);
        int lastCol = firstCol + (int)(v.TextW / v.CharW) + 3;
        float baseX = v.TextX - _scrollX;

        for (int row = firstRow; row <= lastRow; row++)
        {
            int line = DisplayRowToLine(row, out bool lensRow);
            if (lensRow)
            {
                DrawLensRow(canvas, pal, font, line, RowY(row));
                continue;
            }
            var r = GetRowRender(canvas, pal, font, line);
            float y = MathF.Round((RowY(row) + (v.LineH - v.GlyphH) * 0.5f) * scale);

            var runs = r.Runs;
            for (int i = FirstRunAt(runs, firstCol); i < runs.Length; i++)
            {
                ref var run = ref runs[i];
                if (run.Column > lastCol) break;
                if (run.Layout != null)
                {
                    engine.DrawLayout(run.Layout, new Float2(MathF.Round((baseX + run.Column * v.CharW) * scale), y), run.Color);
                    continue;
                }
                for (int k = 0; k < run.Text.Length; k++)
                {
                    float gx = MathF.Round((baseX + (run.Column + k) * v.CharW) * scale);
                    engine.DrawLayout(GlyphLayout(canvas, font, run.Text[k]), new Float2(gx, y), run.Color);
                }
            }

            if (Options.ShowWhitespace) DrawWhitespace(canvas, pal, line, RowY(row));

            if (TryGetCollapsed(line, out var fold))
                DrawFoldPlaceholder(canvas, pal, font, line, fold, RowY(row));
        }
    }

    private static int FirstRunAt(Run[] runs, int column)
    {
        int lo = 0, hi = runs.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (runs[mid].Column + runs[mid].Text.Length <= column) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private void DrawWhitespace(Canvas canvas, CodeEditorColors pal, int line, float rowY)
    {
        var v = _view;
        string s = Document[line];
        int tab = Math.Max(1, Options.TabSize);
        int vis = 0;
        float cy = rowY + v.LineH * 0.5f;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ' ') canvas.RectFilled(ColX(vis) + v.CharW * 0.5f - 1, cy - 1, 2, 2, pal.Whitespace);
            else if (c == '\t')
            {
                int w = tab - vis % tab;
                canvas.RectFilled(ColX(vis) + 2, cy, w * v.CharW - 4, 1, pal.Whitespace);
            }
            vis = c == '\t' ? vis + tab - vis % tab : vis + 1;
        }
    }

    private void DrawFoldPlaceholder(Canvas canvas, CodeEditorColors pal, FontFile font, int line, CodeFoldRange fold, float rowY)
    {
        var v = _view;
        string label = fold.Label ?? "...";
        float x = ColX(VisualColumn(line, Document.GetLineLength(line))) + v.CharW;
        float w = label.Length * v.CharW + v.CharW;
        canvas.RoundedRectFilled(x, rowY + 2, w, v.LineH - 4, 3, pal.FoldPlaceholder);
        DrawPlain(canvas, font, label, x + v.CharW * 0.5f, rowY + (v.LineH - v.GlyphH) * 0.5f, pal.PopupTextDim);
    }

    private RowRender GetRowRender(Canvas canvas, CodeEditorColors pal, FontFile font, int line)
    {
        string text = Document[line];
        int state = GetLineState(line);
        _semantic.TryGetValue(text, out var semantic);
        int tab = Math.Max(1, Options.TabSize);
        float fontKey = FontSize * canvas.FramebufferScale;

        _rows.TryGetValue(text, out var r);
        r ??= new RowRender();
        _rowsNext[text] = r;

        int depth = Options.BracketPairColors ? BracketDepthAt(line) : -1;
        if (r.Built && !r.Pending && r.State == state && ReferenceEquals(r.Semantic, semantic) && ReferenceEquals(r.Palette, pal)
            && r.FontKey == fontKey && r.TabSize == tab && ReferenceEquals(r.Font, font) && r.Ligatures == Options.Ligatures
            && r.BracketDepth == depth)
            return r;

        r.Built = true;
        r.State = state;
        r.Semantic = semantic;
        r.Palette = pal;
        r.FontKey = fontKey;
        r.TabSize = tab;
        r.Font = font;
        r.Ligatures = Options.Ligatures;
        r.BracketDepth = depth;
        if (r.TokenState != state || !r.Built)
        {
            LexLine(line, _tokens);
            r.Tokens = _tokens.ToArray();
            r.TokenState = state;
        }
        r.Runs = BuildRuns(canvas, font, r.Tokens, text, semantic, pal, tab, depth, out r.Pending);
        return r;
    }

    private CodeTokenKind[] _kinds = Array.Empty<CodeTokenKind>();

    private static bool IsBracket(char c, out bool open)
    {
        open = c is '(' or '[' or '{';
        return open || c is ')' or ']' or '}';
    }

    // Bracket nesting at the start of each line, counted from the lexed punctuation and rebuilt from the top after an edit.
    private readonly List<int> _bracketDepths = new();
    private int _bracketDepthVersion = -1;
    private readonly List<CodeToken> _bracketTokens = new();

    private int BracketDepthAt(int line)
    {
        if (_bracketDepthVersion != Document.Version)
        {
            _bracketDepthVersion = Document.Version;
            _bracketDepths.Clear();
            _bracketDepths.Add(0);
        }

        while (_bracketDepths.Count <= line)
        {
            int l = _bracketDepths.Count - 1;
            int depth = _bracketDepths[l];
            string s = Document[l];
            LexLine(l, _bracketTokens);
            foreach (var t in _bracketTokens)
            {
                if (t.Kind != CodeTokenKind.Punctuation) continue;
                for (int i = t.Start; i < t.End && i < s.Length; i++)
                    if (IsBracket(s[i], out bool open)) depth = open ? depth + 1 : Math.Max(0, depth - 1);
            }
            _bracketDepths.Add(depth);
        }
        return _bracketDepths[line];
    }
    private readonly List<Run> _runScratch = new();

    // depth is the bracket nesting at the start of the line, or -1 to leave brackets uncoloured.
    private Run[] BuildRuns(Canvas canvas, FontFile font, CodeToken[] tokens, string text, CodeToken[]? semantic, CodeEditorColors pal, int tab, int depth, out bool pending)
    {
        pending = false;
        if (text.Length == 0) return Array.Empty<Run>();
        if (_kinds.Length < text.Length) _kinds = new CodeTokenKind[Math.Max(text.Length, _kinds.Length * 2)];
        var kinds = _kinds;
        Array.Clear(kinds, 0, text.Length);
        foreach (var t in tokens)
            for (int i = t.Start; i < t.End && i < text.Length; i++) kinds[i] = t.Kind;
        if (semantic != null)
            foreach (var t in semantic)
                for (int i = t.Start; i < t.End && i < text.Length; i++) kinds[i] = t.Kind;

        // Words break at whitespace and at colour changes. Runs are capped so a minified line still
        // splits into small layouts that are cheap to skip when off screen.
        const int MaxRun = 64;
        _runScratch.Clear();
        int vis = 0, i0 = 0;
        while (i0 < text.Length)
        {
            char c = text[i0];
            if (c == ' ' || c == '\t')
            {
                vis = c == '\t' ? vis + tab - vis % tab : vis + 1;
                i0++;
                continue;
            }
            int i1 = i0 + 1;
            Color32 color = pal[kinds[i0]];
            bool open = false;
            bool bracket = depth >= 0 && pal.BracketPairs.Length > 0 && kinds[i0] == CodeTokenKind.Punctuation && IsBracket(c, out open);
            if (bracket)
            {
                // Each bracket is its own run, coloured by its nesting depth.
                if (!open) depth = Math.Max(0, depth - 1);
                color = pal.BracketPairs[depth % pal.BracketPairs.Length];
                if (open) depth++;
            }
            else
                while (i1 < text.Length && i1 - i0 < MaxRun && text[i1] != ' ' && text[i1] != '\t' && kinds[i1] == kinds[i0]
                       && !(depth >= 0 && kinds[i1] == CodeTokenKind.Punctuation && IsBracket(text[i1], out _))) i1++;
            string word = text.Substring(i0, i1 - i0);
            var layout = WordLayout(canvas, font, word);
            if (layout == null) pending = true;
            _runScratch.Add(new Run { Column = vis, Text = word, Layout = layout, Color = ToFont(color) });
            vis += i1 - i0;
            i0 = i1;
        }
        return _runScratch.ToArray();
    }

    // One layout per distinct word, shared across every row, so scrolling and typing rarely lay out new text.
    private readonly Dictionary<string, TextLayout> _layoutCache = new();

    private readonly Dictionary<char, TextLayout> _glyphLayouts = new();
    private double _layoutBudget;
    private double _layoutCostPerChar = 0.01;
    private const double LayoutBudgetMs = 1.5;

    // Null when laying the word out now would blow the frame budget while the user is typing. The run is then
    // drawn from single glyph layouts and laid out properly on a later, quieter frame.
    private TextLayout? WordLayout(Canvas canvas, FontFile font, string word)
    {
        if (_layoutCache.TryGetValue(word, out var layout)) return layout;
        bool typing = _time - _lastEditTime < 0.3;
        if (word.Length > 1 && typing && word.Length * _layoutCostPerChar > _layoutBudget) return null;

        if (_layoutCache.Count > 50000) _layoutCache.Clear();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        layout = CreateLayout(canvas, word, font, FontSize);
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _layoutBudget -= ms;
        _layoutCostPerChar = _layoutCostPerChar * 0.8 + ms / word.Length * 0.2;
        _layoutCache[word] = layout;
        return layout;
    }

    private TextLayout GlyphLayout(Canvas canvas, FontFile font, char c)
    {
        if (_glyphLayouts.TryGetValue(c, out var layout)) return layout;
        layout = CreateLayout(canvas, c.ToString(), font, FontSize);
        _glyphLayouts[c] = layout;
        return layout;
    }

    private static FontColor ToFont(Color32 c) => new(c.R, c.G, c.B, c.A);

    // Popup and status text changes with every keystroke, so text without a cached layout is drawn one
    // glyph at a time on the monospace grid and only laid out whole when the frame budget allows.
    private void DrawPlain(Canvas canvas, FontFile font, string text, float x, float y, Color32 color, float? size = null)
    {
        if (text.Length == 0) return;
        float sz = size ?? FontSize;
        float scale = canvas.FramebufferScale;
        var engine = canvas.Text.FontEngine;
        var fc = ToFont(color);
        var key = (sz, text);

        if (!_miscLayouts.TryGetValue(key, out var layout) && text.Length * _layoutCostPerChar <= _layoutBudget)
        {
            if (_miscLayouts.Count > 2048) _miscLayouts.Clear();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            layout = CreateLayout(canvas, text, font, sz);
            _layoutBudget -= (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _miscLayouts[key] = layout;
        }

        if (layout != null)
        {
            engine.DrawLayout(layout, new Float2(MathF.Round(x * scale), MathF.Round(y * scale)), fc);
            return;
        }

        float cw = _charW * (sz / FontSize);
        float py = MathF.Round(y * scale);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ') continue;
            engine.DrawLayout(SizedGlyph(canvas, font, text[i], sz), new Float2(MathF.Round((x + i * cw) * scale), py), fc);
        }
    }

    private readonly Dictionary<(float, char), TextLayout> _sizedGlyphs = new();

    private TextLayout SizedGlyph(Canvas canvas, FontFile font, char c, float size)
    {
        if (_sizedGlyphs.TryGetValue((size, c), out var layout)) return layout;
        layout = CreateLayout(canvas, c.ToString(), font, size);
        _sizedGlyphs[(size, c)] = layout;
        return layout;
    }

    private float MeasurePlain(Canvas canvas, FontFile font, string text, float size) => text.Length * _charW * (size / FontSize);

    private void DrawSquiggles(Canvas canvas, CodeEditorColors pal, int firstLine, int lastLine)
    {
        var v = _view;
        float clipX0 = v.TextX - v.CharW, clipX1 = v.MinimapX + v.CharW;
        for (int di = FirstDiagnosticFrom(firstLine); di < _diagnostics.Count; di++)
        {
            var d = _diagnostics[di];
            if (d.Range.End.Line < firstLine) continue;
            if (d.Range.Start.Line > lastLine) break;
            var color = d.Severity switch
            {
                CodeDiagnosticSeverity.Error => pal.Error,
                CodeDiagnosticSeverity.Warning => pal.Warning,
                _ => pal.Info,
            };

            for (int line = Math.Max(firstLine, d.Range.Start.Line); line <= Math.Min(lastLine, d.Range.End.Line); line++)
            {
                if (IsLineHidden(line)) continue;
                int c0 = line == d.Range.Start.Line ? d.Range.Start.Column : FirstNonBlank(Document[line]);
                int c1 = line == d.Range.End.Line ? d.Range.End.Column : Document.GetLineLength(line);
                float x0 = ColX(VisualColumn(line, c0));
                float x1 = ColX(VisualColumn(line, c1));
                if (x1 - x0 < v.CharW) x1 = x0 + v.CharW;
                x0 = MathF.Max(x0, clipX0);
                x1 = MathF.Min(x1, clipX1);
                if (x1 <= x0) continue;
                float y = RowY(LineToDisplayRow(line)) + v.LineH - 3;

                if (d.Severity == CodeDiagnosticSeverity.Info)
                {
                    canvas.RectFilled(x0, y + 1, x1 - x0, 1, color);
                    continue;
                }

                canvas.BeginPath();
                canvas.MoveTo(x0, y + 1.5f);
                const float step = 2f;
                bool up = true;
                for (float x = x0 + step; x <= x1 + 0.01f; x += step, up = !up)
                    canvas.LineTo(x, up ? y - 0.5f : y + 1.5f);
                canvas.SetStrokeColor(color);
                canvas.SetStrokeWidth(1f);
                canvas.Stroke();
            }
        }
    }

    private void DrawCarets(Canvas canvas, CodeEditorColors pal, int firstRow, int lastRow)
    {
        if (!IsFocused) return;
        var v = _view;
        double since = _time - _caretMoveTime;
        bool on = !Options.CaretBlink || since < 0.5 || (since % 1.0) < 0.55;
        if (!on) return;

        foreach (var s in _selections)
            DrawCaret(canvas, pal, s.Caret, Options.CaretStyle, firstRow, lastRow);

        // Where dragged text will land.
        if (_drag == DragKind.Move && _moveMoved)
            DrawCaret(canvas, pal, _moveDrop, CaretStyle.Line, firstRow, lastRow);
    }

    private void DrawCaret(Canvas canvas, CodeEditorColors pal, TextPosition at, CaretStyle style, int firstRow, int lastRow)
    {
        var v = _view;
        if (IsLineHidden(at.Line)) return;
        int row = LineToDisplayRow(at.Line);
        if (row < firstRow || row > lastRow) return;
        float x = MathF.Round(ColX(VisualColumn(at.Line, at.Column)));
        float y = RowY(row);
        switch (style)
        {
            case CaretStyle.Block:
                canvas.RectFilled(x, y + 1, v.CharW, v.LineH - 2, pal.Caret with { A = (byte)(pal.Caret.A / 2) });
                break;
            case CaretStyle.Underline:
                canvas.RectFilled(x, y + v.LineH - 3, v.CharW, 2, pal.Caret);
                break;
            default:
                float w = MathF.Max(1f, Options.CaretWidth);
                canvas.RectFilled(x - w * 0.5f, y + 1, w, v.LineH - 2, pal.Caret);
                break;
        }
    }

    private void DrawGutter(Canvas canvas, CodeEditorColors pal, FontFile font, int firstRow, int lastRow)
    {
        var v = _view;
        canvas.RectFilled(v.X, v.Y, v.GutterW, v.TextH, pal.GutterBackground);

        float numSize = FontSize;
        var activeLines = new HashSet<int>();
        foreach (var s in _selections) activeLines.Add(s.Caret.Line);

        bool gutterHover = _pointerInGutter;
        for (int row = firstRow; row <= lastRow; row++)
        {
            int line = DisplayRowToLine(row, out bool lensRow);
            if (lensRow) continue;
            float y = RowY(row);
            float ty = y + (v.LineH - v.GlyphH) * 0.5f;

            if (Options.ShowLineNumbers)
            {
                var color = ToFont(activeLines.Contains(line) ? pal.LineNumberActive : pal.LineNumber);
                DrawNumber(canvas, font, line + 1, v.NumbersX + v.NumbersW - v.CharW * 0.25f, ty, color);
            }

            if (Options.ShowFolding && TryGetFoldAt(line, out _))
            {
                bool collapsed = IsCollapsed(line);
                if (collapsed || gutterHover || _hoverFoldLine == line)
                    DrawChevron(canvas, v.FoldX + v.FoldW * 0.5f, y + v.LineH * 0.5f, v.LineH * 0.18f, !collapsed,
                        _hoverFoldLine == line ? pal.LineNumberActive : pal.FoldMarker);
            }
        }

        DrawMarkers(canvas, pal, firstRow, lastRow);
        DrawDiagnosticGlyphs(canvas, pal, firstRow, lastRow);
    }

    // Line numbers are drawn one cached digit at a time, so scrolling never lays out new text for them.
    private void DrawNumber(Canvas canvas, FontFile font, int number, float right, float y, FontColor color)
    {
        float scale = canvas.FramebufferScale;
        float x = right;
        var engine = canvas.Text.FontEngine;
        do
        {
            int digit = number % 10;
            number /= 10;
            x -= _charW;
            if (!_numberLayouts.TryGetValue(digit, out var layout))
            {
                layout = CreateLayout(canvas, ((char)('0' + digit)).ToString(), font, FontSize);
                _numberLayouts[digit] = layout;
            }
            engine.DrawLayout(layout, new Float2(MathF.Round(x * scale), MathF.Round(y * scale)), color);
        }
        while (number > 0);
    }

    private static void DrawChevron(Canvas canvas, float cx, float cy, float r, bool down, Color32 color)
    {
        canvas.BeginPath();
        if (down)
        {
            canvas.MoveTo(cx - r, cy - r * 0.5f);
            canvas.LineTo(cx, cy + r * 0.5f);
            canvas.LineTo(cx + r, cy - r * 0.5f);
        }
        else
        {
            canvas.MoveTo(cx - r * 0.5f, cy - r);
            canvas.LineTo(cx + r * 0.5f, cy);
            canvas.LineTo(cx - r * 0.5f, cy + r);
        }
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(1.4f);
        canvas.Stroke();
    }

    private void DrawMarkers(Canvas canvas, CodeEditorColors pal, int firstRow, int lastRow)
    {
        var v = _view;
        if (!Options.ShowGlyphMargin) return;
        float cx = v.GlyphX + v.GlyphW * 0.55f;
        float r = MathF.Min(v.GlyphW, v.LineH) * 0.3f;

        foreach (var m in _markers)
        {
            if (m.Line < 0 || m.Line >= Document.LineCount || IsLineHidden(m.Line)) continue;
            int row = LineToDisplayRow(m.Line);
            if (row < firstRow || row > lastRow) continue;
            float cy = RowY(row) + v.LineH * 0.5f;
            var color = m.Color ?? pal.Breakpoint;
            switch (m.Kind)
            {
                case CodeLineMarkerKind.Breakpoint:
                    canvas.CircleFilled(cx, cy, r, color);
                    break;
                case CodeLineMarkerKind.DisabledBreakpoint:
                    canvas.BeginPath();
                    canvas.Circle(cx, cy, r - 0.5f);
                    canvas.SetStrokeColor(color);
                    canvas.SetStrokeWidth(1.4f);
                    canvas.Stroke();
                    break;
                case CodeLineMarkerKind.Execution:
                    canvas.BeginPath();
                    canvas.MoveTo(cx - r, cy - r);
                    canvas.LineTo(cx + r, cy);
                    canvas.LineTo(cx - r, cy + r);
                    canvas.ClosePath();
                    canvas.SetFillColor(m.Color ?? new Color32(255, 214, 102, 255));
                    canvas.Fill();
                    break;
                case CodeLineMarkerKind.Bookmark:
                    canvas.RoundedRectFilled(cx - r * 0.7f, cy - r, r * 1.4f, r * 2f, 1.5f, m.Color ?? pal.Info);
                    break;
            }
        }

        if (_hoverGlyphLine >= 0 && _hoverGlyphLine < Document.LineCount && !IsLineHidden(_hoverGlyphLine))
        {
            bool has = false;
            foreach (var m in _markers) if (m.Line == _hoverGlyphLine && m.Kind != CodeLineMarkerKind.LineHighlight) has = true;
            int row = LineToDisplayRow(_hoverGlyphLine);
            if (!has && row >= firstRow && row <= lastRow)
                canvas.CircleFilled(cx, RowY(row) + v.LineH * 0.5f, r, new Color32(pal.Breakpoint.R, pal.Breakpoint.G, pal.Breakpoint.B, 90));
        }
    }

    private void DrawDiagnosticGlyphs(Canvas canvas, CodeEditorColors pal, int firstRow, int lastRow)
    {
        if (Options.ShowGlyphMargin) return;
        var v = _view;
        int lastLine = -1;
        foreach (var d in _diagnostics)
        {
            if (d.Severity < CodeDiagnosticSeverity.Warning || d.Range.Start.Line == lastLine) continue;
            lastLine = d.Range.Start.Line;
            int row = LineToDisplayRow(lastLine);
            if (row < firstRow || row > lastRow) continue;
            canvas.RectFilled(v.GlyphX + 1, RowY(row) + 2, 2, v.LineH - 4, d.Severity == CodeDiagnosticSeverity.Error ? pal.Error : pal.Warning);
        }
    }

    internal float MinimapScale => MathF.Max(1f, MathF.Round(_view.LineH / 8f));

    // Row shown at the top of the minimap, so the minimap scrolls in step with the editor.
    internal double MinimapTopRow()
    {
        var v = _view;
        float rowH = 2f * MinimapScale;
        double rows = v.TextH / rowH;
        double total = DisplayRowCount;
        if (total <= rows) return 0;
        double maxScroll = MaxScrollY();
        double t = maxScroll > 0 ? _scrollY / maxScroll : 0;
        return t * (total - rows);
    }

    private void DrawMinimap(Canvas canvas, CodeEditorColors pal, int firstRow, int lastRow)
    {
        var v = _view;
        float scale = MinimapScale;
        float rowH = 2f * scale;
        float charW = 1f * scale;
        double top = MinimapTopRow();
        int startRow = (int)top;
        int rows = (int)(v.TextH / rowH) + 2;
        float yOffset = (float)((top - startRow) * rowH);

        canvas.RectFilled(v.MinimapX, v.Y, v.MinimapW, v.TextH, new Color32(0, 0, 0, 30));

        int maxCols = (int)(v.MinimapW / charW) - 2;
        for (int i = 0; i < rows; i++)
        {
            int row = startRow + i;
            if (row >= DisplayRowCount) break;
            int line = DisplayRowToLine(row, out bool lensRow);
            if (lensRow) continue;
            string text = Document[line];
            if (text.Length == 0) continue;
            float y = v.Y + i * rowH - yOffset;
            var ml = MinimapTokens(line, text);
            int tab = Math.Max(1, Options.TabSize);

            foreach (var t in ml.Tokens)
            {
                if (t.Kind == CodeTokenKind.Plain) continue;
                int c0 = Math.Min(t.Start, text.Length);
                int vis0 = text.IndexOf('\t') >= 0 ? VisualColumn(line, c0) : c0;
                if (vis0 >= maxCols) break;
                int vis1 = Math.Min(maxCols, vis0 + t.Length);
                var c = pal[t.Kind];
                canvas.RectFilled(v.MinimapX + 2 + vis0 * charW, y, (vis1 - vis0) * charW, rowH * 0.75f, new Color32(c.R, c.G, c.B, 150));
            }
        }

        float sliderY = (float)(v.Y + (firstRow - top) * rowH);
        float sliderH = MathF.Max(4, (lastRow - firstRow) * rowH);
        var sliderColor = _minimapHover || _drag == DragKind.Minimap ? new Color32(pal.MinimapSlider.R, pal.MinimapSlider.G, pal.MinimapSlider.B, (byte)Math.Min(255, pal.MinimapSlider.A * 2)) : pal.MinimapSlider;
        canvas.RectFilled(v.MinimapX, sliderY, v.MinimapW, sliderH, sliderColor);
    }

    private MinimapLine MinimapTokens(int line, string text)
    {
        int state = GetLineState(line);
        if (_minimap.TryGetValue(text, out var ml) && ml.State == state) return ml;
        if (_minimap.Count > 8000) _minimap.Clear();

        LexLine(line, _tokens);
        var list = new List<CodeToken>(_tokens.Count);
        foreach (var t in _tokens)
        {
            if (t.Kind != CodeTokenKind.Comment && t.Kind != CodeTokenKind.DocComment && t.Kind != CodeTokenKind.String)
            {
                list.Add(t);
                continue;
            }
            // Split long runs at spaces so comments and strings read as words, not bars.
            int start = -1;
            for (int i = t.Start; i <= t.End; i++)
            {
                bool space = i == t.End || i >= text.Length || text[i] == ' ';
                if (!space && start < 0) start = i;
                else if (space && start >= 0) { list.Add(new CodeToken(start, i - start, t.Kind)); start = -1; }
            }
        }
        // Words that are plain text still show, just dim.
        for (int i = 0; i < list.Count; i++)
            if (list[i].Kind == CodeTokenKind.Plain && text.AsSpan(list[i].Start, list[i].Length).Trim().Length > 0)
                list[i] = new CodeToken(list[i].Start, list[i].Length, CodeTokenKind.Identifier);

        ml = new MinimapLine { State = state, Tokens = list.ToArray() };
        _minimap[text] = ml;
        return ml;
    }

    internal (float Y, float H) VerticalThumb()
    {
        var v = _view;
        double content = DisplayRowCount * (double)v.LineH + (Options.ScrollBeyondLastLine ? v.TextH - v.LineH : 0);
        double max = MaxScrollY();
        float h = (float)Math.Max(24, v.TextH * Math.Min(1, v.TextH / Math.Max(1, content)));
        float y = max > 0 ? (float)(_scrollY / max * (v.TextH - h)) : 0;
        return (v.Y + y, h);
    }

    internal (float X, float W) HorizontalThumb()
    {
        var v = _view;
        float max = MaxScrollX();
        float content = max + v.TextW;
        float w = MathF.Max(24, v.TextW * MathF.Min(1, v.TextW / MathF.Max(1, content)));
        float x = max > 0 ? _scrollX / max * (v.TextW - w) : 0;
        return (v.TextX + x, w);
    }

    private void DrawScrollbars(Canvas canvas, CodeEditorColors pal)
    {
        var v = _view;

        // Overview ruler: where errors, warnings and carets are in the whole file.
        int total = Math.Max(1, DisplayRowCount);
        float trackH = v.TextH;
        int lastRow = -1;
        foreach (var d in _diagnostics)
        {
            if (d.Severity < CodeDiagnosticSeverity.Warning) continue;
            int row = LineToDisplayRow(d.Range.Start.Line);
            if (row == lastRow) continue;
            lastRow = row;
            float y = v.Y + row / (float)total * trackH;
            canvas.RectFilled(v.VBarX + (d.Severity == CodeDiagnosticSeverity.Error ? 6 : 2), y, 4, 3, d.Severity == CodeDiagnosticSeverity.Error ? pal.Error : pal.Warning);
        }
        foreach (var s in _selections)
            canvas.RectFilled(v.VBarX, v.Y + LineToDisplayRow(s.Caret.Line) / (float)total * trackH, v.VBarW, 2, new Color32(pal.Caret.R, pal.Caret.G, pal.Caret.B, 160));

        if (MaxScrollY() > 0)
        {
            var (ty, th) = VerticalThumb();
            bool hot = _vbarHover || _drag == DragKind.VScroll;
            canvas.RectFilled(v.VBarX + 2, ty, v.VBarW - 4, th, hot ? pal.ScrollbarHover : pal.Scrollbar);
        }

        if (MaxScrollX() > 0)
        {
            var (tx, tw) = HorizontalThumb();
            bool hot = _hbarHover || _drag == DragKind.HScroll;
            canvas.RectFilled(tx, v.HBarY + 2, tw, v.HBarH - 4, hot ? pal.ScrollbarHover : pal.Scrollbar);
        }
    }

    private string _statusLeft = "", _statusRight = "";
    private int _statusKey;

    private void DrawStatusBar(Canvas canvas, CodeEditorColors pal, FontFile font)
    {
        var v = _view;
        canvas.RectFilled(v.X, v.StatusY, v.W, v.StatusH, pal.StatusBar);

        int errors = CountDiagnostics(CodeDiagnosticSeverity.Error);
        int warnings = CountDiagnostics(CodeDiagnosticSeverity.Warning);
        var sel = Selection;
        int selected = 0;
        foreach (var s in _selections) if (!s.IsEmpty) selected += Document.PositionToOffset(s.End) - Document.PositionToOffset(s.Start);

        int key = HashCode.Combine(Caret, selected, _selections.Count, errors, warnings, Options.TabSize, Options.InsertSpaces, HashCode.Combine(Document.LineEnding, _language.Name, IsDirty, FontSize));
        if (key != _statusKey)
        {
            _statusKey = key;
            _statusLeft = $"Ln {sel.Caret.Line + 1}, Col {VisualColumn(sel.Caret.Line, sel.Caret.Column) + 1}"
                + (selected > 0 ? $"  ({selected} selected)" : "")
                + (_selections.Count > 1 ? $"  {_selections.Count} carets" : "");
            string eol = Document.LineEnding == "\r\n" ? "CRLF" : "LF";
            string indent = Options.InsertSpaces ? $"Spaces: {Options.TabSize}" : $"Tab Size: {Options.TabSize}";
            int zoom = (int)MathF.Round(FontSize / Options.FontSize * 100f);
            _statusRight = $"{errors} errors   {warnings} warnings   {zoom}%   {indent}   {eol}   {_language.Name}";
        }

        float size = MathF.Max(9, FontSize * 0.85f);
        float ty = v.StatusY + (v.StatusH - size * 1.2f) * 0.5f;
        DrawPlain(canvas, font, _statusLeft, v.X + 10, ty, pal.StatusText, size);
        float w = MeasurePlain(canvas, font, _statusRight, size);
        DrawPlain(canvas, font, _statusRight, v.X + v.W - w - 12, ty, pal.StatusText, size);

        if (errors > 0) canvas.CircleFilled(v.X + v.W - w - 22, v.StatusY + v.StatusH * 0.5f, 3.5f, pal.Error);
    }

    #endregion
}
