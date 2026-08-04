// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

using Color = System.Drawing.Color;

using Prowl.OrigamiUI;

namespace Prowl.OrigamiUI.Charts;

/// <summary>
/// Shared implementation behind the Cartesian and Circular chart families. Owns the outer box, title
/// header, and legend column - identical across every chart type regardless of geometry - plus the data
/// set passed at construction and the generic legend-driven hidden/visible state every subtype's series,
/// slices or nodes are filtered against. A subtype supplies only its own legend rows (<see cref="BuildLegendEntries"/>)
/// and its own geometry, painted into the plot area this hands it (<see cref="DrawPlot"/>).
/// </summary>
public abstract class ChartCore<TSelf, T> where TSelf : ChartCore<TSelf, T>
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
    private string _emptyLabel = "No data";

    private bool _legend = true;
    private bool _legendShowValue = true;
    private float? _legendFontSize;
    private bool _legendInteractive;

    private ElementHandle _containerEl;

    /// <summary>The outer chart container, valid from the top of <see cref="Show"/> onward. A subtype
    /// keys its own per-instance state (view rect, sample position, ...) off this, kept separate from the
    /// legend's hidden-key storage above.</summary>
    protected ElementHandle ContainerEl => _containerEl;

    private const string HiddenKeyPrefix = "chartcore_hidden_";
    private const float LegendWidth = 125f;
    private const float SwatchSize = 12f;

    protected ChartCore(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data = null)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _data = data;
    }

    private TSelf Self => (TSelf)this;

    // ── Chrome ──────────────────────────────────────────────────

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

    public TSelf EmptyLabel(string text) { _emptyLabel = text ?? "No data"; return Self; }

    /// <summary>The active variant's colour ramp, which is where a subtype's default series/slice colours
    /// and its own accent colour come from.</summary>
    protected OrigamiRamp Ramp => _theme.Get(_variant);

    /// <summary>Text shown centred in the chart box in place of geometry when a subtype has nothing to
    /// draw. Set via <see cref="EmptyLabel"/>.</summary>
    protected string EmptyLabelText => _emptyLabel;

    /// <summary>Padding passed to <see cref="Padding"/>. A subtype's plot area is already inset by this
    /// through the container itself; exposed for pieces (e.g. a legend's own top padding) that need to
    /// line up with it explicitly.</summary>
    protected float PaddingValue => _padding;

    // ── Legend ──────────────────────────────────────────────────

    public TSelf Legend(bool show = true) { _legend = show; return Self; }
    public TSelf LegendShowValue(bool show = true) { _legendShowValue = show; return Self; }

    /// <summary>Font size, in pixels, of the legend's labels. Unset uses the theme's XS size.</summary>
    public TSelf LegendFontSize(float size) { _legendFontSize = MathF.Max(1f, size); return Self; }

    /// <summary>Let a click on a legend swatch hide and show what it represents. Also gated globally by
    /// <see cref="Origami.LegendSelectionEnabled"/>.</summary>
    public TSelf LegendInteractive(bool interactive) { _legendInteractive = interactive; return Self; }

    /// <summary>Whether the caller asked for value text next to each legend label. A subtype's
    /// <see cref="BuildLegendEntries"/> reads this to decide whether to populate <see cref="LegendEntry.ValueText"/>.</summary>
    protected bool LegendShowValueEnabled => _legendShowValue;

    /// <summary>Whether legend interaction is both requested on this instance and not killed globally.</summary>
    protected bool LegendInteractiveActive => _legendInteractive && Origami.LegendSelectionEnabled;

    /// <summary>Whether the legend entry keyed <paramref name="key"/> is currently hidden. Always false
    /// while <see cref="LegendInteractiveActive"/> is off, so turning off the global kill switch
    /// immediately reveals everything even though the stored toggle survives underneath it.</summary>
    protected bool IsLegendHidden(int key)
        => LegendInteractiveActive && _containerEl.IsValid && _paper.GetElementStorage(_containerEl, HiddenKeyPrefix + key, false);

    private void ToggleLegend(int key)
    {
        if (!_containerEl.IsValid) return;
        bool hidden = !_paper.GetElementStorage(_containerEl, HiddenKeyPrefix + key, false);
        _paper.SetElementStorage(_containerEl, HiddenKeyPrefix + key, hidden);
    }

    // ── Type hooks ──────────────────────────────────────────────

    /// <summary>Called once at the top of <see cref="Show"/>, before any data is resolved.</summary>
    protected virtual void OnBeforeShow() { }

    /// <summary>This chart's legend rows for the current data, in draw order. Called every frame
    /// regardless of whether the legend is shown, since a subtype's <see cref="DrawPlot"/> is expected to
    /// resolve and cache its series/slices/nodes here rather than a second time. Each entry's
    /// <see cref="LegendEntry.Hidden"/> should come from <see cref="IsLegendHidden"/> on that entry's key.</summary>
    protected abstract IReadOnlyList<LegendEntry> BuildLegendEntries();

    /// <summary>Draw everything past the legend divider: gutters, ticks, grid and marks for a Cartesian
    /// type, or the plot circle and its chrome for a Circular type. Called as a direct child of the same
    /// row the legend column sits in, so this is free to enter its own Column/Box occupying the rest of
    /// that row.</summary>
    protected abstract void DrawPlot();

    // ── Show ────────────────────────────────────────────────────

    public void Show()
    {
        OnBeforeShow();

        ElementBuilder container = _paper.Column(_id)
            .Size(_width, _height)
            .Rounded(_theme.Metrics.ContainerRounding)
            .BorderColor(_theme.BorderSoft).BorderWidth(1f)
            .Padding(_padding);

        DecorateContainer(container);

        using (container.Enter())
        {
            _containerEl = _paper.CurrentParent;

            using (_paper.Row(_id + "_chart_header").Height(20).ChildRight().Enter())
                Origami.Label(_paper, _id + "_chart_header", _title).MD().Height(15).AlignCenter().Show();

            _paper.Box(_id + "_chart_header_div").Height(1).BackgroundColor(_theme.BorderStrong);

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

    /// <summary>Hook for a subtype to add chrome to the outer container beyond what every chart shares,
    /// such as Cartesian's <c>.BackgroundColor(...)</c>. No-op by default.</summary>
    protected virtual void DecorateContainer(ElementBuilder container) { }

    protected static Color32 ToC32(Color c) => new Color32(c.R, c.G, c.B, c.A);
    protected static Color32 ToC32(Color c, float alpha) => new Color32(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0f, 255f));
}
