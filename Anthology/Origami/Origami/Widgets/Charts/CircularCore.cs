// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>One slice of a <see cref="CircularCore{TSelf, T}"/> chart: a single item of the source data
/// set resolved into the label, value and colour the chart draws it with. <c>Index</c> stays the item's
/// position in the source list even after sorting, so colour functions and legend toggles keep pointing
/// at the same item; <c>Fraction</c> is the share of the visible total this slice sweeps.</summary>
public sealed class CircularSlice<T>
{
    public string Label = "";
    public double Value;
    public Color Color;
    public T? Payload;
    public int Index;
    public bool LegendHidden;
    public double Fraction;

    public bool Visible => !LegendHidden;
}

/// <summary>
/// Shared implementation for every circular chart type (Pie, Donut, Radar). Resolves the data set
/// into slices and draws the per-slice labels and hover tooltip around the geometry each type paints for
/// itself; the outer box, title, legend and data visibility all come from <see cref="ChartCore{TSelf, T}"/>.
/// </summary>
public abstract class CircularCore<TSelf, T> : ChartCore<TSelf, T> where TSelf : CircularCore<TSelf, T>
{
    private List<CircularSlice<T>>? _resolvedSlices;

    private Func<T, string>? _nameSelector;
    private Func<T, double>? _valueSelector;

    private bool _tooltip = true;
    private Func<double, string>? _valueFormatter;

    private Func<T, int, Color>? _colorFunction;

    private bool _labels = true;
    private Func<T, IComparable>? _sortKey;
    private bool _sortDescending;
    private bool _showPercent;
    private bool _showValues;

    protected CircularCore(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data = null)
        : base(paper, id, theme, data) { }

    private TSelf Self => (TSelf)this;

    // --- Data ---

    /// <summary>Label selector for the data set passed at construction. Names the slice in the legend,
    /// the per-slice labels and the tooltip header.</summary>
    public TSelf Name(Func<T, string> selector) { _nameSelector = selector; return Self; }

    /// <summary>Value selector for the data set passed at construction. Drives each slice's share of the
    /// circle; non-finite values count as zero.</summary>
    public TSelf Value(Func<T, double> selector) { _valueSelector = selector; return Self; }

    /// <summary>Colour selector, taking the item and its index in the source data set. Overrides the
    /// colour the chart would otherwise cycle out of the active variant's ramp.</summary>
    public TSelf ColorFunction(Func<T, int, Color> selector) { _colorFunction = selector; return Self; }

    // --- Tooltip / formatting ---

    /// <summary>Show a readout beside the pointer for the hovered slice. On by default: a wedge carries
    /// no axis to read its value off.</summary>
    public TSelf Tooltip(bool show = true) { _tooltip = show; return Self; }

    public TSelf ValueFormatter(Func<double, string> formatter) { _valueFormatter = formatter; return Self; }

    // --- Presentation ---

    public TSelf Labels(bool show = true) { _labels = show; return Self; }

    /// <summary>Order the slices around the circle by <paramref name="key"/> rather than by data order.
    /// Sorting is stable and does not change which item a colour function or legend toggle refers to.</summary>
    public TSelf SortBy(Func<T, IComparable> key, bool descending = false) { _sortKey = key; _sortDescending = descending; return Self; }

    public TSelf ShowPercent(bool show = true) { _showPercent = show; return Self; }
    public TSelf ShowValues(bool show = true) { _showValues = show; return Self; }

    // --- Slice resolution ---

