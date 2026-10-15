// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

using Prowl.Editor.Core;
using Prowl.Editor.Core.Diagnostics;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Rosetta;
using Prowl.Runtime.Diagnostics;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// Live view of <see cref="FrameStats"/>: a scrolling chart stacked by category over a table of every
/// category. Single frames go past too fast to read, so frames are gathered into one column per refresh
/// interval. Click a column to pause on it, and a category to hide it from the chart. Categories with subcategories
/// show as one group that expands into them, with an Other row for the parent's own time.
/// </summary>
public class FrameStatsPanel : DockPanel
{
    [MenuItem("Window/Debug/Frame Stats", priority: 100)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(FrameStatsPanel));

    public override string Title => Loc.Get("panel.frame_stats");
    public override string Icon => EditorIcons.ChartColumn;

    // Enough columns to fill a wide chart at the narrowest column width.
    private const int ColumnCapacity = 2048;
    private const float MinColumnW = 1f, MaxColumnW = 32f, DefaultColumnW = 4f;
    private const float ToolbarH = 34f;
    private const float HeaderH = 22f;
    private const float ValueW = 64f;
    private const float SplitterH = 5f;
    private const float MinGraphH = 60f;
    private const float MinTableH = 60f;
    private static float RowH => EditorTheme.RowHeight;

    // Toolbar choices; the labels sit with them in DrawToolbar.
    private static readonly double[] RateSeconds = [0, 0.05, 0.1, 0.25, 1.0];
    private static readonly float[] ScaleMs = [0f, 8f, 16.67f, 33.33f, 66.67f];

    private readonly History _history = new(ColumnCapacity);
    // Rows switched on or off by the user, by category path; any other row follows its category's HiddenByDefault.
    // Kept by path, so a saved choice also reaches a category game code makes later.
    private readonly Dictionary<string, bool> _shown = new(StringComparer.OrdinalIgnoreCase);
    // Groups whose own time, the Other row under them, is switched off.
    private readonly HashSet<string> _otherHidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<FrameStat> _effectiveHidden = [];
    private readonly List<Row> _rows = [];
    // The hovered row: a category, or with _hoveredOther the Other row under it.
    private FrameStat? _hovered;
    private bool _hoveredOther;
    private long _selectedId = -1;
    private bool _paused;
    private int _rate = 2;
    private int _scale;
    private bool _stackMax;
    // Leaves switched-off rows out of the table, rather than greying them.
    private bool _hideUnselected;
    // Changes whenever a row is switched on or off or a group opens or closes, so the chart knows to redraw.
    private int _visibility;
    // The chart's bars, recorded once and replayed until what they show changes. The hover and selection highlights
    // and the readout change far more often, so they are drawn over the bars each frame.
    private readonly CanvasSnapshot _bars = new();
    private BarsKey _barsKey;
    private (ContentKey Content, bool StackMax) _tallestOf = (ContentKey.None, false);
    private float _tallestMs;
    // Chart height as a share of the space below the toolbar, once the splitter has been dragged; until then a default.
    private float _split = -1;
    private float _graphHAtDragStart;
    // When to write the layout after a setting changed, so a drag or a run of scroll steps is one write. 0 when not due.
    private long _saveAt;
    // Scrolling over the chart zooms: wider columns show fewer frames in more detail.
    private float _columnW = DefaultColumnW;
    // How many of the newest columns fit across the chart; the table and Auto scale describe only these.
    private int _visibleColumns;
    // Where the chart was last laid out, so the table can tell which column the pointer is over before the chart
    // draws, and the column under the pointer, or -1.
    private Rect _graphRect;
    private int _hoveredAgo = -1;

    private int SelectedAgo => _history.AgoOf(_selectedId);

    // The column the table describes: the selected one, else the hovered one, else the newest.
    private int ShownAgo => SelectedAgo >= 0 ? SelectedAgo : Math.Max(0, _hoveredAgo);
    private History.Column? ShownColumn => _history.Ago(ShownAgo);

    public override void OnGUI(Paper paper, float width, float height)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        // The panel's own work, here and in the callbacks Paper runs at the end of its frame, is scoped to its own
        // category. Only Paper's handling of its few elements and the upload of what it draws stay in the editor's UI.
        using FrameStatScope stats = EditorFrameStats.FrameStatsUI.Scope();

        // Frames from while paused are dropped, so the chart picks up from now when it resumes.
        if (_paused) _history.Skip();
        else
        {
            _effectiveHidden.Clear();
            foreach (FrameStat category in FrameStats.Categories)
                if (IsHidden(category)) _effectiveHidden.Add(category);
            _history.Sample(TimeSpan.FromSeconds(RateSeconds[_rate]), _effectiveHidden);
        }
        _visibleColumns = Math.Min(_history.Count, (int)MathF.Ceiling(width / _columnW));
        _hoveredAgo = HoveredColumn(paper);

        SaveIfDue();

        float below = height - ToolbarH - SplitterH;
        float maxGraphH = Math.Max(MinGraphH, below - MinTableH);
        float graphH = Math.Clamp(_split < 0 ? Math.Clamp(height * 0.4f, 90f, 240f) : _split * below, MinGraphH, maxGraphH);

