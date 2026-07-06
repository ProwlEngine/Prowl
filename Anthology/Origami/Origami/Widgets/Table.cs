// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>A single cell in a <see cref="TableRow"/>.</summary>
public sealed class TableCell
{
    internal string Text = "";
    internal Color Color;
    internal IOrigamiIcon? Icon;
    internal TextAlignment Align = TextAlignment.MiddleLeft;
}

/// <summary>A row of cells in a <see cref="TableBuilder"/>. Chain <see cref="Cell(string, Color)"/> calls.</summary>
public sealed class TableRow
{
    internal readonly List<TableCell> Cells = new();

    /// <summary>Append a text cell.</summary>
    public TableRow Cell(string text, Color color)
    {
        Cells.Add(new TableCell { Text = text, Color = color });
        return this;
    }

    /// <summary>Append a right-aligned text cell (e.g. numbers).</summary>
    public TableRow CellRight(string text, Color color)
    {
        Cells.Add(new TableCell { Text = text, Color = color, Align = TextAlignment.MiddleRight });
        return this;
    }

    /// <summary>Append a text cell with a leading vector icon (host paints into the slot rect).</summary>
    public TableRow Cell(string text, Color color, IOrigamiIcon icon)
    {
        Cells.Add(new TableCell { Text = text, Color = color, Icon = icon });
        return this;
    }
}

