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

/// <summary>One row of a circular chart's legend. <c>Key</c> is the element-storage key the hide toggle
/// writes, and is a source data index for slice legends or a series index for the types whose legend
/// lists series instead.</summary>
public readonly struct CircularLegendEntry
{
    public readonly string Label;
    public readonly Color Color;
    public readonly double Value;
    public readonly bool HasValue;
    public readonly int Key;
    public readonly bool Hidden;

    public CircularLegendEntry(string label, Color color, double value, bool hasValue, int key, bool hidden)
    {
        Label = label ?? "";
        Color = color;
        Value = value;
        HasValue = hasValue;
        Key = key;
        Hidden = hidden;
    }
}

/// <summary>
/// Shared implementation for every circular chart type (Pie, Donut, Radar, Gauge). Resolves the data set
/// into slices, and draws the title, legend, per-slice labels and hover tooltip around the geometry each
/// type paints for itself.
/// </summary>
public abstract class CircularCore<TSelf, T> where TSelf : CircularCore<TSelf, T>
{
    protected readonly Paper _paper;
    protected readonly string _id;
    protected readonly OrigamiTheme _theme;
    protected readonly IReadOnlyList<T>? _data;

    private string _title = "";

    private UnitValue _width = UnitValue.Stretch();
    private float _height = 220f;
    private float _padding = 0f;

    private OrigamiVariant _variant = OrigamiVariant.Primary;
    private Color? _backgroundColor;
    private string _emptyLabel = "No data";

    private Func<T, string>? _nameSelector;
    private Func<T, double>? _valueSelector;

    private bool _legend = true;
    private bool _legendShowValue = true;
    private float? _legendFontSize;
    private bool _legendInteractive;

    private bool _tooltip = true;
    private Func<double, string>? _valueFormatter;

    private Func<T, int, Color>? _colorFunction;

    private bool _labels = true;
    private Func<T, IComparable>? _sortKey;
    private bool _sortDescending;
    private bool _showPercent;
    private bool _showValues;

    private ElementHandle _containerEl;

