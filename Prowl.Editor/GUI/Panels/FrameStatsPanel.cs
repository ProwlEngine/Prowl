// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

using Prowl.Editor.Core;
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
using TextAlignment = Prowl.PaperUI.TextAlignment;
using VColor = Prowl.Vector.Color;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// Live view of <see cref="FrameStats"/>: a scrolling chart stacked by category over a table of every
/// category. Single frames go past too fast to read, so frames are gathered into one column per refresh
/// interval. Click a column to pause on it, and a category to hide it from the chart.
/// </summary>
public class FrameStatsPanel : DockPanel
{
    [MenuItem("Window/Debug/Frame Stats", priority: 100)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(FrameStatsPanel));

    public override string Title => Loc.Get("panel.frame_stats");
    public override string Icon => EditorIcons.ChartColumn;

    // Enough columns to fill a wide chart at the narrowest column width.
    private const int ColumnCapacity = 2048;
    private const float MinColumnW = 1f, MaxColumnW = 32f;
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
    // Idle starts hidden: with a frame cap it is most of every frame and squashes the rest.
    private readonly HashSet<FrameStat> _hidden = [FrameStats.Idle];
    private readonly List<(int Index, float AvgMs)> _rows = [];
    private FrameStat? _hovered;
    private long _selectedId = -1;
    private bool _paused;
    private int _rate = 2;
    private int _scale;
    private bool _stackMax;
    // Chart height once the splitter has been dragged; until then it follows the panel's height.
    private float _graphH = -1;
    private float _graphHAtDragStart;
    // Scrolling over the chart zooms: wider columns show fewer frames in more detail.
    private float _columnW = 4f;
    // How many of the newest columns fit across the chart; the table and Auto scale describe only these.
    private int _visibleColumns;
    // Where the chart was last laid out, so the table can tell which column the pointer is over before the chart
    // draws, and the column under the pointer, or -1.
    private Rect _graphRect;
    private int _hoveredAgo = -1;

    private int SelectedAgo => _history.AgoOf(_selectedId);

    // The column the toolbar and the table describe: the hovered one, else the selected one, else the newest.
    private int ShownAgo => _hoveredAgo >= 0 ? _hoveredAgo : Math.Max(0, SelectedAgo);
    private History.Column? ShownColumn => _history.Ago(ShownAgo);

    public override void OnGUI(Paper paper, float width, float height)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Frames from while paused are dropped, so the chart picks up from now when it resumes.
        if (_paused) _history.Skip();
        else _history.Sample(TimeSpan.FromSeconds(RateSeconds[_rate]), _hidden);
        _visibleColumns = Math.Min(_history.Count, (int)MathF.Ceiling(width / _columnW));
        _hoveredAgo = HoveredColumn(paper);

        float maxGraphH = Math.Max(MinGraphH, height - ToolbarH - SplitterH - MinTableH);
        float graphH = Math.Clamp(_graphH < 0 ? Math.Clamp(height * 0.4f, 90f, 240f) : _graphH, MinGraphH, maxGraphH);