    private List<CircularSlice<T>> ResolveSlices()
    {
        var slices = new List<CircularSlice<T>>();
        if (_data == null || _data.Count == 0) return slices;

        for (int i = 0; i < _data.Count; i++)
        {
            T item = _data[i];
            double value = _valueSelector != null ? _valueSelector(item) : 0d;
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0d;

            slices.Add(new CircularSlice<T>
            {
                Label = _nameSelector != null ? (_nameSelector(item) ?? "") : "",
                Value = value,
                Payload = item,
                Index = i,
            });
        }

        if (_sortKey != null)
            slices = _sortDescending
                ? slices.OrderByDescending(s => _sortKey(s.Payload!)).ToList()
                : slices.OrderBy(s => _sortKey(s.Payload!)).ToList();

        for (int ordinal = 0; ordinal < slices.Count; ordinal++)
        {
            CircularSlice<T> slice = slices[ordinal];
            slice.Color = _colorFunction != null ? _colorFunction(slice.Payload!, slice.Index) : DefaultSliceColor(ordinal);
            if (LegendListsSlices)
                slice.LegendHidden = IsLegendHidden(slice.Index);
        }

        double magnitude = 0d;
        foreach (CircularSlice<T> slice in slices)
            if (slice.Visible) magnitude += Math.Abs(slice.Value);

        foreach (CircularSlice<T> slice in slices)
            slice.Fraction = magnitude > 0d && slice.Visible ? Math.Abs(slice.Value) / magnitude : 0d;

        return slices;
    }

    /// <summary>Colour of the slice at <paramref name="ordinal"/> when no <see cref="ColorFunction"/> is
    /// set: the active variant's ramp walked in a high-contrast order and wrapped every seven slices.</summary>
    protected Color DefaultSliceColor(int ordinal)
    {
        OrigamiRamp ramp = Ramp;
        return (ordinal % 7) switch
        {
            0 => ramp.C500,
            1 => ramp.C300,
            2 => ramp.C700,
            3 => ramp.C400,
            4 => ramp.C600,
            5 => ramp.C200,
            _ => ramp.C100,
        };
    }

    private static double VisibleTotal(IReadOnlyList<CircularSlice<T>> slices)
    {
        double total = 0d;
        foreach (CircularSlice<T> slice in slices)
            if (slice.Visible) total += slice.Value;
        return total;
    }

    private static double VisibleMagnitude(IReadOnlyList<CircularSlice<T>> slices)
    {
        double total = 0d;
        foreach (CircularSlice<T> slice in slices)
            if (slice.Visible) total += Math.Abs(slice.Value);
        return total;
    }

    // --- Legend hooks ---

    /// <summary>Whether the legend lists the resolved slices, which is also what makes a legend toggle
    /// hide a slice. Radar overrides this to list its series instead once it has any.</summary>
    protected virtual bool LegendListsSlices => true;

    /// <summary>The legend rows for the current data. The default is one row per slice, labelled by the
    /// slice and keyed on its source index.</summary>
    protected virtual IReadOnlyList<LegendEntry> BuildLegend(IReadOnlyList<CircularSlice<T>> slices)
    {
        var entries = new List<LegendEntry>(slices.Count);
        foreach (CircularSlice<T> slice in slices)
        {
            string? valueText = LegendShowValueEnabled ? Format(slice.Value) : null;
            entries.Add(new LegendEntry(
                slice.Label.Length > 0 ? slice.Label : "Slice " + slice.Index,
                slice.Color, slice.Index, valueText, slice.LegendHidden));
        }
        return entries;
    }

    /// <summary>Whether the legend entry with this key is currently toggled off. Always false while the
    /// legend is not interactive. Exposed under this name for chart types (Radar) whose keys refer to
    /// something other than a slice.</summary>
    protected bool IsHidden(int key) => IsLegendHidden(key);

    /// <summary>Formats a value the way this chart's legend, labels and tooltip do, honouring
    /// <see cref="ValueFormatter"/>.</summary>
    protected string FormatValue(double v) => Format(v);

    private string Format(double v) => _valueFormatter != null ? _valueFormatter(v) : v.ToString("0.###");

    // --- Geometry context ---

