// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

using Prowl.OrigamiUI;

namespace Prowl.OrigamiUI.Charts;

/// <summary>Shared chrome for every chart: outer box, title, legend, value formatting, tooltip popup and
/// zoom/pan view state. A subtype supplies its legend rows and draws its plot.</summary>
public abstract class ChartCore<TSelf, T> where TSelf : ChartCore<TSelf, T>
{
    protected readonly Paper _paper;
    protected readonly string _id;
    protected readonly OrigamiTheme _theme;
    protected readonly IReadOnlyList<T>? _data;

    private string _title = "";
    private UnitValue _width = UnitValue.Stretch();
    private UnitValue _height = UnitValue.Pixels(220f);
    private float _padding;
    private OrigamiVariant _variant = OrigamiVariant.Primary;
    private string _emptyLabel = "No data";
    private Color? _backgroundColor;
    private Func<double, string>? _valueFormatter;

    private bool _legend = true;
    private bool _legendShowValue = true;
    private float? _legendFontSize;
    private bool _legendInteractive;

    private ElementHandle _containerEl;

    private const string HiddenKeyPrefix = "chart_hidden_";
    private const string ViewKey = "chart_view";
    private const string ViewActiveKey = "chart_view_active";
    private const string ViewSizeKey = "chart_view_size";
    private const string PointerKey = "chart_pointer";
    private const string PointerOnKey = "chart_pointer_on";
    private const float LegendWidth = 125f;
    private const float SwatchSize = 12f;
    private const float PopupGap = 8f;
    private const float MinViewSpan = 0.005f;
    private const float ZoomRate = 0.14f;

