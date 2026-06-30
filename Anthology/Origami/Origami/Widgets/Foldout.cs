// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Drawing;


using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.OrigamiUI;

/// <summary>
/// Fluent builder for an Origami foldout. Construct via <see cref="Origami.Foldout"/>;
/// chain modifiers; call <see cref="Body"/> to render.
/// </summary>
/// <remarks>
/// Layout when expanded:
/// <list type="bullet">
/// <item><description>Header — top-rounded only, fills the foldout's row width, hosts the chevron / toggle / label / badge.</description></item>
/// <item><description>Body wrapper — bottom-rounded, same surface tone as the header (extends visually). Wraps the inner content panel and a vertical scroll if needed.</description></item>
/// <item><description>Inner content panel — sits inside the body wrapper with margin and its own rounded outline; uses a darker fill so it reads as a recessed inset where actual content lives.</description></item>
/// </list>
/// </remarks>
public sealed class FoldoutBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly string _label;
    private readonly OrigamiTheme _theme;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private bool _defaultExpanded;
    private bool? _toggleValue;
    private Action<bool>? _toggleSetter;
    private string? _badge;

    private Color? _headerBgOverride;
    private Color? _bodyBgOverride;
    private bool _bodyOutlined;
    private float? _roundingOverride;

    internal FoldoutBuilder(Paper paper, string id, string label, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _label = label ?? string.Empty;
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    // ── Variant ────────────────────────────────────────────────────────

    public FoldoutBuilder Variant(OrigamiVariant variant) { _variant = variant; return this; }
    public FoldoutBuilder Primary() => Variant(OrigamiVariant.Primary);
    public FoldoutBuilder Success() => Variant(OrigamiVariant.Success);
    public FoldoutBuilder Warning() => Variant(OrigamiVariant.Warning);
    public FoldoutBuilder Danger()  => Variant(OrigamiVariant.Danger);
    public FoldoutBuilder Info()    => Variant(OrigamiVariant.Info);
    public FoldoutBuilder Subtle()  => Variant(OrigamiVariant.Subtle);

    // ── Behaviour ──────────────────────────────────────────────────────

    /// <summary>First-time expansion state. After the first frame, user expand state persists in element storage.</summary>
    public FoldoutBuilder DefaultExpanded(bool expanded = true) { _defaultExpanded = expanded; return this; }

    /// <summary>
    /// Add an enable toggle next to the header chevron. The label dims when <paramref name="value"/> is false;
    /// <paramref name="setter"/> fires on click.
    /// </summary>
    public FoldoutBuilder Toggle(bool value, Action<bool> setter)
    {
        _toggleValue = value;
        _toggleSetter = setter ?? throw new ArgumentNullException(nameof(setter));
        return this;
    }

    /// <summary>Right-aligned text on the header — use for counts, summaries, status indicators.</summary>
    public FoldoutBuilder Badge(string? text) { _badge = text; return this; }

    // ── Per-instance style overrides ───────────────────────────────────

    public FoldoutBuilder HeaderBackground(Color color) { _headerBgOverride = color; return this; }
    public FoldoutBuilder BodyBackground(Color color) { _bodyBgOverride = color; return this; }
    public FoldoutBuilder BodyOutlined(bool outlined = true) { _bodyOutlined = outlined; return this; }
    public FoldoutBuilder Rounding(float radius) { _roundingOverride = radius; return this; }

    // ── Terminator ─────────────────────────────────────────────────────

    /// <summary>Render the foldout. <paramref name="drawContents"/> only runs when expanded.</summary>
    public void Body(Action drawContents)
    {
        ArgumentNullException.ThrowIfNull(drawContents);

        var ramp = _theme.Get(_variant);
        var ink = _theme.Ink;
        var metrics = _theme.Metrics;
        var icons = _theme.Icons;
        float rounding = _roundingOverride ?? metrics.Rounding;
        bool hasToggle = _toggleValue.HasValue;
        bool isEnabled = _toggleValue ?? true;

        // Subtle suppresses idle bg; everything else uses C300 unless overridden.
        Color headerBg = _headerBgOverride
            ?? (_variant == OrigamiVariant.Subtle ? Color.Transparent : ramp.C300);

        // Probe expand state up front (we need it to choose corner rounding).
        // The header element itself stores it; we declare the row first, then read.
        // No ChildLeft/Right on the header — first/last visible children carry their
        // own edge padding so layout doesn't depend on flaky child-padding semantics.
        var header = _paper.Row($"{_id}_header")
            .Height(metrics.HeaderHeight);

        bool expanded = _paper.GetElementStorage(header._handle, "exp", _defaultExpanded);

        // First we need the animation value, lets grab it here so we can skip creating the body at all if animation is 0
        // But also since the Header needs rounded at the bottom to stay 0 while the body exists
        float anim = 0f;
        using (header.Enter())
        {
            anim = _paper.AnimateBool(expanded, 0.2f);
        }

        // Header rounding: full when collapsed, top-only when expanded so it reads as
        // continuous with the body wrapper below it. Bottom margin removed when expanded
        // so the body sits flush against the header (no visual gap).
        if (expanded || anim > float.Epsilon)
        {
            header.Rounded(rounding, rounding, 0, 0);
            header.Margin(UnitValue.Auto, UnitValue.Auto, 2, 0);
        }
        else
        {
            header.Rounded(rounding);
            header.Margin(UnitValue.Auto, 2);
        }

        if (headerBg.A > 0)
            header.BackgroundColor(headerBg);
        header.Hovered.BackgroundColor(ramp.C500).End();
        header.OnClick(_ => _paper.SetElementStorage(header._handle, "exp", !expanded));

        if (_theme.Font != null)
        {
            using (header.Enter())
            {
                bool drawChevron = !string.IsNullOrEmpty(icons.ChevronDown) && !string.IsNullOrEmpty(icons.ChevronRight);
                bool drawToggleGlyph = hasToggle && !string.IsNullOrEmpty(icons.CheckboxOn) && !string.IsNullOrEmpty(icons.CheckboxOff);
                bool drawBadge = !string.IsNullOrEmpty(_badge);

                // Edge padding is carried by the first/last visible child instead of the header's
                // ChildLeft/Right. `leftPad` is consumed by whichever child draws first.
                float leftPad = metrics.HeaderPadX;
                float rightPad = metrics.HeaderPadX;

                // Disclosure chevron — skipped (and its width reclaimed by the label) when no glyph is set.
                if (drawChevron)
                {
                    _paper.Box($"{_id}_arrow")
                        .Width(metrics.IconWidth).MaxWidth(metrics.IconWidth)
                        .Margin(leftPad, 0, UnitValue.Auto, UnitValue.Auto)
                        .Alignment(TextAlignment.MiddleLeft)
                        .Text(expanded ? icons.ChevronDown : icons.ChevronRight, _theme.Font)
                        .TextColor(ink.C300)
                        .FontSize(metrics.FontSize * 0.7f);
                    leftPad = 0;
                }

                // Enable toggle (uses checkbox glyphs when available; otherwise we still register
                // the click target as a small bg-only box so the user can toggle, and dim the
                // label as feedback).
                if (hasToggle)
                {
                    var setter = _toggleSetter!;
                    var toggleBox = _paper.Box($"{_id}_chk")
                        .Width(metrics.IconWidth).Height(metrics.HeaderHeight)
                        .Margin(leftPad, 0, 0, 0)
                        .Alignment(TextAlignment.MiddleCenter);
                    if (drawToggleGlyph)
                        toggleBox.Text(isEnabled ? icons.CheckboxOn : icons.CheckboxOff, _theme.Font)
                                 .TextColor(ink.C500)
                                 .FontSize(metrics.FontSize);
                    toggleBox.OnClick(0, (_, e) => { e.StopPropagation(); setter(!isEnabled); });
                    leftPad = 0;
                }

                // Label — fills remaining width. Carries leftPad if it's the first child,
                // and rightPad when there's no badge after it.
                _paper.Box($"{_id}_lbl")
                    .Width(UnitValue.Stretch())
                    .Margin(leftPad, drawBadge ? 0 : rightPad, 0, 0)
                    .Text(_label, _theme.Font)
                    .TextColor(hasToggle && !isEnabled ? ink.C300 : ink.C500)
                    .Alignment(TextAlignment.MiddleLeft)
                    .FontSize(metrics.FontSize);

                // Badge — last child, carries rightPad.
                if (drawBadge)
                {
                    _paper.Box($"{_id}_badge")
                        .Width(UnitValue.Auto).Height(metrics.HeaderHeight)
                        .Margin(metrics.BadgePadLeft, rightPad, 0, 0)
                        .Text(_badge, _theme.Font)
                        .TextColor(ink.C300)
                        .FontSize(metrics.FontSize - 1f)
                        .Alignment(TextAlignment.MiddleRight);
                }
            }
        }

        // ── Body ──────────────────────────────────────────────────

        if (!expanded && anim <= float.Epsilon)
            return;

        // Outer wrapper: same surface as the header (so the two read as one card),
        // bottom-rounded only, with small inner padding so the inset panel doesn't
        // hug the rounded corner. No top margin/padding — sits flush with the header.
        Color bodyBg = _bodyBgOverride ?? headerBg;

        var bodyWrapper = _paper.Column($"{_id}_body")
            .Width(UnitValue.Stretch())
            .Height(UnitValue.Auto)
            .Rounded(0, 0, rounding, rounding);
        if (bodyBg.A > 0) bodyWrapper.BackgroundColor(bodyBg);
        if (_bodyOutlined) bodyWrapper.BorderColor(ramp.C500).BorderWidth(1f);

        using (bodyWrapper.Enter())
        {
            // Inner panel — recessed darker fill with its own rounded corners. Sized to
            // children; callers wrap content in a ScrollView themselves if they want one.
            var inner = _paper.Box($"{_id}_inner")
                .Width(UnitValue.Stretch())
                .Height(UnitValue.Lerp(0, UnitValue.Auto, anim))
                .Margin(_theme.Metrics.PaddingSmall)
                .Padding(_theme.Metrics.PaddingSmall)
                .BackgroundColor(ramp.C100)
                .Clip()
                .Rounded(rounding);

            using (inner.Enter())
            {
                drawContents();
            }
        }
    }
}