    /// <summary>Geometry and resolved data handed to <see cref="PaintMarks"/>. The paint pass gets
    /// absolute canvas pixels; the passes that build Paper nodes inside the plot element get the same
    /// geometry with the plot's top-left corner at the origin.</summary>
    protected readonly struct CircularContext
    {
        public readonly float PlotL, PlotT, PlotR, PlotB;
        public readonly float CenterX, CenterY;

        /// <summary>Radius of the largest circle that fits in the plot, less this chart type's
        /// <see cref="CircularCore{TSelf, T}.RadiusInset"/>.</summary>
        public readonly float Radius;

        public readonly IReadOnlyList<CircularSlice<T>> Slices;

        /// <summary>Signed sum of the visible slices' values.</summary>
        public readonly double Total;

        /// <summary>Ordinal into <see cref="Slices"/> of the slice under the pointer, or -1.</summary>
        public readonly int HoverIndex;

        internal CircularContext(float plotL, float plotT, float plotR, float plotB, float radiusInset,
            IReadOnlyList<CircularSlice<T>> slices, double total, int hoverIndex)
        {
            PlotL = plotL; PlotT = plotT; PlotR = plotR; PlotB = plotB;
            CenterX = (plotL + plotR) * 0.5f;
            CenterY = (plotT + plotB) * 0.5f;
            Radius = MathF.Max(1f, 0.5f * MathF.Min(plotR - plotL, plotB - plotT) - radiusInset);
            Slices = slices;
            Total = total;
            HoverIndex = hoverIndex;
        }

        /// <summary>The point <paramref name="radius"/> pixels from the centre along
        /// <paramref name="angleRadians"/>, measured clockwise from the positive x axis.</summary>
        public Float2 PointAt(float angleRadians, float radius)
            => new Float2(CenterX + MathF.Cos(angleRadians) * radius, CenterY + MathF.Sin(angleRadians) * radius);

        /// <summary>Number of slices not hidden by the legend.</summary>
        public int VisibleCount
        {
            get
            {
                int n = 0;
                foreach (CircularSlice<T> slice in Slices)
                    if (slice.Visible) n++;
                return n;
            }
        }
    }

    // --- Type hooks ---

    /// <summary>Paint this chart type's geometry (wedges, rings, spokes, needle) into the already-clipped
    /// plot area described by <paramref name="ctx"/>.</summary>
    protected abstract void PaintMarks(Canvas canvas, in CircularContext ctx);

    /// <summary>Build any Paper nodes this chart type wants on top of its geometry, such as a gauge's
    /// central readout or a radar's tick labels. Called from inside the plot element, so everything laid
    /// out here must be self-directed.</summary>
    protected virtual void DrawOverlay(Paper paper, in CircularContext ctx) { }

    /// <summary>Ordinal of the slice at <paramref name="pointer"/>, or -1 for none. Only the chart type
    /// knows how it laid its slices out, so the core never resolves a hit on its own.</summary>
    protected virtual int HitTest(in CircularContext ctx, Float2 pointer) => -1;

    /// <summary>Where the per-slice label for <paramref name="ordinal"/> is centred. Defaults to the plot
    /// centre; every type that draws labels overrides this.</summary>
    protected virtual Float2 LabelAnchor(in CircularContext ctx, int ordinal) => new Float2(ctx.CenterX, ctx.CenterY);

    /// <summary>Whether per-slice labels mean anything on this chart type. Radar turns them off in favour
    /// of its own spoke labels.</summary>
    protected virtual bool LabelsEnabledForType => true;

    /// <summary>Pixels trimmed off the radius to leave room for whatever this chart type draws outside
    /// its circle, such as a radar's spoke labels.</summary>
    protected virtual float RadiusInset => 8f;

    /// <summary>Whether an all-zero data set counts as no data. Chart types whose values do not come from
    /// the slices themselves clear this so they still draw with zero-valued slices.</summary>
    protected virtual bool RequiresPositiveTotal => true;

    // --- Show ---

    /// <summary>This chart's slices, resolved and legend-filtered, built once per <c>Show()</c> by
    /// <see cref="BuildLegend"/> and consumed by <see cref="DrawPlot"/>.</summary>
    protected override IReadOnlyList<LegendEntry> BuildLegendEntries()
    {
        List<CircularSlice<T>> slices = ResolveSlices();
        _resolvedSlices = slices;
        return BuildLegend(slices);
    }