        using (paper.Column("fs_root").Size(width, height).Enter())
        {
            DrawToolbar(paper);
            DrawGraph(paper, font, graphH);
            DrawSplitter(paper, graphH, maxGraphH, below);
            DrawTable(paper, font, width, height - ToolbarH - graphH - SplitterH);
        }
    }

    #region Saved state

    // Kept with the dock layout: the toolbar choices, the split, the zoom and which categories are hidden.
    public override bool SerializeState(JsonObject state)
    {
        state["rate"] = _rate;
        state["scale"] = _scale;
        state["stackMax"] = _stackMax;
        state["hideUnselected"] = _hideUnselected;
        state["columnWidth"] = (double)_columnW;
        if (_split >= 0) state["split"] = (double)_split;

        var shown = new JsonObject();
        foreach ((string path, bool on) in _shown) shown[path] = on;
        state["shown"] = shown;
        state["otherHidden"] = ToArray(_otherHidden);
        state["expanded"] = ToArray(_expanded);
        return true;
    }

    public override void RestoreState(JsonObject state)
    {
        if (TryRead(state["rate"], out int rate) && rate >= 0 && rate < RateSeconds.Length) _rate = rate;
        if (TryRead(state["scale"], out int scale) && scale >= 0 && scale < ScaleMs.Length) _scale = scale;
        if (TryRead(state["stackMax"], out bool stackMax)) _stackMax = stackMax;
        if (TryRead(state["hideUnselected"], out bool hideUnselected)) _hideUnselected = hideUnselected;
        if (TryRead(state["columnWidth"], out double columnW)) _columnW = Math.Clamp((float)columnW, MinColumnW, MaxColumnW);
        if (TryRead(state["split"], out double split) && split > 0 && split < 1) _split = (float)split;

        // Paths are kept even for categories that do not exist yet, without making them, so game code that makes
        // one later still gets the saved choice.
        if (state["shown"] is JsonObject shown)
        {
            _shown.Clear();
            foreach ((string path, JsonNode? node) in shown)
                if (TryRead(node, out bool on)) _shown[path] = on;
        }
        else if (state["hidden"] is JsonArray hidden)
        {
            // Layouts from before groups list only the hidden rows; every other row takes its default.
            _shown.Clear();
            foreach (JsonNode? node in hidden)
                if (TryRead(node, out string? path)) _shown[path!] = false;
        }
        ReadSet(state["otherHidden"], _otherHidden);
        ReadSet(state["expanded"], _expanded);
        _visibility++;
    }

    private static JsonArray ToArray(HashSet<string> paths)
    {
        var array = new JsonArray();
        foreach (string path in paths) array.Add(path);
        return array;
    }

    private static void ReadSet(JsonNode? node, HashSet<string> into)
    {
        if (node is not JsonArray array) return;
        into.Clear();
        foreach (JsonNode? item in array)
            if (TryRead(item, out string? path)) into.Add(path!);
    }

    private static bool TryRead<T>(JsonNode? node, out T? value)
    {
        value = default;
        return node is JsonValue v && v.TryGetValue(out value);
    }

    // Whether the category's own row is switched off, leaving it and everything under it out.
    private bool RowOff(FrameStat c) => _shown.TryGetValue(c.Path, out bool shown) ? !shown : c.HiddenByDefault;

    /// <summary>Whether the category's row reads as off: switched off itself, or under a group that is.</summary>
    internal bool IsRowHidden(FrameStat c)
    {
        for (FrameStat? row = c; row != null; row = row.Parent)
            if (RowOff(row)) return true;
        return false;
    }

    /// <summary>Whether the Other row under a group, its own time, reads as off.</summary>
    internal bool IsOtherHidden(FrameStat group) => IsRowHidden(group) || _otherHidden.Contains(group.Path);

    /// <summary>Whether the category's own time is left out of the chart.</summary>
    internal bool IsHidden(FrameStat c) => c.Children.Count > 0 ? IsOtherHidden(c) : IsRowHidden(c);

    /// <summary>Whether the table lists this row: always, unless switched-off rows are being left out.</summary>
    internal bool IsListed(FrameStat c, bool other = false) => !_hideUnselected || !(other ? IsOtherHidden(c) : IsRowHidden(c));

    internal bool HideUnselected
    {
        get => _hideUnselected;
        set
        {
            _hideUnselected = value;
            SaveSoon();
        }
    }

    internal bool IsExpanded(FrameStat group) => _expanded.Contains(group.Path);

    internal void ToggleExpanded(FrameStat group)
    {
        if (!_expanded.Remove(group.Path)) _expanded.Add(group.Path);
        _visibility++;
        SaveSoon();
    }

    /// <summary>A click on a row: <paramref name="other"/> for the Other row under a group.</summary>
    internal void ToggleVisibility(FrameStat c, bool other = false)
    {
        if (other)
        {
            if (!IsOtherHidden(c)) _otherHidden.Add(c.Path);
            else
            {
                // A group that was off comes on with only its own time showing.
                if (IsRowHidden(c))
                {
                    Reveal(c);
                    foreach (FrameStat child in c.Children) _shown[child.Path] = false;
                }
                _otherHidden.Remove(c.Path);
            }
        }
        else if (IsRowHidden(c)) Reveal(c);
        else _shown[c.Path] = false;
        _visibility++;
        SaveSoon();
    }

    // Switches a row on. Under a group that was off, it comes on alone: the group's other rows, its own time
    // included, go off, so what was hidden before stays hidden.
    private void Reveal(FrameStat c)
    {
        if (c.Parent is { } parent && IsRowHidden(parent))
        {
            Reveal(parent);
            _otherHidden.Add(parent.Path);
            foreach (FrameStat sibling in parent.Children)
                if (sibling != c) _shown[sibling.Path] = false;
        }
        _shown[c.Path] = true;
    }

    // The layout is otherwise only written on save, so a change is written a moment after it settles. Not during
    // play, where the other panels' state is the play session's and must not be kept.
    private void SaveSoon() => _saveAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency;

    private void SaveIfDue()
    {
        if (_saveAt == 0 || Stopwatch.GetTimestamp() < _saveAt || Runtime.Application.IsPlaying) return;
        _saveAt = 0;
        EditorApplication.Instance?.SaveDockLayout();
    }

    #endregion

    private void SetPaused(bool paused)
    {
        _paused = paused;
        // Columns move on again, so a selection would slide away under the pointer.
        if (!paused) _selectedId = -1;
    }

    private void Clear()
    {
        _history.Clear();
        _selectedId = -1;
    }

    // A divider line with a taller grab area, dragged to trade chart height for table height.
    private void DrawSplitter(Paper paper, float graphH, float maxGraphH, float below)
    {
        using (paper.Column("fs_split").Height(SplitterH).Cursor(PaperCursor.ResizeVertical)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .OnDragStart(_ => _graphHAtDragStart = graphH)
            .OnDragging(e =>
            {
                _split = Math.Clamp(_graphHAtDragStart + (float)e.TotalDelta.Y, MinGraphH, maxGraphH) / below;
                SaveSoon();
            })
            .Enter())
        {
            paper.Box("fs_split_line").Height(1).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
        }
    }

    private void ToggleSelection(int ago)
    {
        if (_history.Ago(ago) == null) return;

        long id = _history.IdOf(ago);
        if (id == _selectedId)
        {
            _selectedId = -1;
            return;
        }
        _selectedId = id;
        _paused = true;
    }

    private float StackedMs(History.Column column, int category) => _stackMax ? column.Max(category) : column.Avg(category);

    #region Toolbar

    private void DrawToolbar(Paper paper)
    {
        using (paper.Column("fs_tb_col").Height(ToolbarH).Enter())
        {
            using (paper.Row("fs_tb").Height(ToolbarH - 1).Padding(10, 8, 0, 0).Gap(6).Enter())
            {
                EditorGUI.ToolbarIconBtn(paper, "fs_pause", _paused ? EditorIcons.Play : EditorIcons.Pause,
                    Loc.Get(_paused ? "frame_stats.resume_tip" : "frame_stats.pause_tip"), _paused, () => SetPaused(!_paused));
                EditorGUI.ToolbarIconBtn(paper, "fs_clear", EditorIcons.Trash, Loc.Get("frame_stats.clear_tip"), false, Clear);

                paper.Box("fs_sp");

                ToolbarStrings text = ToolbarText();
                ToolbarDropdown(paper, "fs_rate", 100, text.RateTip, _rate, v => { _rate = v; SaveSoon(); }, text.Rates);
                ToolbarDropdown(paper, "fs_mode", 66, text.ModeTip, _stackMax ? 1 : 0, v => { _stackMax = v == 1; SaveSoon(); }, text.Modes);
                ToolbarDropdown(paper, "fs_scale", 80, text.ScaleTip, _scale, v => { _scale = v; SaveSoon(); }, text.Scales);
            }
            EditorGUI.Divider(paper, "fs_tb_div");
        }
    }

    // The toolbar's words, made again only when the language changes.
    private sealed record ToolbarStrings(string Locale, string RateTip, string ModeTip, string ScaleTip, string[] Rates, string[] Modes,
        string[] Scales);

    private ToolbarStrings? _toolbar;

    private ToolbarStrings ToolbarText()
    {
        if (_toolbar?.Locale == Loc.CurrentLocale) return _toolbar;
        return _toolbar = new ToolbarStrings(Loc.CurrentLocale,
            Loc.Get("frame_stats.rate_tip"), Loc.Get("frame_stats.mode_tip"), Loc.Get("frame_stats.scale_tip"),
            [Loc.Get("frame_stats.rate_every_frame"), "0.05 s", "0.1 s", "0.25 s", "1 s"],
            [Caps(Loc.Get("frame_stats.mode_avg")), Caps(Loc.Get("frame_stats.mode_max"))],
            [Loc.Get("frame_stats.scale_auto"), "8 ms", "16 ms", "33 ms", "66 ms"]);
    }

    private static void ToolbarDropdown(Paper paper, string id, float width, string tooltip, int index, Action<int> onChange, string[] labels)
    {
        // Hover bubbles up from the dropdown to this wrapper, so the wrapper carries the tooltip.
        using (paper.Row($"{id}_wrap").Width(width).Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Tooltip(tooltip).Enter())
            Origami.Dropdown(paper, id, index, onChange, labels).Width(width).Show();
    }

    #endregion

    #region Graph

    private void DrawGraph(Paper paper, FontFile font, float height)
    {
        paper.Box("fs_graph").Height(height).Clip()
            .OnClick(0, (_, e) => ToggleSelection(ColumnAt((float)e.RelativePosition.X, (float)e.ElementRect.Size.X, _columnW)))
            .OnScroll(e =>
            {
                e.StopPropagation();
                _columnW = Math.Clamp(_columnW * MathF.Exp(e.Delta * 0.15f), MinColumnW, MaxColumnW);
                SaveSoon();
            })
            .OnPostLayout((handle, rect) =>
            {
                _graphRect = rect;
                paper.Draw(ref handle, (canvas, r) =>
                {
                    using FrameStatScope stats = EditorFrameStats.FrameStatsUI.Scope();
                    PaintGraph(paper, canvas, r, font);
                });
            });
    }

    private int HoveredColumn(Paper paper)
    {
        Float2 ptr = paper.PointerPos;
        float x = (float)_graphRect.Min.X, w = (float)_graphRect.Size.X;
        if (ptr.X < x || ptr.X >= x + w || ptr.Y < _graphRect.Min.Y || ptr.Y >= _graphRect.Max.Y) return -1;

        int ago = ColumnAt((float)ptr.X - x, w, _columnW);
        return ago < _visibleColumns ? ago : -1;
    }

    private void PaintGraph(Paper paper, Canvas canvas, Rect r, FontFile font)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, h = (float)r.Size.Y;
        canvas.RectFilled(x, y, w, h, Color32.FromArgb(255, 10, 10, 14));

        IReadOnlyList<FrameStat> categories = FrameStats.Categories;
        float scaleMs = ScaleMs[_scale] > 0 ? ScaleMs[_scale] : AutoScaleMs(TallestStackMs(categories));
        float colW = _columnW;
        float barW = Math.Max(1f, colW - (colW > 3f ? 1f : 0f));
        float bottom = y + h;

        Float2 ptr = paper.PointerPos;
        bool pointerIn = ptr.X >= x && ptr.X < x + w && ptr.Y >= y && ptr.Y < y + h;
        int hoveredAgo = pointerIn ? ColumnAt((float)ptr.X - x, w, colW) : -1;
        int selectedAgo = SelectedAgo;

        // The bars, target lines and scale are recorded, anchored at the chart's corner so they replay wherever the panel
        // moves. Paused, or between columns, a frame costs one copy of them.
        var key = new BarsKey(Content, colW, w, h, scaleMs, _stackMax, _hovered, _hoveredOther, ThemeKey.Current);
        if (key != _barsKey || !canvas.DrawSnapshot(_bars, x, y))
        {
            // While a row is hovered its blocks stand out, and which row that is changes as the pointer moves down the
            // table. That is drawn without recording, keeping the plain chart's recording to replay when it leaves.
            bool record = WorthRecording && _hovered == null;
            if (record) canvas.BeginSnapshot(_bars, x, y);
            // Newest at the right edge, older ones leftwards until the chart is full.
            for (int ago = 0; ago < _visibleColumns; ago++)
                DrawStack(canvas, categories, _history.Ago(ago)!, x + w - (ago + 1) * colW, barW, bottom, h, scaleMs);
            PaintTargets(canvas, font, x, y, w, h, scaleMs);
            // A capture that cannot replay is drawn again next frame.
            if (record) _barsKey = canvas.EndSnapshot() ? key : default;
        }
        PaintGraphOverlay(canvas, font, x, y, w, h, colW, barW, hoveredAgo, selectedAgo);
    }

    private static void PaintTargets(Canvas canvas, FontFile font, float x, float y, float w, float h, float scaleMs)
    {
        float bottom = y + h;
        // Most important first, so when the chart is too short for all of them the common targets win. Stroking changes
        // canvas state, which a recording must hand back as it found it.
        canvas.SaveState();
        Span<float> drawnY = stackalloc float[TargetLines.Length];
        int drawn = 0;
        foreach ((int fps, Color32 color, string label) in TargetLines)
        {
            float lineY = ToY(1000f / fps, bottom, h, scaleMs);
            bool crowded = false;
            for (int i = 0; i < drawn && !crowded; i++)
                crowded = MathF.Abs(drawnY[i] - lineY) < TargetLabelH;
            if (crowded || !TargetLine(canvas, font, x, w, lineY, y, label, color)) continue;
            drawnY[drawn++] = lineY;
        }
        canvas.RestoreState();

        FontFile mono = EditorTheme.FontMono ?? font;
        canvas.DrawText($"{scaleMs:0.#} ms", x + w - 6, y + 4, EditorTheme.InkDim, EditorTheme.FontSizeSmall, mono, 0, new Float2(1, 0));
    }

    private void PaintGraphOverlay(Canvas canvas, FontFile font, float x, float y, float w, float h, float colW, float barW,
        int hoveredAgo, int selectedAgo)
    {
        if (selectedAgo >= 0 && selectedAgo < _visibleColumns)
            canvas.RectFilled(x + w - (selectedAgo + 1) * colW, y, barW, h, EditorTheme.WithAlpha(EditorTheme.Accent, 70));
        if (hoveredAgo >= 0 && hoveredAgo < _visibleColumns && hoveredAgo != selectedAgo)
            canvas.RectFilled(x + w - (hoveredAgo + 1) * colW, y, barW, h, Color32.FromArgb(28, 255, 255, 255));

        int readoutAgo = _history.Ago(selectedAgo) != null ? selectedAgo : hoveredAgo;
        if (_history.Ago(readoutAgo) is not { } readout) return;

        // Paused with a column selected, the same words would otherwise be laid out again every frame.
        var key = new ReadoutKey(_history.Version, readoutAgo, Loc.CurrentLocale, ThemeKey.Current);
        if (key == _readoutKey && canvas.DrawSnapshot(_readout, x, y)) return;

        canvas.BeginSnapshot(_readout, x, y);
        canvas.DrawText(Loc.Get("frame_stats.readout", new
        {
            frames = readout.Frames,
            avg = readout.AvgFrameMs.ToString("0.00"),
            peak = readout.PeakMs.ToString("0.00"),
        }), x + 6, y + 4, EditorTheme.Ink500, EditorTheme.FontSizeSmall, EditorTheme.FontMono ?? font);
        // A capture that cannot replay is drawn again next frame.
        _readoutKey = canvas.EndSnapshot() ? key : default;
    }

    // The column the readout describes, by how far back it is in the columns held at that version.
    private readonly record struct ReadoutKey(long History, int Ago, string Locale, ThemeKey Theme);

    private readonly CanvasSnapshot _readout = new();
    private ReadoutKey _readoutKey;

    private void DrawStack(Canvas canvas, IReadOnlyList<FrameStat> categories, History.Column column, float x, float width,
        float chartBottom, float chartH, float scaleMs)
    {
        var bar = new Bar(canvas, column, categories.Count, x, width, chartBottom, chartH, scaleMs);
        foreach (FrameStat c in categories)
            if (c.Parent == null) StackCategory(ref bar, c);
    }

    // One column's bar while it is being stacked.
    private struct Bar(Canvas canvas, History.Column column, int count, float x, float width, float chartBottom, float chartH, float scaleMs)
    {
        public readonly Canvas Canvas = canvas;
        public readonly History.Column Column = column;
        public readonly int Count = count;
        public readonly float X = x, Width = width, ChartBottom = chartBottom, ChartH = chartH, ScaleMs = scaleMs;
        public float Stack;
    }

    // A collapsed group is one block of everything shown under it; otherwise its own time, then each subcategory.
    private void StackCategory(ref Bar bar, FrameStat c)
    {
        if (bar.Stack >= bar.ScaleMs || c.Index >= bar.Count || RowOff(c)) return;

        if (c.Children.Count > 0 && !IsExpanded(c))
        {
            StackBlock(ref bar, ShownMs(bar.Column, bar.Count, c), c.Color, Lit(c, ownTime: false));
            return;
        }

        if (c.Children.Count == 0 || !_otherHidden.Contains(c.Path))
            StackBlock(ref bar, StackedMs(bar.Column, c.Index), c.Color, Lit(c, ownTime: true));
        foreach (FrameStat child in c.Children)
            StackCategory(ref bar, child);
    }

    private static void StackBlock(ref Bar bar, float ms, VColor color, bool lit)
    {
        if (ms <= 0) return;
        float bottom = ToY(bar.Stack, bar.ChartBottom, bar.ChartH, bar.ScaleMs);
        float top = ToY(bar.Stack + ms, bar.ChartBottom, bar.ChartH, bar.ScaleMs);
        bar.Stack += ms;
        if (bottom - top < 0.5f) return;
        bar.Canvas.RectFilled(bar.X, top, bar.Width, bottom - top, ToColor32(color, lit ? 230 : 60));
    }

    // The category's own time and that of everything under it, leaving out what is switched off.
    private float ShownMs(History.Column column, int count, FrameStat c)
    {
        if (c.Index >= count || RowOff(c)) return 0;
        float ms = c.Children.Count > 0 && _otherHidden.Contains(c.Path) ? 0 : StackedMs(column, c.Index);
        foreach (FrameStat child in c.Children)
            ms += ShownMs(column, count, child);
        return ms;
    }

    // Whether a block stands out for the hovered row: the row's own block, or any block in the group it heads. The
    // Other row lights only its group's own time.
    private bool Lit(FrameStat block, bool ownTime)
    {
        if (_hovered == null) return true;
        if (_hoveredOther) return ownTime && block == _hovered;
        for (FrameStat? row = block; row != null; row = row.Parent)
            if (row == _hovered) return true;
        return false;
    }

    private static float ToY(float ms, float chartBottom, float chartH, float scaleMs)
        => chartBottom - Math.Min(ms, scaleMs) / scaleMs * chartH;

    // What the chart and table show: the columns held and how many fit, which rows are switched on and opened, and how
    // many categories there are. Every drawing kept for reuse depends on it.
    private readonly record struct ContentKey(long History, int Columns, int Visibility, int Categories)
    {
        public static readonly ContentKey None = new(-1, 0, 0, 0);
    }

    private ContentKey Content => new(_history.Version, _visibleColumns, _visibility, FrameStats.Categories.Count);

    // The theme's colours and text size the kept drawings use. They change with the theme or accent, and every frame
    // while the theme eases from one to the next.
    private readonly record struct ThemeKey(int Accent, int Ink500, int Ink400, int InkDim, int InkFaint, int BorderSoft, float FontSize)
    {
        public static ThemeKey Current => new(EditorTheme.Accent.ToArgb(), EditorTheme.Ink500.ToArgb(), EditorTheme.Ink400.ToArgb(),
            EditorTheme.InkDim.ToArgb(), EditorTheme.InkFaint.ToArgb(), EditorTheme.BorderSoft.ToArgb(), EditorTheme.FontSizeSmall);
    }

    // Everything the recorded bars depend on. The hovered row is part of it because its blocks stand out.
    private readonly record struct BarsKey(ContentKey Content, float ColumnW, float Width, float Height, float ScaleMs,
        bool StackMax, FrameStat? Hovered, bool HoveredOther, ThemeKey Theme);

    // Scanning every column only when a column arrives or what is shown changes, not every frame.
    private float TallestStackMs(IReadOnlyList<FrameStat> categories)
    {
        var of = (Content, _stackMax);
        if (of != _tallestOf)
        {
            _tallestOf = of;
            _tallestMs = ScanTallestStackMs(categories);
        }
        return _tallestMs;
    }

    private float ScanTallestStackMs(IReadOnlyList<FrameStat> categories)
    {
        float tallest = 0;
        for (int ago = 0; ago < _visibleColumns; ago++)
        {
            History.Column column = _history.Ago(ago)!;
            float stack = 0;
            for (int i = 0; i < categories.Count; i++)
                if (!IsHidden(categories[i]))
                    stack += StackedMs(column, i);
            tallest = Math.Max(tallest, stack);
        }
        return tallest;
    }

    // Frame rate targets marked on the chart, most important first: common monitor refresh rates, and 30 for
    // the lowest playable rate.
    private static readonly (int Fps, Color32 Color, string Label)[] TargetLines =
    [
        (60, Color32.FromArgb(60, 80, 200, 100), "60 fps"),
        (30, Color32.FromArgb(60, 220, 180, 50), "30 fps"),
        (120, Color32.FromArgb(60, 80, 190, 220), "120 fps"),
        (144, Color32.FromArgb(60, 120, 140, 240), "144 fps"),
        (240, Color32.FromArgb(60, 190, 120, 230), "240 fps"),
    ];

    // The height a target line's label takes above it.
    private const float TargetLabelH = 14f;

    private static bool TargetLine(Canvas canvas, FontFile font, float x, float w, float lineY, float top, string label, Color32 color)
    {
        // Off the chart, or too close to the top to fit its label.
        if (lineY <= top + TargetLabelH) return false;
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(1f);
        canvas.BeginPath();
        canvas.MoveTo(x, lineY);
        canvas.LineTo(x + w, lineY);
        canvas.Stroke();
        canvas.DrawText(label, x + 6, lineY - 12, color, 9, font);
        return true;
    }

    private static readonly float[] AutoScaleSteps = [1f, 1.5f, 2f, 3f, 4f, 5f, 6f, 8f, 10f];

    /// <summary>
    /// The chart height for Auto: the tallest stack plus a little headroom, rounded up to a round number of
    /// milliseconds so the scale label stays readable. Never under 0.5 ms, so a near-empty chart is not all noise.
    /// </summary>
    internal static float AutoScaleMs(float tallestMs)
    {
        float target = Math.Max(0.5f, tallestMs * 1.1f);
        float magnitude = MathF.Pow(10f, MathF.Floor(MathF.Log10(target)));
        foreach (float step in AutoScaleSteps)
            if (step * magnitude >= target * 0.9999f)
                return step * magnitude;
        return 10f * magnitude;
    }

    /// <summary>Which column, counted back from the newest at the right edge, lies under <paramref name="x"/>.</summary>
    internal static int ColumnAt(float x, float width, float columnW) => (int)((width - x) / columnW);

    #endregion

    #region Table

    // The table is drawn on the canvas, like the chart: as Paper elements its rows came to a few hundred, each styled,
    // laid out and drawn every frame. It is two elements, the header and the rows, with the rows in a scroll view.
    // Clicks, hover and tooltips work from where the pointer is, and the rows are recorded and replayed until something
    // in them changes.
    private const float TablePadL = 10f, TablePadR = 18f, TableGap = 8f;
    private const float BodyTop = HeaderH + 1;

    private enum HitKind { None, Eye, Header, Row, Expand }

    /// <summary>What the pointer is over in the table: a header column or a row by index, or the eye.</summary>
    private readonly record struct TableHit(HitKind Kind, int Index = -1)
    {
        public bool OnRow => Kind is HitKind.Row or HitKind.Expand;
    }

    // Everything the recorded rows depend on.
    private readonly record struct RowsKey(ContentKey Content, int ShownAgo, bool HideUnselected, float Scroll,
        float Width, float ViewH, string Locale, ThemeKey Theme);

    private readonly CanvasSnapshot _rowsSnapshot = new();
    private RowsKey _rowsKey;
    private Rect _headerRect, _rowsRect;
    private int _headerId, _rowsId;
    private TableHit _tableHit;
    // How far the rows are scrolled and how much of them shows, as the scroll view last laid them out.
    private float _scrollY, _viewH;
    // What the row list was last built from; it is sorted by the totals, so it only changes with them.
    private (ContentKey Content, bool HideUnselected) _rowsOf = (ContentKey.None, false);

    private void DrawTable(Paper paper, FontFile font, float width, float height)
    {
        IReadOnlyList<FrameStat> categories = FrameStats.Categories;
        History.Totals totals = _history.Summarise(categories.Count, _visibleColumns);

        var rowsOf = (Content, _hideUnselected);
        if (rowsOf != _rowsOf)
        {
            _rowsOf = rowsOf;
            _rows.Clear();
            AddRows(categories, null, 0, totals);
        }

        _tableHit = HitAt(paper);
        // The hovered row lights its blocks in the chart.
        _hovered = _tableHit.OnRow ? _rows[_tableHit.Index].Category : null;
        _hoveredOther = _tableHit.OnRow && _rows[_tableHit.Index].Other;

        paper.Box("fs_hdr").Height(BodyTop)
            .OnClick(_ => ClickTable(HitAt(paper)))
            .OnHover(e => ClaimTooltip(e.Source, HitAt(paper)))
            .OnPostLayout((handle, rect) =>
            {
                _headerRect = rect;
                _headerId = handle.Data.ID;
                paper.Draw(ref handle, (canvas, r) =>
                {
                    using FrameStatScope stats = EditorFrameStats.FrameStatsUI.Scope();
                    PaintHeader(canvas, (float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, font);
                });
            });

        // Only the rows in view are drawn, which the scroll view allows when it snaps rather than eases.
        Origami.ScrollView(paper, "fs_rows", width, Math.Max(0, height - BodyTop)).SmoothScroll(false).WheelStep(RowH * 2).Body(view =>
        {
            _scrollY = view.ScrollY;
            _viewH = view.Height;
            paper.Box("fs_rows_body").Height(Math.Max(1, _rows.Count * RowH))
                .OnClick(_ => ClickTable(HitAt(paper)))
                .OnHover(e => ClaimTooltip(e.Source, HitAt(paper)))
                .OnPostLayout((handle, rect) =>
                {
                    _rowsRect = rect;
                    _rowsId = handle.Data.ID;
                    paper.Draw(ref handle, (canvas, r) =>
                    {
                        using FrameStatScope stats = EditorFrameStats.FrameStatsUI.Scope();
                        PaintRows(canvas, r, font, totals);
                    });
                });
        });
    }

    // The right edge of value column k, counted from the left: Now, Avg, Max, Calls, %.
    private static float ValueRight(float width, int k) => width - TablePadR - (ValueColumns - 1 - k) * (ValueW + TableGap);

    private const int ValueColumns = 5;

    private TableHit HitAt(Paper paper)
    {
        Float2 pointer = paper.PointerPos;
        if (_headerId != 0 && paper.IsElementHovered(_headerId))
        {
            float hx = (float)(pointer.X - _headerRect.Min.X), w = (float)_headerRect.Size.X;
            if ((float)(pointer.Y - _headerRect.Min.Y) >= HeaderH) return default;
            if (hx >= TablePadL - 3 && hx < TablePadL + 13) return new TableHit(HitKind.Eye);
            for (int k = 0; k < ValueColumns; k++)
                if (hx >= ValueRight(w, k) - ValueW && hx < ValueRight(w, k)) return new TableHit(HitKind.Header, k);
            return default;
        }
        if (_rowsId == 0 || !paper.IsElementHovered(_rowsId)) return default;

        // The rows run on past the scroll view, which clips them; only those in view are under the pointer.
        float lx = (float)(pointer.X - _rowsRect.Min.X), ly = (float)(pointer.Y - _rowsRect.Min.Y);
        if (ly < _scrollY || ly >= _scrollY + _viewH) return default;
        int row = (int)(ly / RowH);
        if (row < 0 || row >= _rows.Count) return default;
        Row r = _rows[row];
        float x0 = TablePadL + r.Depth * 20;
        bool group = !r.Other && r.Category.Children.Count > 0;
        return new TableHit(group && lx >= x0 && lx < x0 + 12 ? HitKind.Expand : HitKind.Row, row);
    }

    private void ClickTable(TableHit hit)
    {
        switch (hit.Kind)
        {
            case HitKind.Eye: HideUnselected = !_hideUnselected; break;
            case HitKind.Expand: ToggleExpanded(_rows[hit.Index].Category); break;
            case HitKind.Row: ToggleVisibility(_rows[hit.Index].Category, _rows[hit.Index].Other); break;
        }
    }

    // Each row, header column and the eye claims its tooltip under an ID of its own, so moving from one to the next waits
    // out the hover delay again, as it would between separate elements.
    private void ClaimTooltip(ElementHandle source, TableHit hit)
    {
        if (TooltipFor(hit) is not { } tip) return;
        int depth = 0;
        for (ElementHandle h = source.GetParentHandle(); h.IsValid; h = h.GetParentHandle())
            depth++;
        // A row's arrow shares the row's tooltip, so it keeps the row's ID.
        HitKind kind = hit.Kind == HitKind.Expand ? HitKind.Row : hit.Kind;
        TooltipSystem.Hover(HashCode.Combine(source.Data.ID, kind, hit.Index), tip, depth);
    }

    private TooltipContent? TooltipFor(TableHit hit)
    {
        switch (hit.Kind)
        {
            case HitKind.Eye: return HeaderText().Eye(_hideUnselected);
            case HitKind.Header: return HeaderText().Tips[hit.Index];
            case HitKind.Row or HitKind.Expand:
                Row row = _rows[hit.Index];
                return RowTextFor(row.Category, row.Other, !row.Other && row.Category.Children.Count > 0).Tip;
            default: return null;
        }
    }

    // Recording copies everything drawn, which only pays off if it is replayed. Running at a new column every frame, the
    // chart and table change every frame and never would be, while paused or between columns they replay until they do.
    private bool WorthRecording => _paused || RateSeconds[_rate] > 0;

    // Everything the recorded header depends on.
    private readonly record struct HeaderKey(int NowLabel, bool HideUnselected, float Width, string Locale, ThemeKey Theme);

    private readonly CanvasSnapshot _headerSnapshot = new();
    private HeaderKey _headerKey;

    // Only a handful of words, but laying text out costs more than replaying it, so the header is recorded too.
    private void PaintHeader(Canvas canvas, float x, float y, float w, FontFile font)
    {
        int nowLabel = SelectedAgo >= 0 ? 2 : _hoveredAgo >= 0 ? 1 : 0;

        // The eye sits over the rows' colour swatches, the column it switches. Its highlight goes under the
        // recording, so hovering it does not record the header again.
        if (_tableHit.Kind == HitKind.Eye)
            canvas.RoundedRectFilled(x + TablePadL - 3, y + 2, 16, HeaderH - 4, EditorTheme.Roundness, EditorTheme.Hover);

        var key = new HeaderKey(nowLabel, _hideUnselected, w, Loc.CurrentLocale, ThemeKey.Current);
        if (key == _headerKey && canvas.DrawSnapshot(_headerSnapshot, x, y)) return;

        canvas.BeginSnapshot(_headerSnapshot, x, y);
        float fs = EditorTheme.FontSizeSmall, mid = y + HeaderH / 2;
        HeaderStrings text = HeaderText();
        canvas.DrawText(_hideUnselected ? EditorIcons.EyeSlash : EditorIcons.Eye, x + TablePadL + 5, mid,
            _hideUnselected ? EditorTheme.Accent : EditorTheme.InkDim, fs, font, 0, new Float2(0.5f, 0.5f));

        canvas.DrawText(text.Category, x + TablePadL + 10 + TableGap, mid, EditorTheme.InkDim, fs, font, 0, new Float2(0, 0.5f));
        for (int k = 0; k < ValueColumns; k++)
            canvas.DrawText(k == 0 ? text.Now[nowLabel] : text.Values[k], x + ValueRight(w, k), mid, EditorTheme.InkDim, fs, font, 0,
                new Float2(1, 0.5f));

        canvas.RectFilled(x, y + HeaderH, w, 1, EditorTheme.BorderSoft);
        // A capture that cannot replay is drawn again next frame.
        _headerKey = canvas.EndSnapshot() ? key : default;
    }

    private void PaintRows(Canvas canvas, Rect r, FontFile font, History.Totals totals)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X;
        // The hovered row's highlight goes under the rows, so moving down the table does not record them again.
        if (_tableHit.OnRow)
            canvas.RectFilled(x, y + _tableHit.Index * RowH, w, RowH, EditorTheme.Hover);

        // Anchored at the rows' corner, which moves as they scroll, so the scroll is part of the key.
        var key = new RowsKey(Content, ShownAgo, _hideUnselected, _scrollY, w, _viewH, Loc.CurrentLocale, ThemeKey.Current);
        if (key == _rowsKey && canvas.DrawSnapshot(_rowsSnapshot, x, y)) return;

        bool record = WorthRecording;
        if (record) canvas.BeginSnapshot(_rowsSnapshot, x, y);

        float fs = EditorTheme.FontSizeSmall;
        FontFile mono = EditorTheme.FontMono ?? font;
        History.Column? shown = ShownColumn;
        float nameRight = x + ValueRight(w, 0) - ValueW - TableGap;

        int first = Math.Max(0, (int)(_scrollY / RowH)), last = Math.Min(_rows.Count, (int)MathF.Ceiling((_scrollY + _viewH) / RowH));
        for (int index = first; index < last; index++)
        {
            Row row = _rows[index];
            FrameStat c = row.Category;
            bool other = row.Other;
            // A group row stands for everything under it; its own time alone is the Other row.
            bool group = !other && c.Children.Count > 0;
            int i = c.Index;
            bool hidden = other ? IsOtherHidden(c) : IsRowHidden(c);
            Color32 nameInk = hidden ? EditorTheme.InkFaint : EditorTheme.Ink500;
            Color32 valueInk = hidden ? EditorTheme.InkFaint : EditorTheme.Ink400;
            RowText text = RowTextFor(c, other, group);
            float top = y + index * RowH, mid = top + RowH / 2;

            float cx = x + TablePadL + row.Depth * 20;
            if (group)
            {
                canvas.DrawText(IsExpanded(c) ? EditorIcons.ChevronDown : EditorIcons.ChevronRight, cx + 6, mid, nameInk, fs, font, 0,
                    new Float2(0.5f, 0.5f));
                cx += 12 + TableGap;
            }

            Color32 swatch = ToColor32(c.Color, 255);
            if (hidden)
            {
                canvas.SaveState();
                canvas.SetStrokeColor(swatch);
                canvas.SetStrokeWidth(1f);
                canvas.BeginPath();
                canvas.RoundedRect(cx + 0.5f, mid - 4.5f, 9, 9, 2);
                canvas.Stroke();
                canvas.RestoreState();
            }
            else canvas.RoundedRectFilled(cx, mid - 5, 10, 10, 2, swatch);
            cx += 10 + TableGap;

            canvas.SaveState();
            canvas.IntersectScissor(cx, top, Math.Max(0, nameRight - cx), RowH);
            canvas.DrawText(text.Label, cx, mid, nameInk, fs, font, 0, new Float2(0, 0.5f));
            canvas.RestoreState();

            float nowMs = shown == null ? 0 : group ? shown.TreeAvg(i) : shown.Avg(i);
            float calls = shown == null ? 0 : group ? shown.TreeCalls(i) : shown.Calls(i);
            float share = totals.AvgFrameMs > 0 ? row.AvgMs / totals.AvgFrameMs * 100f : 0;
            Value(0, text.Now.Format(nowMs, Ms));
            Value(1, text.Avg.Format(row.AvgMs, Ms));
            Value(2, text.Max.Format(group ? totals.TreeMaxMs[i] : totals.MaxMs[i], Ms));
            Value(3, text.Calls.Format(calls, static v => v.ToString("0.#")));
            Value(4, text.Share.Format(share, static v => $"{v:0.0}%"));

            void Value(int k, string value) => canvas.DrawText(value, x + ValueRight(w, k), mid, valueInk, fs, mono, 0, new Float2(1, 0.5f));
        }

        // A capture that cannot replay is drawn again next frame.
        if (record) _rowsKey = canvas.EndSnapshot() ? key : default;
    }

    // The header's words, in capitals, and its tooltips, made again only when the language changes.
    private sealed class HeaderStrings
    {
        public string Locale = "";
        public string Category = "";
        public string[] Now = [];
        public string[] Values = [];
        public TooltipContent[] Tips = [];
        public TooltipContent HideTip = new(), ShowTip = new();
        public TooltipContent Eye(bool hiding) => hiding ? ShowTip : HideTip;
    }

    private HeaderStrings? _header;

    private HeaderStrings HeaderText()
    {
        if (_header?.Locale == Loc.CurrentLocale) return _header;
        TooltipContent Tip(string key) => new(Loc.Get(key));
        return _header = new HeaderStrings
        {
            Locale = Loc.CurrentLocale,
            Category = Caps(Loc.Get("frame_stats.col_category")),
            Now = [Caps(Loc.Get("frame_stats.col_now")), Caps(Loc.Get("frame_stats.col_hovered")), Caps(Loc.Get("frame_stats.col_selected"))],
            Values = ["", Caps(Loc.Get("frame_stats.col_avg")), Caps(Loc.Get("frame_stats.col_max")), Caps(Loc.Get("frame_stats.col_calls")), "%"],
            Tips =
            [
                Tip("frame_stats.col_now_tip"), Tip("frame_stats.col_avg_tip"), Tip("frame_stats.col_max_tip"),
                Tip("frame_stats.col_calls_tip"), Tip("frame_stats.col_share_tip"),
            ],
            HideTip = Tip("frame_stats.hide_unselected_tip"),
            ShowTip = Tip("frame_stats.show_unselected_tip"),
        };
    }

    /// <summary>A table row: a category, or with <see cref="Other"/> the Other row for a group's own time.</summary>
    private readonly record struct Row(FrameStat Category, bool Other, int Depth, float AvgMs);

    // The rows under a group, or the top level for null, slowest first, each expanded group followed by its own.
    private void AddRows(IReadOnlyList<FrameStat> categories, FrameStat? group, int depth, History.Totals totals)
    {
        var level = new List<Row>();
        if (group != null && IsListed(group, other: true))
            level.Add(new Row(group, true, depth, totals.AvgMs[group.Index]));
        foreach (FrameStat c in group?.Children ?? categories)
        {
            // Registered since this frame's totals were taken, or met again under its parent.
            if (c.Index >= categories.Count || (group == null && c.Parent != null)) continue;
            if (!IsListed(c)) continue;
            level.Add(new Row(c, false, depth, c.Children.Count > 0 ? totals.TreeAvgMs[c.Index] : totals.AvgMs[c.Index]));
        }
        level.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));

        foreach (Row row in level)
        {
            _rows.Add(row);
            if (!row.Other && row.Category.Children.Count > 0 && IsExpanded(row.Category))
                AddRows(categories, row.Category, depth + 1, totals);
        }
    }

    // What a row would otherwise build every frame: its label, tooltip and numbers. A number is formatted again only
    // when it changes, which is once a column at most.
    private sealed class RowText
    {
        public string Label = "";
        public TooltipContent Tip = new();
        public string? Locale;
        public bool Group;
        public readonly CachedNumber Now = new(), Avg = new(), Max = new(), Calls = new(), Share = new();
    }

    private sealed class CachedNumber
    {
        private float _value = float.NaN;
        private string _text = "";

        public string Format(float value, Func<float, string> format)
        {
            if (value != _value)
            {
                _value = value;
                _text = format(value);
            }
            return _text;
        }
    }

    private readonly Dictionary<(FrameStat, bool), RowText> _rowText = [];

    private RowText RowTextFor(FrameStat c, bool other, bool group)
    {
        if (!_rowText.TryGetValue((c, other), out RowText? text))
            _rowText[(c, other)] = text = new RowText();

        // Words follow the language, and a category gains its group tip once something is made under it.
        if (text.Locale != Loc.CurrentLocale || text.Group != group)
        {
            text.Locale = Loc.CurrentLocale;
            text.Group = group;
            text.Label = other ? Loc.Get("frame_stats.group_other") : DisplayName(c);
            text.Tip = new TooltipContent { Title = other ? $"{DisplayPath(c)}/{text.Label}" : DisplayPath(c), Text = RowTooltip(c, other, group) };
        }
        return text;
    }

    // What each built-in category measures. Categories made by game code get only the hint.
    private static readonly Dictionary<FrameStat, string> DescriptionKeys = new()
    {
        [FrameStats.Input] = "frame_stats.desc_input",
        [FrameStats.Async] = "frame_stats.desc_async",
        [FrameStats.Assets] = "frame_stats.desc_assets",
        [FrameStats.Update] = "frame_stats.desc_update",
        [FrameStats.Physics] = "frame_stats.desc_physics",
        [FrameStats.Animation] = "frame_stats.desc_animation",
        [FrameStats.Navigation] = "frame_stats.desc_navigation",
        [FrameStats.Audio] = "frame_stats.desc_audio",
        [FrameStats.Render] = "frame_stats.desc_render",
        [FrameStats.UI] = "frame_stats.desc_ui",
        [FrameStats.Editor] = "frame_stats.desc_editor",
        [EditorFrameStats.UI] = "frame_stats.desc_editor_ui",
        [EditorFrameStats.Gizmos] = "frame_stats.desc_editor_gizmos",
        [EditorFrameStats.Assets] = "frame_stats.desc_editor_assets",
        [EditorFrameStats.Scripts] = "frame_stats.desc_editor_scripts",
        [EditorFrameStats.FrameStatsUI] = "frame_stats.desc_editor_frame_stats",
        [FrameStats.RenderWait] = "frame_stats.desc_render_wait",
        [FrameStats.Idle] = "frame_stats.desc_idle",
        [FrameStats.GC] = "frame_stats.desc_gc",
        [FrameStats.Other] = "frame_stats.desc_other",
    };

    // Built-in categories' names in the editor's language. Categories made by game code keep the name they were given.
    private static readonly Dictionary<FrameStat, string> NameKeys = new()
    {
        [FrameStats.Input] = "frame_stats.name_input",
        [FrameStats.Async] = "frame_stats.name_async",
        [FrameStats.Assets] = "frame_stats.name_assets",
        [FrameStats.Update] = "frame_stats.name_update",
        [FrameStats.Physics] = "frame_stats.name_physics",
        [FrameStats.Animation] = "frame_stats.name_animation",
        [FrameStats.Navigation] = "frame_stats.name_navigation",
        [FrameStats.Audio] = "frame_stats.name_audio",
        [FrameStats.Render] = "frame_stats.name_render",
        [FrameStats.UI] = "frame_stats.name_ui",
        [FrameStats.Editor] = "frame_stats.name_editor",
        [EditorFrameStats.UI] = "frame_stats.name_editor_ui",
        [EditorFrameStats.Gizmos] = "frame_stats.name_editor_gizmos",
        [EditorFrameStats.Assets] = "frame_stats.name_editor_assets",
        [EditorFrameStats.Scripts] = "frame_stats.name_editor_scripts",
        [EditorFrameStats.FrameStatsUI] = "frame_stats.name_editor_frame_stats",
        [FrameStats.RenderWait] = "frame_stats.name_render_wait",
        [FrameStats.Idle] = "frame_stats.name_idle",
        [FrameStats.GC] = "frame_stats.name_gc",
        [FrameStats.Other] = "frame_stats.name_other",
    };

    private static string DisplayName(FrameStat c) => NameKeys.TryGetValue(c, out string? key) ? Loc.Get(key) : c.Name;

    // The category's path with each part in the editor's language, such as "Editor/Gizmos".
    private static string DisplayPath(FrameStat c) => c.Parent == null ? DisplayName(c) : $"{DisplayPath(c.Parent)}/{DisplayName(c)}";

    // What a group's own time is made of, where it is known. Other groups get the general description.
    private static readonly Dictionary<FrameStat, string> OtherDescriptionKeys = new()
    {
        [FrameStats.Editor] = "frame_stats.desc_editor_other",
    };

    private static string RowTooltip(FrameStat category, bool other, bool group)
    {
        var parts = new List<string>(3);
        if (other) parts.Add(Loc.Get(OtherDescriptionKeys.TryGetValue(category, out string? otherKey) ? otherKey : "frame_stats.group_other_tip"));
        else if (DescriptionKeys.TryGetValue(category, out string? key)) parts.Add(Loc.Get(key));
        if (group) parts.Add(Loc.Get("frame_stats.group_tip"));
        parts.Add(Loc.Get("frame_stats.row_tip"));
        return string.Join("\n\n", parts);
    }

    // Headers and the AVG/MAX choice are shown in capitals. The translations stay in normal case, and the
    // locale's own rules uppercase them, so Turkish gets a dotted capital I.
    private static string Caps(string text)
    {
        try { return text.ToUpper(CultureInfo.GetCultureInfo(Loc.CurrentLocale)); }
        catch (CultureNotFoundException) { return text.ToUpperInvariant(); }
    }

    private static string Ms(float ms) => ms <= 0.005f ? "-" : $"{ms:0.00}";

    #endregion

    private static Color32 ToColor32(VColor c, int alpha) => Color32.FromArgb(alpha, ToByte(c.R), ToByte(c.G), ToByte(c.B));
    private static int ToByte(float v) => (int)Math.Clamp(v * 255f + 0.5f, 0f, 255f);

    /// <summary>
    /// The panel's data, apart from its drawing: <see cref="FrameStats"/> frames gathered into columns, one
    /// per refresh interval, keeping the newest <see cref="Capacity"/>.
    /// </summary>
    internal sealed class History
    {
        /// <summary>One interval's worth of frames. Per-category arrays are indexed like <see cref="FrameStats.Categories"/>.</summary>
        public sealed class Column
        {
            public int Frames;
            public float AvgFrameMs;
            /// <summary>The slowest frame, counting only the categories shown when it was gathered.</summary>
            public float PeakMs;
            public float[] AvgMs = [];
            public float[] MaxMs = [];
            public float[] AvgCalls = [];
            // The same for each category together with everything under it, added up within each frame.
            public float[] TreeAvgMs = [];
            public float[] TreeMaxMs = [];
            public float[] TreeAvgCalls = [];

            // A category registered after the column was made has nothing in it.
            public float Avg(int category) => category < AvgMs.Length ? AvgMs[category] : 0;
            public float Max(int category) => category < MaxMs.Length ? MaxMs[category] : 0;
            public float Calls(int category) => category < AvgCalls.Length ? AvgCalls[category] : 0;
            public float TreeAvg(int category) => category < TreeAvgMs.Length ? TreeAvgMs[category] : 0;
            public float TreeMax(int category) => category < TreeMaxMs.Length ? TreeMaxMs[category] : 0;
            public float TreeCalls(int category) => category < TreeAvgCalls.Length ? TreeAvgCalls[category] : 0;
        }

        /// <summary>
        /// Every category's average and worst frame across the newest columns, weighted by frames. The arrays are
        /// reused, so they hold until the next <see cref="Summarise"/>.
        /// </summary>
        public readonly record struct Totals(float[] AvgMs, float[] MaxMs, float[] TreeAvgMs, float[] TreeMaxMs, float AvgFrameMs);

        private readonly Column?[] _columns;
        private long _committed;

        // The column being gathered.
        private float[] _sumMs = [], _maxMs = [], _sumCalls = [];
        private float[] _sumTreeMs = [], _maxTreeMs = [], _sumTreeCalls = [];
        // One frame's per-category totals with everything under each, while they are added up.
        private float[] _frameTreeMs = [], _frameTreeCalls = [];
        private float _sumFrameMs, _peakMs;
        private int _pendingFrames;
        private long _pendingSince = Stopwatch.GetTimestamp();
        private long _framesSeen = FrameStats.FrameCount;
        private float[] _totalAvg = [], _totalMax = [], _totalTreeAvg = [], _totalTreeMax = [];
        // What the last totals covered, so asking again before a column arrives reuses them.
        private Totals _totals;
        private (long Version, int Categories, int Columns) _totalsOf = (-1, 0, 0);

        public History(int capacity) => _columns = new Column?[capacity];

        /// <summary>Changes whenever the columns held change, so anything drawn from them can be kept until it does.</summary>
        public long Version { get; private set; }

        public int Capacity => _columns.Length;

        /// <summary>How many columns are held, at most <see cref="Capacity"/>.</summary>
        public int Count => (int)Math.Min(_committed, Capacity);

        /// <summary>A column by how many columns back from the newest it is, or null past what is held.</summary>
        public Column? Ago(int ago) => ago >= 0 && ago < Count ? _columns[(_committed - 1 - ago) % Capacity] : null;

        /// <summary>An id for the column <paramref name="ago"/> back that stays with it as newer columns arrive.</summary>
        public long IdOf(int ago) => _committed - 1 - ago;

        /// <summary>How far back the column with this id now is, or -1 once it is no longer held.</summary>
        public int AgoOf(long id)
        {
            if (id < 0) return -1;
            long ago = _committed - 1 - id;
            return ago < Count ? (int)ago : -1;
        }

        /// <summary>
        /// Takes in the frames recorded since the last call and, once <paramref name="interval"/> has passed
        /// since the column started, closes it. Categories in <paramref name="hidden"/> do not count towards its peak.
        /// </summary>
        public void Sample(TimeSpan interval, IReadOnlySet<FrameStat> hidden)
        {
            IReadOnlyList<FrameStat> categories = FrameStats.Categories;
            if (_sumMs.Length < categories.Count)
            {
                Array.Resize(ref _sumMs, categories.Count);
                Array.Resize(ref _maxMs, categories.Count);
                Array.Resize(ref _sumCalls, categories.Count);
                Array.Resize(ref _sumTreeMs, categories.Count);
                Array.Resize(ref _maxTreeMs, categories.Count);
                Array.Resize(ref _sumTreeCalls, categories.Count);
                Array.Resize(ref _frameTreeMs, categories.Count);
                Array.Resize(ref _frameTreeCalls, categories.Count);
            }

            long count = FrameStats.FrameCount;
            int fresh = (int)Math.Min(count - _framesSeen, FrameStats.HistoryCount);
            _framesSeen = count;

            for (int ago = fresh - 1; ago >= 0; ago--)
            {
                float shownMs = 0;
                for (int i = 0; i < categories.Count; i++)
                {
                    FrameStatSample s = categories[i].GetSample(ago);
                    _sumMs[i] += s.Ms;
                    _maxMs[i] = Math.Max(_maxMs[i], s.Ms);
                    _sumCalls[i] += s.Calls;
                    _frameTreeMs[i] = s.Ms;
                    _frameTreeCalls[i] = s.Calls;
                    if (!hidden.Contains(categories[i])) shownMs += s.Ms;
                }
                // A parent comes before everything under it, so walking back finishes each category's total, then
                // adds it into its parent's before the parent is reached.
                for (int i = categories.Count - 1; i >= 0; i--)
                {
                    _sumTreeMs[i] += _frameTreeMs[i];
                    _maxTreeMs[i] = Math.Max(_maxTreeMs[i], _frameTreeMs[i]);
                    _sumTreeCalls[i] += _frameTreeCalls[i];
                    if (categories[i].Parent is { } parent)
                    {
                        _frameTreeMs[parent.Index] += _frameTreeMs[i];
                        _frameTreeCalls[parent.Index] += _frameTreeCalls[i];
                    }
                }
                _sumFrameMs += FrameStats.GetFrameMs(ago);
                _peakMs = Math.Max(_peakMs, shownMs);
                _pendingFrames++;
            }

            if (_pendingFrames > 0 && Stopwatch.GetElapsedTime(_pendingSince) >= interval)
                Commit(categories.Count);
        }

        /// <summary>Drops the frames recorded since the last call, and whatever was gathered towards the next column.</summary>
        public void Skip()
        {
            _framesSeen = FrameStats.FrameCount;
            DropPending();
        }

        /// <summary>Drops every column, and the one being gathered.</summary>
        public void Clear()
        {
            // The column objects stay for reuse; nothing past the count is ever read.
            _committed = 0;
            Version++;
            DropPending();
        }

        /// <summary>Totals over the newest <paramref name="columns"/> columns.</summary>
        public Totals Summarise(int categoryCount, int columns)
        {
            var of = (Version, categoryCount, columns);
            if (of == _totalsOf) return _totals;
            _totalsOf = of;
            _totals = Total(categoryCount, columns);
            return _totals;
        }

        private Totals Total(int categoryCount, int columns)
        {
            if (_totalAvg.Length < categoryCount)
            {
                _totalAvg = new float[categoryCount];
                _totalMax = new float[categoryCount];
                _totalTreeAvg = new float[categoryCount];
                _totalTreeMax = new float[categoryCount];
            }
            float[] avg = _totalAvg, max = _totalMax, treeAvg = _totalTreeAvg, treeMax = _totalTreeMax;
            Array.Clear(avg);
            Array.Clear(max);
            Array.Clear(treeAvg);
            Array.Clear(treeMax);
            int frames = 0;
            float frameMs = 0;

            for (int ago = 0; ago < Math.Min(columns, Count); ago++)
            {
                Column column = Ago(ago)!;
                frames += column.Frames;
                frameMs += column.AvgFrameMs * column.Frames;
                for (int i = 0; i < categoryCount; i++)
                {
                    avg[i] += column.Avg(i) * column.Frames;
                    max[i] = Math.Max(max[i], column.Max(i));
                    treeAvg[i] += column.TreeAvg(i) * column.Frames;
                    treeMax[i] = Math.Max(treeMax[i], column.TreeMax(i));
                }
            }

            if (frames == 0) return new Totals(avg, max, treeAvg, treeMax, 0);
            for (int i = 0; i < categoryCount; i++)
            {
                avg[i] /= frames;
                treeAvg[i] /= frames;
            }
            return new Totals(avg, max, treeAvg, treeMax, frameMs / frames);
        }

        private void Commit(int categoryCount)
        {
            float n = _pendingFrames;

            // Columns are reused as the ring wraps, so taking one in allocates nothing once the ring is full.
            Column column = _columns[_committed % Capacity] ??= new Column();
            if (column.AvgMs.Length != categoryCount)
            {
                column.AvgMs = new float[categoryCount];
                column.MaxMs = new float[categoryCount];
                column.AvgCalls = new float[categoryCount];
                column.TreeAvgMs = new float[categoryCount];
                column.TreeMaxMs = new float[categoryCount];
                column.TreeAvgCalls = new float[categoryCount];
            }
            column.Frames = _pendingFrames;
            column.AvgFrameMs = _sumFrameMs / n;
            column.PeakMs = _peakMs;
            for (int i = 0; i < categoryCount; i++)
            {
                column.AvgMs[i] = _sumMs[i] / n;
                column.MaxMs[i] = _maxMs[i];
                column.AvgCalls[i] = _sumCalls[i] / n;
                column.TreeAvgMs[i] = _sumTreeMs[i] / n;
                column.TreeMaxMs[i] = _maxTreeMs[i];
                column.TreeAvgCalls[i] = _sumTreeCalls[i] / n;
            }

            _committed++;
            Version++;
            DropPending();
        }

        private void DropPending()
        {
            Array.Clear(_sumMs);
            Array.Clear(_maxMs);
            Array.Clear(_sumCalls);
            Array.Clear(_sumTreeMs);
            Array.Clear(_maxTreeMs);
            Array.Clear(_sumTreeCalls);
            _sumFrameMs = _peakMs = 0;
            _pendingFrames = 0;
            _pendingSince = Stopwatch.GetTimestamp();
        }
    }
}