/// <summary>
/// Fluent builder for a data table. Declare columns with <see cref="Column"/>, add rows with
/// <see cref="Row"/>, then call <see cref="Show"/>. Construct via
/// <see cref="Origami.Table(Paper, string, int, Action{int})"/>.
/// </summary>
public sealed class TableBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly int _selected;
    private readonly Action<int> _onSelect;

    private readonly List<(string Header, float Flex, bool Sortable, TextAlignment Align)> _columns = new();
    private readonly List<TableRow> _rows = new();
    private UnitValue _width = UnitValue.Stretch();
    private bool _bordered = true;

    // Fixed-size scrolling body (for embedding in a panel / dialog).
    private float _scrollW, _scrollH;
    private bool _scroll;
    private bool _virtualize;

    // Sort state
    private int _sortColumn = -1;
    private bool _sortAscending = true;
    private Action<int>? _onSort;

    // Row interactions
    private Action<int>? _onActivate;
    private Action<int>? _onContext;

    // Rich-row extensions (multi-select / drag / ping / fully custom cell content). Opt-in; when unset
    // the table behaves as the simple single-select data grid.
    private bool _multiSelect;
    private Func<int, bool>? _isSelected;
    private Action<int, bool, bool>? _onSelectModified;  // (row, ctrl, shift)
    private Action<int>? _onRowDragStart;
    private Action<int, float>? _onRowHover;              // (row, normalizedY)
    private Func<int, bool>? _isPinged;
    private Func<float>? _pingAlpha;
    private int? _rowCount;                               // used with CellContent instead of _rows
    private Action<int, int>? _cellContent;              // (row, col) — host draws into the current cell

    private float _rowHeight = 30f;

    internal TableBuilder(Paper paper, string id, int selected, Action<int> onSelect, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _selected = selected;
        _onSelect = onSelect;
        _theme = theme;
        _rowHeight = _theme.Metrics.RowHeight;
    }

    /// <summary>Override table width (default Stretch).</summary>
    public TableBuilder Width(UnitValue width) { _width = width; return this; }

    /// <summary>Fixed pixel size with an internal vertical scroll for the rows (header stays pinned).</summary>
    public TableBuilder Scroll(float width, float height)
    {
        _scroll = true;
        _scrollW = MathF.Max(40f, width);
        _scrollH = MathF.Max(40f, height);
        _width = UnitValue.Pixels(_scrollW);
        return this;
    }

    /// <summary>Row height in pixels (default 30).</summary>
    public TableBuilder RowHeight(float h) { _rowHeight = MathF.Max(16f, h); return this; }

    /// <summary>
    /// Only build the rows currently scrolled into view (plus a small buffer), padding the rest with
    /// spacers so the scrollbar and total height stay exact. Rows are fixed-height, so the visible
    /// range is computed directly from the scroll offset. Requires <see cref="Scroll"/>; big win for
    /// long lists (hundreds+ rows). Row element IDs stay keyed to the data index, so selection,
    /// sort and context actions are unaffected.
    /// </summary>
    public TableBuilder Virtualize(bool virtualize = true) { _virtualize = virtualize; return this; }

    /// <summary>Draw the outer border + rounded corners (default true). Pass false to embed flush inside a panel.</summary>
    public TableBuilder Bordered(bool bordered) { _bordered = bordered; return this; }

    /// <summary>Add a column. <paramref name="flex"/> is a proportional weight; <paramref name="sortable"/> shows a sort caret.
    /// <paramref name="align"/> sets the header text alignment (match it to the cells' alignment).</summary>
    public TableBuilder Column(string header, float flex = 1f, bool sortable = false, TextAlignment align = TextAlignment.MiddleLeft)
    {
        _columns.Add((header, MathF.Max(0.01f, flex), sortable, align));
        return this;
    }

    /// <summary>
    /// Wire sort state: <paramref name="activeColumn"/> shows a directional caret (<paramref name="ascending"/>),
    /// and clicking any sortable header fires <paramref name="onSortColumn"/> with that column's index.
    /// </summary>
    public TableBuilder Sort(int activeColumn, bool ascending, Action<int> onSortColumn)
    {
        _sortColumn = activeColumn;
        _sortAscending = ascending;
        _onSort = onSortColumn;
        return this;
    }

    /// <summary>Fire when a row is double-clicked (e.g. open / navigate into it).</summary>
    public TableBuilder OnRowActivate(Action<int> onActivate) { _onActivate = onActivate; return this; }

    /// <summary>Fire when a row is right-clicked.</summary>
    public TableBuilder OnRowContext(Action<int> onContext) { _onContext = onContext; return this; }

    // ── Rich-row extensions ─────────────────────────────────────────────

    /// <summary>Enable multi-select: rows use <see cref="IsSelected"/> and clicks fire <see cref="OnSelectModified"/>.</summary>
    public TableBuilder MultiSelect(bool on = true) { _multiSelect = on; return this; }

    /// <summary>Predicate deciding whether a row renders selected (overrides the constructor's selected index).</summary>
    public TableBuilder IsSelected(Func<int, bool> predicate) { _isSelected = predicate; return this; }

    /// <summary>Click handler carrying the ctrl/shift modifiers, for range/toggle selection.</summary>
    public TableBuilder OnSelectModified(Action<int, bool, bool> handler) { _onSelectModified = handler; return this; }

    /// <summary>Start a drag from a row.</summary>
    public TableBuilder OnRowDragStart(Action<int> handler) { _onRowDragStart = handler; return this; }

    /// <summary>Hover callback (row index + normalized Y), e.g. for drop-target tracking.</summary>
    public TableBuilder OnRowHover(Action<int, float> handler) { _onRowHover = handler; return this; }

    /// <summary>Rows matching this predicate get a fading "ping" highlight (alpha from <see cref="PingAlpha"/>).</summary>
    public TableBuilder IsPinged(Func<int, bool> predicate) { _isPinged = predicate; return this; }

    /// <summary>Current ping alpha 0..1 (usually a shared animated value).</summary>
    public TableBuilder PingAlpha(Func<float> alpha) { _pingAlpha = alpha; return this; }

    /// <summary>
    /// Draw fully custom content per cell instead of using <c>.Row().Cell()</c> data. Set the number of
    /// rows with <see cref="RowCount"/>; for each row/column the callback is invoked inside the (already
    /// laid-out, column-flex-sized, clipped) cell so the host can draw carets, tags, badges, rename
    /// fields, etc. while the table owns the column widths, header, selection background and scrolling.
    /// </summary>
    public TableBuilder CellContent(Action<int, int> draw) { _cellContent = draw; return this; }

    /// <summary>Row count when using <see cref="CellContent"/>.</summary>
    public TableBuilder RowCount(int count) { _rowCount = Math.Max(0, count); return this; }

    /// <summary>Begin a new row. Chain <c>.Cell(...)</c> on the returned row.</summary>
    public TableRow Row()
    {
        var r = new TableRow();
        _rows.Add(r);
        return r;
    }

    /// <summary>Render the table.</summary>
    public void Show()
    {
        var m = _theme.Metrics;
        var font = _theme.Font;
        var headerFace = _theme.SemiBold ?? font;
        var ink = _theme.Ink;
        if (font == null || _columns.Count == 0) return;

        Color glassIn = _theme.Glass;
        Color bdSoft = _theme.BorderSoft;
        Color hover = _theme.Hover;
        Color accDim = _theme.Selected;
        var acc = _theme.Primary.C500;

        float headerFont = m.FontSizeSmall;
        float bodyFont = m.FontSize - 1f;
        const float hHead = 26f;
        float hRow = _rowHeight;

        var container = _paper.Column(_id)
            .Width(_width)
            .Height(_scroll ? UnitValue.Pixels(_scrollH) : UnitValue.Auto)
            .Clip();
        if (_bordered)
            container.Rounded(9f).BorderColor(bdSoft).BorderWidth(1);

        using (container.Enter())
        {
            // ── Header row ──
            var headRow = _paper.Row($"{_id}_head")
                .Width(UnitValue.Stretch()).Height(hHead)
                .BackgroundColor(glassIn);
            if (_bordered) headRow.RoundedTop(9f);
            using (headRow.Enter())
            {
                for (int c = 0; c < _columns.Count; c++)
                {
                    var col = _columns[c];
                    int ci = c;
                    bool active = _sortColumn == c;
                    bool right = col.Align == TextAlignment.MiddleRight;
                    var hcell = _paper.Row($"{_id}_h{c}")
                        .Width(UnitValue.Stretch(col.Flex)).Height(hHead)
                        .Padding(10, 10, 0, 0);
                    if (col.Sortable && _onSort != null)
                    {
                        hcell.Hovered.BackgroundColor(hover).End();
                        hcell.OnClick(ci, (k, _) => _onSort!(k));
                    }

                    using (hcell.Enter())
                    {
                        if (right)
                            _paper.Box($"{_id}_hs{c}").Width(UnitValue.Stretch()).Height(hHead).IsNotInteractable();

                        _paper.Box($"{_id}_ht{c}")
                            .Width(UnitValue.Auto).Height(hHead)
                            .IsNotInteractable()
                            .Text(col.Header.ToUpperInvariant(), headerFace)
                            .TextColor(active ? _theme.Primary.C700 : ink.C200)
                            .FontSize(headerFont)
                            .Alignment(TextAlignment.MiddleLeft);

                        if (col.Sortable)
                        {
                            Color caret = active ? _theme.Primary.C700 : ink.C200;
                            bool up = active && _sortAscending;
                            bool dim = !active;
                            _paper.Box($"{_id}_hc{c}")
                                .Width(12).Height(hHead)
                                .IsNotInteractable()
                                .OnPostLayout((h2, r2) => _paper.Draw(ref h2, (canvas, rr) =>
                                {
                                    float cx = (float)(rr.Min.X + rr.Size.X / 2);
                                    float cy = (float)(rr.Min.Y + rr.Size.Y / 2);
                                    canvas.SaveState();
                                    canvas.SetStrokeColor(dim ? Color.FromArgb(90, caret.R, caret.G, caret.B) : caret);
                                    canvas.SetStrokeWidth(1.3f);
                                    canvas.SetStrokeCap(EndCapStyle.Round);
                                    canvas.SetStrokeJoint(JointStyle.Round);
                                    canvas.BeginPath();
                                    if (up) { canvas.MoveTo(cx - 3f, cy + 1.5f); canvas.LineTo(cx, cy - 1.8f); canvas.LineTo(cx + 3f, cy + 1.5f); }
                                    else { canvas.MoveTo(cx - 3f, cy - 1.5f); canvas.LineTo(cx, cy + 1.8f); canvas.LineTo(cx + 3f, cy - 1.5f); }
                                    canvas.Stroke();
                                    canvas.RestoreState();
                                }));
                        }
                    }
                }
            }

            _paper.Box($"{_id}_hdiv").Width(UnitValue.Stretch()).Height(1).BackgroundColor(bdSoft).IsNotInteractable();

            // ── Body rows (optionally inside a scroll view) ──
            if (_scroll)
            {
                float innerW = _bordered ? _scrollW - 2f : _scrollW;
                float bodyH = _scrollH - hHead - 1f;
                if (_virtualize)
                    // Snap-scroll: the visible window is computed from the target offset, so easing the
                    // content toward it would leave rows mismatched (blank gaps) mid-animation.
                    Origami.ScrollView(_paper, $"{_id}_sv", innerW, bodyH).SmoothScroll(false).Body(DrawRowsVirtual);
                else
                    Origami.ScrollView(_paper, $"{_id}_sv", innerW, bodyH).Body(DrawRows);
            }
            else
            {
                DrawRows();
            }

            int RowN() => _cellContent != null ? (_rowCount ?? 0) : _rows.Count;

            void DrawRows()
            {
                int n = RowN();
                for (int i = 0; i < n; i++)
                    DrawRow(i);
            }

            // Render only the rows intersecting the viewport, padding above/below with spacers so the
            // content height (and thus the scrollbar) matches a fully-populated table. Rows are a fixed
            // hRow tall with a 1px divider between them, so the visible range follows straight from the
            // scroll offset (stride = hRow + 1). The last row has no trailing divider, so the bottom
            // spacer drops that 1px to keep the total exact.
            void DrawRowsVirtual(ScrollViewport vp)
            {
                int n = RowN();
                if (n == 0) return;

                float stride = hRow + 1f;
                int first = Math.Max(0, (int)MathF.Floor(vp.ScrollY / stride));
                int span = (int)MathF.Ceiling(vp.Height / stride) + 1; // +1 covers a partial row at the bottom
                int last = Math.Min(n - 1, first + span);

                float topH = first * stride;
                if (topH > 0f)
                    _paper.Box($"{_id}_vtop").Width(UnitValue.Stretch()).Height(topH).IsNotInteractable();

                for (int i = first; i <= last; i++)
                    DrawRow(i);

                float botH = last >= n - 1 ? 0f : (n - 1 - last) * stride - 1f;
                if (botH > 0f)
                    _paper.Box($"{_id}_vbot").Width(UnitValue.Stretch()).Height(botH).IsNotInteractable();
            }

            void DrawRow(int i)
            {
                TableRow? row = _cellContent == null && i < _rows.Count ? _rows[i] : null;
                int idx = i;
                bool selected = _isSelected != null ? _isSelected(i) : i == _selected;
                bool isLast = i == RowN() - 1;

                var rowBuilder = _paper.Row($"{_id}_r{i}")
                    .Width(UnitValue.Stretch()).Height(hRow)
                    .BackgroundColor(selected ? accDim : Color.Transparent)
                    .Hovered.BackgroundColor(selected ? accDim : hover).End();

                if (_multiSelect && _onSelectModified != null)
                    rowBuilder.OnClick(idx, (ci, e) =>
                    {
                        e.StopPropagation();  // don't let the click bubble to a container that clears selection
                        bool ctrl = _paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl);
                        bool shift = _paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift);
                        _onSelectModified!(ci, ctrl, shift);
                    });
                else
                    rowBuilder.OnClick(idx, (ci, e) => { e.StopPropagation(); _onSelect(ci); });

                if (_onActivate != null) rowBuilder.OnDoubleClick(idx, (ci, _) => _onActivate!(ci));
                if (_onContext != null) rowBuilder.OnRightClick(idx, (ci, _) => _onContext!(ci));
                if (_onRowDragStart != null) rowBuilder.OnDragStart(idx, (ci, _) => _onRowDragStart!(ci));
                if (_onRowHover != null) rowBuilder.OnHover(idx, (ci, e) => _onRowHover!(ci, (float)e.NormalizedPosition.Y));

                bool pinged = _isPinged != null && _isPinged(i);
                if (selected || pinged)
                {
                    rowBuilder.OnPostLayout((h2, r2) => _paper.Draw(ref h2, (canvas, rr) =>
                    {
                        if (selected)
                            canvas.RectFilled((float)rr.Min.X, (float)rr.Min.Y, 2f, (float)rr.Size.Y,
                                Color32.FromArgb(255, (byte)acc.R, (byte)acc.G, (byte)acc.B));
                        if (pinged)
                        {
                            float pa = _pingAlpha?.Invoke() ?? 0f;
                            if (pa > 0f)
                                canvas.RectFilled((float)rr.Min.X, (float)rr.Min.Y, (float)rr.Size.X, (float)rr.Size.Y,
                                    Color32.FromArgb((int)(pa * 60), 255, 220, 50));
                        }
                    }));
                }

                using (rowBuilder.Enter())
                {
                    for (int c = 0; c < _columns.Count; c++)
                    {
                        var col = _columns[c];

                        using (_paper.Row($"{_id}_r{i}c{c}")
                            .Width(UnitValue.Stretch(col.Flex)).Height(hRow)
                            .Padding(10, 10, 0, 0)
                            .Clip()
                            .Enter())
                        {
                            if (_cellContent != null) { _cellContent(i, c); continue; }

                            TableCell? cell = row != null && c < row.Cells.Count ? row.Cells[c] : null;
                            if (cell == null) continue;

                            if (cell.Icon != null)
                            {
                                var icon = cell.Icon;
                                var iconCol = cell.Color;
                                float isz = bodyFont;
                                _paper.Box($"{_id}_r{i}c{c}i")
                                    .Width(19).Height(hRow)
                                    .IsNotInteractable()
                                    .OnPostLayout((h2, r2) => _paper.Draw(ref h2, (canvas, rr) =>
                                    {
                                        float ix = (float)(rr.Min.X + (rr.Size.X - isz) * 0.5f);
                                        float iy = (float)(rr.Min.Y + (rr.Size.Y - isz) * 0.5f);
                                        icon.Draw(canvas, new Prowl.Vector.Rect(
                                            new Prowl.Vector.Float2(ix, iy),
                                            new Prowl.Vector.Float2(ix + isz, iy + isz)), iconCol);
                                    }));
                            }

                            _paper.Box($"{_id}_r{i}c{c}t")
                                .Width(UnitValue.Stretch()).Height(hRow)
                                .IsNotInteractable()
                                .Text(cell.Text, font)
                                .TextColor(cell.Color)
                                .FontSize(bodyFont)
                                .Alignment(cell.Align);
                        }
                    }
                }

                if (!isLast)
                    _paper.Box($"{_id}_rd{i}").Width(UnitValue.Stretch()).Height(1).BackgroundColor(bdSoft).IsNotInteractable();
            }
        }
    }
}