    protected ChartCore(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data = null)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _data = data;
    }

    private TSelf Self => (TSelf)this;

    /// <summary>Header text. The header row is hidden when empty.</summary>
    public TSelf Title(string text) { _title = text ?? ""; return Self; }

    public TSelf Width(UnitValue width) { _width = width; return Self; }
    public TSelf Height(UnitValue height) { _height = height; return Self; }
    public TSelf Size(UnitValue width, UnitValue height) { _width = width; _height = height; return Self; }
    public TSelf Padding(float padding) { _padding = MathF.Max(0f, padding); return Self; }

    public TSelf Variant(OrigamiVariant v) { _variant = v; return Self; }
    public TSelf Primary() => Variant(OrigamiVariant.Primary);
    public TSelf Success() => Variant(OrigamiVariant.Success);
    public TSelf Warning() => Variant(OrigamiVariant.Warning);
    public TSelf Danger() => Variant(OrigamiVariant.Danger);
    public TSelf Info() => Variant(OrigamiVariant.Info);

    public TSelf EmptyLabel(string text) { _emptyLabel = text ?? "No data"; return Self; }
    public TSelf BackgroundColor(Color color) { _backgroundColor = color; return Self; }
    public TSelf ValueFormatter(Func<double, string> formatter) { _valueFormatter = formatter; return Self; }

    public TSelf Legend(bool show = true) { _legend = show; return Self; }
    public TSelf LegendShowValue(bool show = true) { _legendShowValue = show; return Self; }
    public TSelf LegendFontSize(float size) { _legendFontSize = MathF.Max(1f, size); return Self; }

    /// <summary>Let a click on a legend swatch hide and show what it represents. Also gated globally by
    /// <see cref="Origami.LegendSelectionEnabled"/>.</summary>
    public TSelf LegendInteractive(bool interactive) { _legendInteractive = interactive; return Self; }

    protected ElementHandle ContainerEl => _containerEl;
    protected OrigamiRamp Ramp => _theme.Get(_variant);
    protected float PaddingValue => _padding;
    protected bool LegendShowValueEnabled => _legendShowValue;
    protected bool LegendInteractiveActive => _legendInteractive && Origami.LegendSelectionEnabled;

    protected bool IsLegendHidden(int key)
        => LegendInteractiveActive && _containerEl.IsValid && _paper.GetElementStorage(_containerEl, HiddenKeyPrefix + key, false);

    private void ToggleLegend(int key)
    {
        if (!_containerEl.IsValid) return;
        bool hidden = !_paper.GetElementStorage(_containerEl, HiddenKeyPrefix + key, false);
        _paper.SetElementStorage(_containerEl, HiddenKeyPrefix + key, hidden);
    }

    protected string FormatValue(double v) => _valueFormatter != null ? _valueFormatter(v) : v.ToString("0.###");

    protected Color RampColor(int ordinal, int depth = 0)
    {
        int stop = (ordinal % 7) switch { 0 => 5, 1 => 3, 2 => 7, 3 => 4, 4 => 6, 5 => 2, _ => 1 };
        stop = ((stop - 1 + Math.Max(0, depth)) % 7) + 1;

        OrigamiRamp ramp = Ramp;
        return stop switch
        {
            1 => ramp.C100,
            2 => ramp.C200,
            3 => ramp.C300,
            4 => ramp.C400,
            5 => ramp.C500,
            6 => ramp.C600,
            _ => ramp.C700,
        };
    }

    protected static Color32 ToC32(Color c, float alpha = 1f)
        => new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0f, 255f));

    protected static bool IsFinite(double v) => double.IsFinite(v);

    protected virtual void OnBeforeShow() { }

    protected abstract IReadOnlyList<LegendEntry> BuildLegendEntries();

    protected abstract void DrawPlot();

    public void Show()
    {
        OnBeforeShow();

        ElementBuilder container = _paper.Column(_id)
            .Size(_width, _height)
            .Rounded(_theme.Metrics.ContainerRounding)
            .BorderColor(_theme.BorderSoft).BorderWidth(1f)
            .Padding(_padding);

        if (_backgroundColor.HasValue) container.BackgroundColor(_backgroundColor.Value);

        using (container.Enter())
        {
            _containerEl = _paper.CurrentParent;

            if (_title.Length > 0)
            {
                using (_paper.Row(_id + "_chart_header").Height(20).Enter())
                    Origami.Label(_paper, _id + "_chart_header", _title).MD().Height(15).AlignCenter().Show();

                _paper.Box(_id + "_chart_header_div").Height(1).BackgroundColor(_theme.BorderStrong);
            }

            using (_paper.Row(_id + "_chart_col_split").Enter())
            {
                IReadOnlyList<LegendEntry> entries = BuildLegendEntries();

                if (_legend && entries.Count > 0)
                {
                    LegendBuilder legend = Origami.Legend(_paper, _id + "_legend", entries)
                        .Width(LegendWidth)
                        .SwatchSize(SwatchSize)
                        .Padding(_padding)
                        .RowGap(_padding)
                        .Interactive(_legendInteractive)
                        .OnToggle(ToggleLegend);

                    if (_legendFontSize.HasValue) legend.FontSize(_legendFontSize.Value);

                    legend.Show();

                    _paper.Box(_id + "_chart_split_div").Width(1).BackgroundColor(_theme.BorderStrong);
                }

                DrawPlot();
            }
        }
    }

    protected void DrawEmpty()
    {
        using (_paper.Row(_id + "_chart_empty_wrap").Enter())
            Origami.Label(_paper, _id + "_chart_empty", _emptyLabel).LG().Show();
    }

    protected float TextSize => _theme.Metrics.FontSize - 2f;

    protected Float2 MeasureText(Canvas canvas, string text)
        => _theme.Font == null || text.Length == 0 ? new Float2(0f, 0f) : canvas.MeasureText(text, TextSize, _theme.Font);

    protected void DrawText(Canvas canvas, string text, float x, float y, float originX, float originY, Color? color = null)
    {
        if (_theme.Font == null || text.Length == 0) return;
        canvas.DrawText(text, x, y, ToC32(color ?? _theme.Ink.C400), TextSize, _theme.Font, 0f, new Float2(originX, originY));
    }

    protected string FitText(Canvas canvas, string text, float maxWidth)
    {
        if (MeasureText(canvas, text).X <= maxWidth) return text;

        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (MeasureText(canvas, text[..mid] + "...").X <= maxWidth) lo = mid;
            else hi = mid - 1;
        }
        return lo == 0 ? "" : text[..lo] + "...";
    }

    protected void TrackPointer(ElementBuilder plotBox, ElementHandle plotEl)
    {
        plotBox.OnHover(e =>
        {
            _paper.SetElementStorage(plotEl, PointerKey, e.RelativePosition);
            _paper.SetElementStorage(plotEl, PointerOnKey, true);
        });
        plotBox.OnLeave(_ => _paper.SetElementStorage(plotEl, PointerOnKey, false));
    }

    protected bool TryGetPointer(ElementHandle plotEl, out Float2 pointer)
    {
        pointer = _paper.GetElementStorage(plotEl, PointerKey, new Float2(0f, 0f));
        return _paper.GetElementStorage(plotEl, PointerOnKey, false);
    }

    protected void Popup(float anchorX, float y, float minX, float maxX, string header, IReadOnlyList<(Color Color, string Text)> rows)
    {
        if (header.Length == 0 && rows.Count == 0) return;

        string widthKey = _id + "_popup_w";
        float lastWidth = _paper.GetRootStorage<float>(widthKey);

        float x = anchorX + PopupGap;
        if (lastWidth > 0f && x + lastWidth > maxX)
            x = MathF.Max(minX, anchorX - PopupGap - lastWidth);

        ElementBuilder popup = _paper.Column(_id + "_popup")
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Size(UnitValue.Auto)
            .BackgroundColor(_theme.Popover)
            .BorderColor(_theme.BorderStrong).BorderWidth(1f)
            .Rounded(6f)
            .Padding(6f)
            .Gap(6f)
            .Layer(Layer.Topmost + 1000)
            .OnPostLayout((_, rect) => _paper.SetRootStorage(widthKey, (float)rect.Size.X));

        using (popup.Enter())
        {
            if (header.Length > 0)
                Origami.Label(_paper, _id + "_popup_hdr", header).XS().AlignCenter().AlignLeft().Height(SwatchSize).Show();

            for (int i = 0; i < rows.Count; i++)
            {
                (Color color, string text) = rows[i];

                using (_paper.Row($"{_id}_popup_row_{i}").Height(SwatchSize).Width(UnitValue.Auto).Gap(2f).Enter())
                {
                    _paper.Box($"{_id}_popup_sw_{i}").Size(SwatchSize).BackgroundColor(color).Rounded(2f);
                    Origami.Label(_paper, $"{_id}_popup_txt_{i}", text).XS().AlignCenter().AlignLeft().Height(SwatchSize).Show();
                }
            }
        }
    }

    protected struct ViewRect
    {
        public float X, Y, W, H;
    }

    private static ViewRect ClampView(ViewRect view)
    {
        view.W = Math.Clamp(view.W <= 0f ? 1f : view.W, MinViewSpan, 1f);
        view.H = Math.Clamp(view.H <= 0f ? 1f : view.H, MinViewSpan, 1f);
        view.X = Math.Clamp(view.X, 0f, 1f - view.W);
        view.Y = Math.Clamp(view.Y, 0f, 1f - view.H);
        return view;
    }

    protected ViewRect View
        => ClampView(_containerEl.IsValid ? _paper.GetElementStorage(_containerEl, ViewKey, default(ViewRect)) : default);

    private void SetView(ViewRect view)
    {
        if (_containerEl.IsValid) _paper.SetElementStorage(_containerEl, ViewKey, ClampView(view));
    }

    protected void WireView(ElementBuilder plotBox, ElementHandle plotEl, bool zoomable, bool pannable, bool axisY)
    {
        if (!zoomable && !pannable) return;

        plotBox.OnPostLayout((_, rect) => _paper.SetElementStorage(plotEl, ViewSizeKey, new Float2((float)rect.Size.X, (float)rect.Size.Y)));

        if (zoomable)
        {
            plotBox.OnClick(_ => _paper.SetElementStorage(plotEl, ViewActiveKey, true));
            plotBox.OnLeave(_ => _paper.SetElementStorage(plotEl, ViewActiveKey, false));
            plotBox.OnScroll(e =>
            {
                if (!_paper.GetElementStorage(plotEl, ViewActiveKey, false)) return;
                Zoom(e, axisY);
            });
        }

        if (pannable && _paper.IsPointerDown(PaperMouseBtn.Middle) && _paper.IsParentHovered)
        {
            Float2 size = _paper.GetElementStorage(plotEl, ViewSizeKey, new Float2(0f, 0f));
            Float2 delta = _paper.PointerDelta;
            if (size.X <= 0f || size.Y <= 0f || (delta.X == 0f && delta.Y == 0f)) return;

            ViewRect view = View;
            view.X -= (float)delta.X / size.X * view.W;
            if (axisY) view.Y += (float)delta.Y / size.Y * view.H;
            SetView(view);
        }
    }

    private void Zoom(ScrollEvent e, bool axisY)
    {
        double w = e.ElementRect.Size.X, h = e.ElementRect.Size.Y;
        if (w <= 0d || h <= 0d) return;

        ViewRect view = View;
        float factor = MathF.Exp(-e.Delta * ZoomRate);

        float fx = (float)Math.Clamp((e.PointerPosition.X - e.ElementRect.Min.X) / w, 0d, 1d);
        float nw = Math.Clamp(view.W * factor, MinViewSpan, 1f);
        view.X += fx * (view.W - nw);
        view.W = nw;

        if (axisY)
        {
            float fy = (float)Math.Clamp((e.ElementRect.Min.Y + h - e.PointerPosition.Y) / h, 0d, 1d);
            float nh = Math.Clamp(view.H * factor, MinViewSpan, 1f);
            view.Y += fy * (view.H - nh);
            view.H = nh;
        }

        SetView(view);
        e.StopPropagation();
    }
}
