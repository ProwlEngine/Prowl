// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;

using Color = System.Drawing.Color;

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
    private bool? _expandedOverride;
    private Action<bool>? _onExpandChanged;
    private bool? _toggleValue;
    private Action<bool>? _toggleSetter;
    private string? _badge;
    private IOrigamiIcon? _icon;

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
    /// Controlled expansion: the caller owns the open state. Overrides the internal storage so the
    /// foldout renders exactly <paramref name="value"/>; <paramref name="onChanged"/> fires with the
    /// requested state when the header is clicked. Use this for accordions (single-open groups).
    /// </summary>
    public FoldoutBuilder Expanded(bool value, Action<bool> onChanged)
    {
        _expandedOverride = value;
        _onExpandChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        return this;
    }

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

    /// <summary>
    /// Optional leading vector icon, drawn between the caret and the label. The stroke color is
    /// preset to the acc-300 accent (<c>theme.Primary.C700</c>); the action receives the icon's
    /// canvas and bounds — draw a path and <c>Stroke()</c> (or override the stroke color yourself).
    /// </summary>
    public FoldoutBuilder Icon(IOrigamiIcon? icon) { _icon = icon; return this; }

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

        var ink = _theme.Ink;
        var metrics = _theme.Metrics;
        float rounding = _roundingOverride ?? 9f;

        // Nebula "w2fold" tokens.
        Color bdSoft      = _theme.BorderSoft;   // --bd-soft
        Color glassIn     = _theme.Glass;       // --glass-in
        Color hoverPurple = _theme.Hover;    // rgba(168,85,247,0.12)
        Color caretCol    = ink.C200;                            // --t-lo
        Color labelCol    = ink.C500;                            // --t-hi
        Color badgeCol    = ink.C300;                            // --t-mid
        Color accIcon     = _theme.Primary.C700;                 // --acc-300

        bool hasToggle = _toggleValue.HasValue;
        bool isEnabled = _toggleValue ?? true;

        // Subtle suppresses the idle glass fill; everything else uses glass-in unless overridden.
        Color headerBg = _headerBgOverride
            ?? (_variant == OrigamiVariant.Subtle ? Color.Transparent : glassIn);

        float headH = metrics.FontSize + 18f;   // ~9px vertical padding around the label
        const float padX = 11f;                  // header horizontal padding
        const float gap  = 8f;                   // flex gap between header children

        // Outer card: 1px bd-soft border, radius 9, overflow hidden.
        var container = _paper.Column($"{_id}")
            .Width(UnitValue.Stretch())
            .Height(UnitValue.Auto)
            .Rounded(rounding)
            .BorderColor(bdSoft)
            .BorderWidth(1f)
            .Clip();

        using (container.Enter())
        {
            var header = _paper.Row($"{_id}_header")
                .Width(UnitValue.Stretch())
                .Height(headH);

            bool expanded = _expandedOverride ?? _paper.GetElementStorage(header._handle, "exp", _defaultExpanded);

            // Grab the open/close animation value up front so we can skip the body when fully closed.
            float anim;
            using (header.Enter())
                anim = _paper.AnimateBool(expanded, 0.2f);

            if (headerBg.A > 0)
                header.BackgroundColor(headerBg);
            header.Hovered.BackgroundColor(hoverPurple).End();
            // Round the header fill to match the clipped rounded container (a rectangular clip alone
            // leaves square top corners poking over the border): top-only when a body follows, else all.
            if (expanded || anim > float.Epsilon)
                header.RoundedTop(rounding);
            else
                header.Rounded(rounding);
            if (_onExpandChanged != null)
                header.OnClick(_ => _onExpandChanged(!expanded));
            else
                header.OnClick(_ => _paper.SetElementStorage(header._handle, "exp", !expanded));

            using (header.Enter())
            {
                float leftPad = padX;

                // Disclosure caret — vector (the glyph font is empty). Points right when collapsed
                // and rotates to point down as the foldout opens, driven by `anim`.
                using (_paper.Box($"{_id}_arrow")
                    .Width(14f).Height(headH)
                    .Margin(leftPad, gap, 0, 0)
                    .IsNotInteractable()
                    .Enter())
                {
                    float a = anim;
                    _paper.Draw((canvas, rr) =>
                    {
                        float cx = (float)(rr.Min.X + rr.Size.X * 0.5f);
                        float cy = (float)(rr.Min.Y + rr.Size.Y * 0.5f);
                        DrawCaret(canvas, cx, cy, a, caretCol);
                    });
                }
                leftPad = 0f;

                // Optional leading accent icon (acc-300), vertically centered in the header.
                if (_icon != null)
                {
                    var icon = _icon;
                    using (_paper.Box($"{_id}_icon")
                        .Width(14f).Height(headH)
                        .Margin(0, gap, 0, 0)
                        .IsNotInteractable()
                        .Enter())
                    {
                        _paper.Draw((canvas, rr) =>
                        {
                            const float isz = 14f;
                            float ix = (float)(rr.Min.X + (rr.Size.X - isz) * 0.5f);
                            float iy = (float)(rr.Min.Y + (rr.Size.Y - isz) * 0.5f);
                            var cell = new Prowl.Vector.Rect(ix, iy, ix + isz, iy + isz);
                            icon.Draw(canvas, cell, accIcon);
                        });
                    }
                }

                if (_theme.Font != null)
                {
                    bool drawBadge = !string.IsNullOrEmpty(_badge);

                    // Enable toggle (optional). The glyph font is empty, so this reads as a small
                    // click target that dims the label as feedback.
                    if (hasToggle)
                    {
                        var setter = _toggleSetter!;
                        _paper.Box($"{_id}_chk")
                            .Width(metrics.IconWidth).Height(headH)
                            .Margin(0, gap, 0, 0)
                            .Alignment(TextAlignment.MiddleCenter)
                            .OnClick(0, (_, e) => { e.StopPropagation(); setter(!isEnabled); });
                    }

                    // Label — fills the remaining width; carries the right edge padding when no badge follows.
                    _paper.Box($"{_id}_lbl")
                        .Width(UnitValue.Stretch())
                        .Margin(leftPad, drawBadge ? 0 : padX, 0, 0)
                        .Text(_label, _theme.SemiBold ?? _theme.Font)
                        .TextColor(hasToggle && !isEnabled ? ink.C300 : labelCol)
                        .Alignment(TextAlignment.MiddleLeft)
                        .FontSize(metrics.FontSize);

                    // Badge — last child, carries the right edge padding.
                    if (drawBadge)
                    {
                        _paper.Box($"{_id}_badge")
                            .Width(UnitValue.Auto).Height(headH)
                            .Margin(metrics.BadgePadLeft, padX, 0, 0)
                            .Text(_badge, _theme.Font)
                            .TextColor(badgeCol)
                            .FontSize(metrics.FontSize - 1f)
                            .Alignment(TextAlignment.MiddleRight);
                    }
                }
            }

            // ── Body ──────────────────────────────────────────────────

            if (!expanded && anim <= float.Epsilon)
                return;

            Color bodyBg = _bodyBgOverride ?? Color.Transparent;

            // Body wrapper collapses via an animated height and is clipped so content wipes in/out.
            var body = _paper.Column($"{_id}_body")
                .Width(UnitValue.Stretch())
                .Height(UnitValue.Lerp(0, UnitValue.Auto, anim))
                .Clip();
            if (bodyBg.A > 0)
                body.BackgroundColor(bodyBg);

            using (body.Enter())
            {
                // 1px bd-soft rule separating the header from the body (CSS border-top).
                _paper.Box($"{_id}_sep")
                    .Width(UnitValue.Stretch()).Height(1f)
                    .BackgroundColor(bdSoft)
                    .IsNotInteractable();

                var content = _paper.Box($"{_id}_inner")
                    .Width(UnitValue.Stretch())
                    .Height(UnitValue.Auto)
                    .Padding(12f, 12f, 10f, 10f)
                    .TextColor(ink.C300)                 // --t-mid body text
                    .FontSize(metrics.FontSize);
                if (_bodyOutlined)
                    content.BorderColor(bdSoft).BorderWidth(1f);

                using (content.Enter())
                    drawContents();
            }
        }
    }

    // Two-segment chevron. Base shape points right (collapsed); rotates 90° to point down as
    // `anim` goes 0 → 1. Points from the prototype: (-2,-3.5) → (1.5,0) → (-2,3.5) about the center.
    private static void DrawCaret(Canvas canvas, float cx, float cy, float anim, Color color)
    {
        float th = anim * (MathF.PI * 0.5f);
        float cos = MathF.Cos(th), sin = MathF.Sin(th);

        float ax = cx + (-2f) * cos - (-3.5f) * sin;
        float ay = cy + (-2f) * sin + (-3.5f) * cos;
        float bx = cx + (1.5f) * cos;
        float by = cy + (1.5f) * sin;
        float dx = cx + (-2f) * cos - (3.5f) * sin;
        float dy = cy + (-2f) * sin + (3.5f) * cos;

        canvas.SaveState();
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(1.5f);
        canvas.SetStrokeCap(EndCapStyle.Round);
        canvas.SetStrokeJoint(JointStyle.Round);
        canvas.BeginPath();
        canvas.MoveTo(ax, ay);
        canvas.LineTo(bx, by);
        canvas.LineTo(dx, dy);
        canvas.Stroke();
        canvas.RestoreState();
    }
}