    protected override void DrawPlot()
    {
        List<CircularSlice<T>> slices = _resolvedSlices ?? ResolveSlices();
        DrawChartBox(slices);
    }

    private void DrawChartBox(List<CircularSlice<T>> slices)
    {
        if (slices.Count == 0 || (RequiresPositiveTotal && VisibleMagnitude(slices) <= 0d))
        {
            using (_paper.Row(_id + "_chart_empty_wrap").Enter())
                Origami.Label(_paper, _id + "_chart_empty", EmptyLabelText).LG().Show();

            return;
        }

        double total = VisibleTotal(slices);
        float inset = RadiusInset;

        ElementBuilder plotBox = _paper.Box(_id + "_chart_plot").Clip();

        using (plotBox.Enter())
        {
            ElementHandle plotEl = _paper.CurrentParent;

            if (_tooltip)
            {
                plotBox.OnHover(e =>
                {
                    _paper.SetElementStorage(plotEl, HoverPosKey, e.RelativePosition);
                    _paper.SetElementStorage(plotEl, HoverOnKey, true);
                });
                plotBox.OnLeave(_ => _paper.SetElementStorage(plotEl, HoverOnKey, false));
            }

            _paper.Draw((canvas, rect) =>
            {
                float ox = (float)rect.Min.X, oy = (float)rect.Min.Y;
                float w = (float)rect.Size.X, h = (float)rect.Size.Y;
                if (w < 4f || h < 4f) return;

                int hover = -1;
                if (_tooltip && _paper.GetElementStorage(plotEl, HoverOnKey, false))
                {
                    Float2 local = _paper.GetElementStorage(plotEl, HoverPosKey, new Float2(0f, 0f));
                    var probe = new CircularContext(ox, oy, ox + w, oy + h, inset, slices, total, -1);
                    hover = HitTest(in probe, new Float2(local.X + ox, local.Y + oy));
                }

                var ctx = new CircularContext(ox, oy, ox + w, oy + h, inset, slices, total, hover);
                PaintMarks(canvas, in ctx);

                _paper.SetElementStorage(plotEl, PlotKey, new PlotInfo { Width = w, Height = h });
                _paper.SetElementStorage(plotEl, HoverIdxKey, hover);
            });

            PlotInfo info = _paper.GetElementStorage<PlotInfo>(plotEl, PlotKey, default);
            if (info.Width <= 0f || info.Height <= 0f) return;

            int hoverIndex = _paper.GetElementStorage(plotEl, HoverIdxKey, -1);
            var plot = new CircularContext(0f, 0f, info.Width, info.Height, inset, slices, total, hoverIndex);

            if (_labels && LabelsEnabledForType)
                DrawLabels(in plot);

            DrawOverlay(_paper, in plot);

            if (_tooltip && hoverIndex >= 0 && hoverIndex < slices.Count)
                DrawTooltip(in plot, _paper.GetElementStorage(plotEl, HoverPosKey, new Float2(0f, 0f)), hoverIndex);
        }
    }

    private void DrawLabels(in CircularContext ctx)
    {
        for (int i = 0; i < ctx.Slices.Count; i++)
        {
            CircularSlice<T> slice = ctx.Slices[i];
            if (!slice.Visible || slice.Fraction < MinLabelFraction) continue;

            string text = slice.Label;
            if (_showValues) text += " " + Format(slice.Value);
            if (_showPercent) text += " (" + (slice.Fraction * 100d).ToString("0.#") + "%)";
            if (text.Length == 0) continue;

            Float2 anchor = LabelAnchor(in ctx, i);

            using (_paper.Box($"{_id}_slice_label_{i}")
                .PositionType(PositionType.SelfDirected)
                .Position(anchor.X - LabelBoxWidth * 0.5f, anchor.Y - LabelBoxHeight * 0.5f)
                .Size(LabelBoxWidth, LabelBoxHeight)
                .Enter())
            {
                Origami.Label(_paper, $"{_id}_slice_label_txt_{i}", text)
                    .XS()
                    .AlignCenter()
                    .Width(LabelBoxWidth)
                    .Height(LabelBoxHeight)
                    .Show();
            }
        }
    }