    protected CircularCore(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data = null)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _data = data;
    }

    private TSelf Self => (TSelf)this;

    // --- Chrome ---

    public TSelf Title(string text) { _title = text ?? ""; return Self; }

    public TSelf Width(float width) { _width = MathF.Max(32f, width); return Self; }
    public TSelf Width(UnitValue width) { _width = width; return Self; }
    public TSelf Height(float height) { _height = MathF.Max(32f, height); return Self; }
    public TSelf Size(float width, float height) { _width = MathF.Max(32f, width); _height = MathF.Max(32f, height); return Self; }
    public TSelf Padding(float padding) { _padding = MathF.Max(0f, padding); return Self; }

    public TSelf Variant(OrigamiVariant v) { _variant = v; return Self; }
    public TSelf Primary() => Variant(OrigamiVariant.Primary);
    public TSelf Success() => Variant(OrigamiVariant.Success);
    public TSelf Warning() => Variant(OrigamiVariant.Warning);
    public TSelf Danger() => Variant(OrigamiVariant.Danger);
    public TSelf Info() => Variant(OrigamiVariant.Info);

    public TSelf BackgroundColor(Color color) { _backgroundColor = color; return Self; }
    public TSelf EmptyLabel(string text) { _emptyLabel = text ?? "No data"; return Self; }

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

    // --- Legend ---

    public TSelf Legend(bool show = true) { _legend = show; return Self; }
    public TSelf LegendShowValue(bool show = true) { _legendShowValue = show; return Self; }

    /// <summary>Font size, in pixels, of the legend's labels. Unset uses the same XS size the rest of the
    /// chart chrome does.</summary>
    public TSelf LegendFontSize(float size) { _legendFontSize = MathF.Max(1f, size); return Self; }

    /// <summary>Let a click on a legend swatch hide and show that entry. Also gated globally by
    /// <see cref="Origami.LegendSelectionEnabled"/>.</summary>
    public TSelf LegendInteractive(bool interactive) { _legendInteractive = interactive; return Self; }

    private bool LegendInteractiveActive => _legendInteractive && Origami.LegendSelectionEnabled;

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

    private List<CircularSlice<T>> ResolveSlices(ElementHandle el)
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

        bool interactiveSlices = LegendInteractiveActive && LegendListsSlices;

        for (int ordinal = 0; ordinal < slices.Count; ordinal++)
        {
            CircularSlice<T> slice = slices[ordinal];
            slice.Color = _colorFunction != null ? _colorFunction(slice.Payload!, slice.Index) : DefaultSliceColor(ordinal);
            if (interactiveSlices)
                slice.LegendHidden = _paper.GetElementStorage(el, HiddenKeyPrefix + slice.Index, false);
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

    /// <summary>The active variant's colour ramp, which is where slice colours and a chart type's own
    /// accent colour come from.</summary>
    protected OrigamiRamp Ramp => _theme.Get(_variant);

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
    protected virtual IReadOnlyList<CircularLegendEntry> BuildLegend(IReadOnlyList<CircularSlice<T>> slices)
    {
        var entries = new List<CircularLegendEntry>(slices.Count);
        foreach (CircularSlice<T> slice in slices)
            entries.Add(new CircularLegendEntry(
                slice.Label.Length > 0 ? slice.Label : "Slice " + slice.Index,
                slice.Color, slice.Value, _legendShowValue, slice.Index, slice.LegendHidden));
        return entries;
    }

    /// <summary>Whether the legend entry with this key is currently toggled off. Always false while the
    /// legend is not interactive.</summary>
    protected bool IsHidden(int key)
    {
        if (!LegendInteractiveActive || !_containerEl.IsValid) return false;
        return _paper.GetElementStorage(_containerEl, HiddenKeyPrefix + key, false);
    }

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

    /// <summary>Whether per-slice labels mean anything on this chart type. Gauge turns them off; its
    /// readout is a single number rather than one label per slice.</summary>
    protected virtual bool LabelsEnabledForType => true;

    /// <summary>Pixels trimmed off the radius to leave room for whatever this chart type draws outside
    /// its circle, such as a radar's spoke labels.</summary>
    protected virtual float RadiusInset => 8f;

    /// <summary>Whether an all-zero data set counts as no data. Chart types whose values do not come from
    /// the slices themselves clear this so they still draw with zero-valued slices.</summary>
    protected virtual bool RequiresPositiveTotal => true;

    /// <summary>Called once at the top of <see cref="Show"/>, before any data is resolved.</summary>
    protected virtual void OnBeforeShow() { }

    // --- Show ---

    public void Show()
    {
        OnBeforeShow();

        ElementBuilder container = _paper.Column(_id)
            .Size(_width, _height)
            .Rounded(_theme.Metrics.ContainerRounding)
            .BorderColor(_theme.BorderSoft).BorderWidth(1f)
            .Padding(_padding);

        if (_backgroundColor.HasValue)
            container.BackgroundColor(_backgroundColor.Value);

        using (container.Enter())
        {
            _containerEl = _paper.CurrentParent;

            using (_paper.Row(_id + "_chart_header").Height(20).ChildRight().Enter())
                Origami.Label(_paper, _id + "_chart_header", _title).MD().Height(15).AlignCenter().Show();

            _paper.Box(_id + "_chart_header_div").Height(1).BackgroundColor(_theme.BorderStrong);

            using (_paper.Row(_id + "_chart_col_split").Enter())
            {
                List<CircularSlice<T>> slices = ResolveSlices(_containerEl);
                IReadOnlyList<CircularLegendEntry> entries = BuildLegend(slices);

                if (_legend && entries.Count > 0)
                    DrawLegend(entries);

                _paper.Box(_id + "_chart_split_div").Width(1).BackgroundColor(_theme.BorderStrong);

                DrawChartBox(slices);
            }
        }
    }

    private void DrawLegend(IReadOnlyList<CircularLegendEntry> entries)
    {
        ElementBuilder legendCol = _paper.Column(_id + "_legend")
            .Width(125f)
            .PaddingTop(_padding)
            .ColBetween(_padding);

        using (legendCol.Enter())
        {
            for (int i = 0; i < entries.Count; i++)
            {
                CircularLegendEntry entry = entries[i];
                int key = entry.Key;

                string text = entry.HasValue ? entry.Label + "  " + Format(entry.Value) : entry.Label;
                Color swatchColor = entry.Hidden ? Color.FromArgb(entry.Color.A / 3, entry.Color) : entry.Color;

                using (_paper.Row($"{_id}_legend_row_{i}").Height(SwatchSize).RowBetween(2f).Enter())
                {
                    ElementBuilder swatch = _paper.Box($"{_id}_legend_sw_{i}").Size(SwatchSize).BackgroundColor(swatchColor).Rounded(2f);

                    if (LegendInteractiveActive)
                        swatch.OnClick(_ => Toggle(key));

                    LabelBuilder label = Origami.Label(_paper, $"{_id}_legend_txt_{i}", text);
                    if (_legendFontSize.HasValue) label.FontSize(_legendFontSize.Value); else label.XS();

                    label.AlignCenter().AlignLeft().Height(SwatchSize).Show();
                }
            }
        }
    }

    private void Toggle(int key)
    {
        if (!_containerEl.IsValid) return;
        bool hidden = !_paper.GetElementStorage(_containerEl, HiddenKeyPrefix + key, false);
        _paper.SetElementStorage(_containerEl, HiddenKeyPrefix + key, hidden);
    }

    private void DrawChartBox(List<CircularSlice<T>> slices)
    {
        if (slices.Count == 0 || (RequiresPositiveTotal && VisibleMagnitude(slices) <= 0d))
        {
            using (_paper.Row(_id + "_chart_empty_wrap").Enter())
                Origami.Label(_paper, _id + "_chart_empty", _emptyLabel).LG().Show();

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

    protected static Color32 ToC32(Color c) => new Color32(c.R, c.G, c.B, c.A);
    protected static Color32 ToC32(Color c, float alpha) => new Color32(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0f, 255f));

    /// <summary>Fills the sector between <paramref name="a0"/> and <paramref name="a1"/>. An
    /// <paramref name="innerR"/> of zero or less gives a solid pie wedge; anything larger cuts the
    /// wedge's inner end out for a donut segment.</summary>
    protected static void PaintWedge(Canvas canvas, float cx, float cy, float innerR, float outerR,
        float a0, float a1, Color32 fill)
    {
        if (outerR <= 0f || MathF.Abs(a1 - a0) < 1e-5f) return;

        if (innerR <= 0f)
        {
            canvas.Pie(cx, cy, outerR, a0, a1);
            canvas.SetFillColor(fill);
            canvas.Fill();
            return;
        }

        int segments = ArcSegments(outerR, a1 - a0);

        canvas.BeginPath();
        for (int i = 0; i <= segments; i++)
        {
            float a = a0 + (a1 - a0) * i / segments;
            float x = cx + MathF.Cos(a) * outerR, y = cy + MathF.Sin(a) * outerR;
            if (i == 0) canvas.MoveTo(x, y); else canvas.LineTo(x, y);
        }
        for (int i = segments; i >= 0; i--)
        {
            float a = a0 + (a1 - a0) * i / segments;
            canvas.LineTo(cx + MathF.Cos(a) * innerR, cy + MathF.Sin(a) * innerR);
        }
        canvas.ClosePath();

        canvas.SetFillColor(fill);
        canvas.FillComplexAA();
    }

    /// <summary>Strokes the arc between <paramref name="a0"/> and <paramref name="a1"/> at
    /// <paramref name="width"/> pixels, which is how a track or ring segment is drawn.</summary>
    protected static void PaintRing(Canvas canvas, float cx, float cy, float radius,
        float a0, float a1, float width, Color32 stroke)
    {
        if (radius <= 0f || width <= 0f || MathF.Abs(a1 - a0) < 1e-5f) return;

        canvas.BeginPath();
        canvas.Arc(cx, cy, radius, a0, a1);
        canvas.SetStrokeColor(stroke);
        canvas.SetStrokeWidth(width);
        canvas.Stroke();
    }

    private static int ArcSegments(float radius, float sweep)
        => Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweep) * MathF.Max(radius, 1f) / 4f), 3, 240);

    private struct PlotInfo
    {
        public float Width, Height;
    }

    private const string HiddenKeyPrefix = "circular_hidden_";
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