        using (paper.Column("fs_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font);
            DrawGraph(paper, font, graphH);
            DrawSplitter(paper, graphH, maxGraphH);
            DrawTable(paper, font, width, height - ToolbarH - graphH - SplitterH);
        }
    }

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
    private void DrawSplitter(Paper paper, float graphH, float maxGraphH)
    {
        using (paper.Column("fs_split").Height(SplitterH).Cursor(PaperCursor.ResizeVertical)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .OnDragStart(_ => _graphHAtDragStart = graphH)
            .OnDragging(e => _graphH = Math.Clamp(_graphHAtDragStart + (float)e.TotalDelta.Y, MinGraphH, maxGraphH))
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

    private void DrawToolbar(Paper paper, FontFile font)
    {
        using (paper.Column("fs_tb_col").Height(ToolbarH).Enter())
        {
            using (paper.Row("fs_tb").Height(ToolbarH - 1).Padding(10, 8, 0, 0).Gap(6).Enter())
            {
                EditorGUI.ToolbarIconBtn(paper, "fs_pause", _paused ? EditorIcons.Play : EditorIcons.Pause,
                    Loc.Get(_paused ? "frame_stats.resume_tip" : "frame_stats.pause_tip"), _paused, () => SetPaused(!_paused));
                EditorGUI.ToolbarIconBtn(paper, "fs_clear", EditorIcons.Trash, Loc.Get("frame_stats.clear_tip"), false, Clear);

                paper.Box("fs_sp");

                ToolbarDropdown(paper, "fs_rate", 100, Loc.Get("frame_stats.rate_tip"), _rate, v => _rate = v,
                    [Loc.Get("frame_stats.rate_every_frame"), "0.05 s", "0.1 s", "0.25 s", "1 s"]);
                ToolbarDropdown(paper, "fs_mode", 66, Loc.Get("frame_stats.mode_tip"), _stackMax ? 1 : 0, v => _stackMax = v == 1,
                    [Caps(Loc.Get("frame_stats.mode_avg")), Caps(Loc.Get("frame_stats.mode_max"))]);
                ToolbarDropdown(paper, "fs_scale", 80, Loc.Get("frame_stats.scale_tip"), _scale, v => _scale = v,
                    [Loc.Get("frame_stats.scale_auto"), "8 ms", "16 ms", "33 ms", "66 ms"]);
            }
            EditorGUI.Divider(paper, "fs_tb_div");
        }
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
            })
            .OnPostLayout((handle, rect) =>
            {
                _graphRect = rect;
                paper.Draw(ref handle, (canvas, r) => PaintGraph(paper, canvas, r, font));
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

        // Newest at the right edge, older ones leftwards until the chart is full.
        for (int ago = 0; ago < _visibleColumns; ago++)
        {
            float cx = x + w - (ago + 1) * colW;
            DrawStack(canvas, categories, _history.Ago(ago)!, cx, barW, bottom, h, scaleMs);

            if (ago == selectedAgo)
                canvas.RectFilled(cx, y, barW, h, EditorTheme.WithAlpha(EditorTheme.Accent, 70));
            else if (ago == hoveredAgo)
                canvas.RectFilled(cx, y, barW, h, Color32.FromArgb(28, 255, 255, 255));
        }

        TargetLine(canvas, font, x, w, ToY(1000f / 60f, bottom, h, scaleMs), y, "60 fps", Color32.FromArgb(60, 80, 200, 100));
        TargetLine(canvas, font, x, w, ToY(1000f / 30f, bottom, h, scaleMs), y, "30 fps", Color32.FromArgb(60, 220, 180, 50));

        float fs = EditorTheme.FontSizeSmall;
        FontFile mono = EditorTheme.FontMono ?? font;
        canvas.DrawText($"{scaleMs:0.#} ms", x + w - 6, y + 4, EditorTheme.InkDim, fs, mono, 0, new Float2(1, 0));

        if ((_history.Ago(hoveredAgo) ?? _history.Ago(selectedAgo)) is { } readout)
            canvas.DrawText(Loc.Get("frame_stats.readout", new
            {
                frames = readout.Frames,
                avg = readout.AvgFrameMs.ToString("0.00"),
                peak = readout.PeakMs.ToString("0.00"),
            }), x + 6, y + 4, EditorTheme.Ink500, fs, mono);
    }

    private void DrawStack(Canvas canvas, IReadOnlyList<FrameStat> categories, History.Column column, float x, float width,
        float chartBottom, float chartH, float scaleMs)
    {
        float stack = 0;
        for (int i = 0; i < categories.Count && stack < scaleMs; i++)
        {
            FrameStat c = categories[i];
            float ms = StackedMs(column, i);
            if (ms <= 0 || _hidden.Contains(c)) continue;

            float bottom = ToY(stack, chartBottom, chartH, scaleMs), top = ToY(stack + ms, chartBottom, chartH, scaleMs);
            stack += ms;
            if (bottom - top < 0.5f) continue;

            int alpha = _hovered == null || _hovered == c ? 230 : 60;
            canvas.RectFilled(x, top, width, bottom - top, ToColor32(c.Color, alpha));
        }
    }

    private static float ToY(float ms, float chartBottom, float chartH, float scaleMs)
        => chartBottom - Math.Min(ms, scaleMs) / scaleMs * chartH;

    private float TallestStackMs(IReadOnlyList<FrameStat> categories)
    {
        float tallest = 0;
        for (int ago = 0; ago < _visibleColumns; ago++)
        {
            History.Column column = _history.Ago(ago)!;
            float stack = 0;
            for (int i = 0; i < categories.Count; i++)
                if (!_hidden.Contains(categories[i]))
                    stack += StackedMs(column, i);
            tallest = Math.Max(tallest, stack);
        }
        return tallest;
    }

    private static void TargetLine(Canvas canvas, FontFile font, float x, float w, float lineY, float top, string label, Color32 color)
    {
        // Off the chart, or too close to the top to fit its label.
        if (lineY <= top + 14) return;
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(1f);
        canvas.BeginPath();
        canvas.MoveTo(x, lineY);
        canvas.LineTo(x + w, lineY);
        canvas.Stroke();
        canvas.DrawText(label, x + 6, lineY - 12, color, 9, font);
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

    private void DrawTable(Paper paper, FontFile font, float width, float height)
    {
        IReadOnlyList<FrameStat> categories = FrameStats.Categories;
        History.Totals totals = _history.Summarise(categories.Count, _visibleColumns);

        _rows.Clear();
        for (int i = 0; i < categories.Count; i++) _rows.Add((i, totals.AvgMs[i]));
        _rows.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));

        float fs = EditorTheme.FontSizeSmall;
        using (paper.Row("fs_hdr").Height(HeaderH).Padding(10, 18, 0, 0).Gap(8).Enter())
        {
            HeaderCell(paper, font, "fs_h_name", Loc.Get("frame_stats.col_category"), null, fs, TextAlignment.MiddleLeft, null);
            string nowKey = _hoveredAgo >= 0 ? "frame_stats.col_hovered" : SelectedAgo >= 0 ? "frame_stats.col_selected" : "frame_stats.col_now";
            HeaderCell(paper, font, "fs_h_now", Loc.Get(nowKey),
                Loc.Get("frame_stats.col_now_tip"), fs, TextAlignment.MiddleRight, ValueW);
            HeaderCell(paper, font, "fs_h_avg", Loc.Get("frame_stats.col_avg"), Loc.Get("frame_stats.col_avg_tip"), fs, TextAlignment.MiddleRight, ValueW);
            HeaderCell(paper, font, "fs_h_max", Loc.Get("frame_stats.col_max"), Loc.Get("frame_stats.col_max_tip"), fs, TextAlignment.MiddleRight, ValueW);
            HeaderCell(paper, font, "fs_h_calls", Loc.Get("frame_stats.col_calls"), Loc.Get("frame_stats.col_calls_tip"), fs, TextAlignment.MiddleRight, ValueW);
            HeaderCell(paper, font, "fs_h_share", "%", Loc.Get("frame_stats.col_share_tip"), fs, TextAlignment.MiddleRight, ValueW);
        }
        EditorGUI.Divider(paper, "fs_hdr_div");

        History.Column? shown = ShownColumn;
        Origami.ScrollView(paper, "fs_rows", width, Math.Max(0, height - HeaderH - 1)).Body(() =>
        {
            foreach ((int i, float avgMs) in _rows)
                DrawRow(paper, font, categories[i], i, avgMs, totals, shown);
        });
    }

    private void DrawRow(Paper paper, FontFile font, FrameStat c, int index, float avgMs, History.Totals totals, History.Column? shown)
    {
        FontFile mono = EditorTheme.FontMono ?? font;
        float fs = EditorTheme.FontSizeSmall;
        bool hidden = _hidden.Contains(c);
        Color nameInk = hidden ? EditorTheme.InkFaint : EditorTheme.Ink500;
        Color valueInk = hidden ? EditorTheme.InkFaint : EditorTheme.Ink400;
        float share = totals.AvgFrameMs > 0 ? avgMs / totals.AvgFrameMs * 100f : 0;
        string id = $"fs_r_{c.Name}";

        using (paper.Row(id).Height(RowH).Padding(10, 18, 0, 0).Gap(8)
            .BackgroundColor(_hovered == c ? EditorTheme.Hover : Color.Transparent)
            .OnClick(c, (cat, _) => { if (!_hidden.Remove(cat)) _hidden.Add(cat); })
            .OnHover(c, (cat, _) => _hovered = cat)
            .OnLeave(_ => { if (_hovered == c) _hovered = null; })
            .Tooltip(c.Name, CategoryTooltip(c))
            .Enter())
        {
            paper.Box($"{id}_sw").Width(10).Height(10).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .Rounded(2).IsNotInteractable()
                .BackgroundColor(hidden ? Color.Transparent : ToColor(c.Color))
                .BorderColor(ToColor(c.Color)).BorderWidth(1);

            paper.Box($"{id}_n").Height(RowH).Clip().IsNotInteractable()
                .Text(c.Name, font).TextColor(nameInk).FontSize(fs).Alignment(TextAlignment.MiddleLeft);

            ValueCell(paper, mono, $"{id}_now", Ms(shown?.Avg(index) ?? 0), valueInk, fs);
            ValueCell(paper, mono, $"{id}_a", Ms(avgMs), valueInk, fs);
            ValueCell(paper, mono, $"{id}_m", Ms(totals.MaxMs[index]), valueInk, fs);
            ValueCell(paper, mono, $"{id}_c", $"{shown?.Calls(index) ?? 0:0.#}", valueInk, fs);
            ValueCell(paper, mono, $"{id}_s", $"{share:0.0}%", valueInk, fs);
        }
    }

    // What each built-in category measures. Categories made by game code get only the hint.
    private static readonly Dictionary<FrameStat, string> CategoryKeys = new()
    {
        [FrameStats.Input] = "frame_stats.cat_input",
        [FrameStats.Async] = "frame_stats.cat_async",
        [FrameStats.Assets] = "frame_stats.cat_assets",
        [FrameStats.Update] = "frame_stats.cat_update",
        [FrameStats.Physics] = "frame_stats.cat_physics",
        [FrameStats.Animation] = "frame_stats.cat_animation",
        [FrameStats.Navigation] = "frame_stats.cat_navigation",
        [FrameStats.Audio] = "frame_stats.cat_audio",
        [FrameStats.Render] = "frame_stats.cat_render",
        [FrameStats.UI] = "frame_stats.cat_ui",
        [FrameStats.Editor] = "frame_stats.cat_editor",
        [FrameStats.RenderWait] = "frame_stats.cat_render_wait",
        [FrameStats.Idle] = "frame_stats.cat_idle",
        [FrameStats.GC] = "frame_stats.cat_gc",
        [FrameStats.Other] = "frame_stats.cat_other",
    };

    private static string CategoryTooltip(FrameStat category)
        => CategoryKeys.TryGetValue(category, out string? key)
            ? $"{Loc.Get(key)}\n\n{Loc.Get("frame_stats.row_tip")}"
            : Loc.Get("frame_stats.row_tip");

    // Headers and the AVG/MAX choice are shown in capitals. The translations stay in normal case, and the
    // locale's own rules uppercase them, so Turkish gets a dotted capital I.
    private static string Caps(string text)
    {
        try { return text.ToUpper(CultureInfo.GetCultureInfo(Loc.CurrentLocale)); }
        catch (CultureNotFoundException) { return text.ToUpperInvariant(); }
    }

    private static string Ms(float ms) => ms <= 0.005f ? "-" : $"{ms:0.00}";

    private static void HeaderCell(Paper paper, FontFile font, string id, string text, string? tooltip, float fs, TextAlignment align, float? width)
    {
        ElementBuilder box = paper.Box(id).Height(HeaderH)
            .Text(Caps(text), font).TextColor(EditorTheme.InkDim).FontSize(fs).Alignment(align);
        if (width.HasValue) box.Width(width.Value);
        // A tooltip needs hover, so only plain headers are made non-interactable.
        if (tooltip != null) box.Tooltip(tooltip);
        else box.IsNotInteractable();
    }

    private static void ValueCell(Paper paper, FontFile mono, string id, string text, Color color, float fs)
    {
        paper.Box(id).Width(ValueW).Height(RowH).IsNotInteractable()
            .Text(text, mono).TextColor(color).FontSize(fs).Alignment(TextAlignment.MiddleRight);
    }

    #endregion

    private static Color ToColor(VColor c) => Color.FromArgb(255, ToByte(c.R), ToByte(c.G), ToByte(c.B));
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

            // A category registered after the column was made has nothing in it.
            public float Avg(int category) => category < AvgMs.Length ? AvgMs[category] : 0;
            public float Max(int category) => category < MaxMs.Length ? MaxMs[category] : 0;
            public float Calls(int category) => category < AvgCalls.Length ? AvgCalls[category] : 0;
        }