    private void DrawTooltip(in CircularContext ctx, Float2 anchor, int ordinal)
    {
        CircularSlice<T> slice = ctx.Slices[ordinal];

        var rows = new List<(Color Color, string Text)> { (slice.Color, Format(slice.Value)) };
        if (_showPercent)
            rows.Add((slice.Color, (slice.Fraction * 100d).ToString("0.#") + "%"));

        CircularTooltip(_paper, in ctx, anchor, slice.Label.Length > 0 ? slice.Label : "Slice " + slice.Index, rows);
    }

    /// <summary>Readout panel anchored beside <paramref name="anchor"/>, in the same plot-local pixels the
    /// overlay pass works in. It flips to the other side of the anchor when it would overhang the plot's
    /// right edge, using the width it laid out at on the previous frame.</summary>
    protected void CircularTooltip(Paper paper, in CircularContext ctx, Float2 anchor, string header,
        IReadOnlyList<(Color Color, string Text)> rows)
    {
        if (header.Length == 0 && rows.Count == 0) return;

        string widthKey = _id + "_tooltip_w";
        float lastWidth = paper.GetRootStorage<float>(widthKey);

        float x = anchor.X + TooltipGap;
        if (lastWidth > 0f && x + lastWidth > ctx.PlotR)
            x = MathF.Max(ctx.PlotL, anchor.X - TooltipGap - lastWidth);

        float y = Math.Clamp(anchor.Y + TooltipGap, ctx.PlotT, ctx.PlotB);

        ElementBuilder popup = paper.Column(_id + "_tooltip")
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Size(UnitValue.Auto)
            .BackgroundColor(_theme.Popover)
            .BorderColor(_theme.BorderStrong).BorderWidth(1f)
            .Rounded(6f)
            .Padding(6f)
            .ColBetween(6f)
            .Layer(Layer.Topmost + 1000)
            .OnPostLayout((_, rect) => paper.SetRootStorage(widthKey, (float)rect.Size.X));

        using (popup.Enter())
        {
            if (header.Length > 0)
                Origami.Label(paper, $"{_id}_tooltip_hdr", header)
                    .XS()
                    .AlignCenter()
                    .AlignLeft()
                    .Height(SwatchSize)
                    .Show();

            for (int i = 0; i < rows.Count; i++)
            {
                (Color color, string text) = rows[i];

                using (paper.Row($"{_id}_tooltip_row_{i}").Height(SwatchSize).Width(UnitValue.Auto).RowBetween(2f).Enter())
                {
                    paper.Box($"{_id}_tooltip_sw_{i}").Size(SwatchSize).BackgroundColor(color).Rounded(2f);

                    Origami.Label(paper, $"{_id}_tooltip_txt_{i}", text)
                        .XS()
                        .AlignCenter()
                        .AlignLeft()
                        .Height(SwatchSize)
                        .Show();
                }
            }
        }
    }

    // --- Canvas helpers ---

    protected const float DegToRad = MathF.PI / 180f;

    /// <summary>Fills the sector between <paramref name="a0"/> and <paramref name="a1"/>. An
    /// <paramref name="innerR"/> of zero or less gives a solid pie wedge; anything larger cuts the
    /// wedge's inner end out for a donut segment.</summary>
    protected static void PaintWedge(Canvas canvas, float cx, float cy, float innerR, float outerR,
        float a0, float a1, Color32 fill)
        => ChartGeometry.PaintWedge(canvas, cx, cy, innerR, outerR, a0, a1, fill);

    private struct PlotInfo
    {
        public float Width, Height;
    }

    private const string PlotKey = "circular_plot";
    private const string HoverPosKey = "circular_hover_pos";
    private const string HoverOnKey = "circular_hover_on";
    private const string HoverIdxKey = "circular_hover_idx";

    private const float SwatchSize = 12f;
    private const float TooltipGap = 8f;
    private const float LabelBoxWidth = 90f;
    private const float LabelBoxHeight = 14f;
    private const double MinLabelFraction = 0.02d;
}