        /// <summary>
        /// Every category's average and worst frame across the newest columns, weighted by frames. The arrays are
        /// reused, so they hold until the next <see cref="Summarise"/>.
        /// </summary>
        public readonly record struct Totals(float[] AvgMs, float[] MaxMs, float AvgFrameMs);

        private readonly Column?[] _columns;
        private long _committed;

        // The column being gathered.
        private float[] _sumMs = [], _maxMs = [], _sumCalls = [];
        private float _sumFrameMs, _peakMs;
        private int _pendingFrames;
        private long _pendingSince = Stopwatch.GetTimestamp();
        private long _framesSeen = FrameStats.FrameCount;
        private float[] _totalAvg = [], _totalMax = [];

        public History(int capacity) => _columns = new Column?[capacity];

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
                    if (!hidden.Contains(categories[i])) shownMs += s.Ms;
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
            DropPending();
        }

        /// <summary>Totals over the newest <paramref name="columns"/> columns.</summary>
        public Totals Summarise(int categoryCount, int columns)
        {
            if (_totalAvg.Length < categoryCount)
            {
                _totalAvg = new float[categoryCount];
                _totalMax = new float[categoryCount];
            }
            float[] avg = _totalAvg, max = _totalMax;
            Array.Clear(avg);
            Array.Clear(max);
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
                }
            }

            if (frames == 0) return new Totals(avg, max, 0);
            for (int i = 0; i < categoryCount; i++) avg[i] /= frames;
            return new Totals(avg, max, frameMs / frames);
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
            }
            column.Frames = _pendingFrames;
            column.AvgFrameMs = _sumFrameMs / n;
            column.PeakMs = _peakMs;
            for (int i = 0; i < categoryCount; i++)
            {
                column.AvgMs[i] = _sumMs[i] / n;
                column.MaxMs[i] = _maxMs[i];
                column.AvgCalls[i] = _sumCalls[i] / n;
            }

            _committed++;
            DropPending();
        }

        private void DropPending()
        {
            Array.Clear(_sumMs);
            Array.Clear(_maxMs);
            Array.Clear(_sumCalls);
            _sumFrameMs = _peakMs = 0;
            _pendingFrames = 0;
            _pendingSince = Stopwatch.GetTimestamp();
        }
    }
}
