// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>Visual style for an Origami button.</summary>
public enum ButtonStyle
{
    /// <summary>Solid fill from the variant ramp. Default.</summary>
    Filled,
    /// <summary>Border-only; fills lightly on hover.</summary>
    Outline,
    /// <summary>No background until hover; just the label.</summary>
    Ghost,
    /// <summary>Light tinted background from the variant ramp's lower stops.</summary>
    Soft,
    /// <summary>Text-only with variant-coloured label and underline on hover.</summary>
    Link,
}

/// <summary>Per-frame state passed to a custom button renderer.</summary>
public readonly struct ButtonContext
{
    public readonly Rect Rect;
    public readonly bool IsHovered;
    public readonly bool IsPressed;
    public readonly bool IsFocused;
    public readonly bool IsLoading;
    public readonly bool IsDisabled;
    public readonly float HoverT;
    public readonly float PressT;
    public readonly float FocusT;
    public readonly OrigamiRamp Surface;
    public readonly OrigamiRamp Ink;
    public readonly OrigamiTheme Theme;

    internal ButtonContext(Rect rect, bool h, bool p, bool f, bool ld, bool d,
        float hT, float pT, float fT, OrigamiRamp surface, OrigamiRamp ink, OrigamiTheme theme)
    {
        Rect = rect; IsHovered = h; IsPressed = p; IsFocused = f; IsLoading = ld; IsDisabled = d;
        HoverT = hT; PressT = pT; FocusT = fT; Surface = surface; Ink = ink; Theme = theme;
    }
}

/// <summary>
/// Fluent builder for an Origami button. Construct via <c>Origami.Button</c> /
/// <c>Origami.IconButton</c>; chain modifiers; call <see cref="Show"/> to render.
/// </summary>
/// <remarks>
/// Single layout box per button; all chrome is painted via <see cref="Canvas"/> with the
/// cheap Filled variants. Hover, press, and focus all animate through <c>AnimateBool</c>
/// for a polished feel without per-element layout churn.
/// </remarks>
public sealed class ButtonBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private string _label;
    private Action? _onClick;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private ButtonStyle _style = ButtonStyle.Filled;

    private UnitValue? _width;
    private float _height = 26f;
    private float _padX = 14f;
    private bool _iconOnly;
    private float? _roundingOverride;

    private string? _leadingIcon;
    private string? _trailingIcon;
    private bool _loading;
    private Action? _customContent;

    private bool _disabled;
    private bool _shadow;
    private bool _pulse;
    private string? _tooltip;
    private int? _tabIndex = 0;
    private bool _autoFocus;

    private Action? _onRightClick;
    private Action? _onDoubleClick;

    private Action<Canvas, ButtonContext>? _customRender;

    /// <summary>If non-null, the builder writes the button's ElementHandle here on Show().</summary>
    private Action<ElementHandle>? _handleSink;

    internal ButtonBuilder(Paper paper, string id, string label, Action? onClick, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _label = label ?? string.Empty;
        _onClick = onClick;
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    // ── Variant ────────────────────────────────────────────────────────

    public ButtonBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public ButtonBuilder Primary() => Variant(OrigamiVariant.Primary);
    public ButtonBuilder Success() => Variant(OrigamiVariant.Success);
    public ButtonBuilder Warning() => Variant(OrigamiVariant.Warning);
    public ButtonBuilder Danger()  => Variant(OrigamiVariant.Danger);
    public ButtonBuilder Info()    => Variant(OrigamiVariant.Info);
    public ButtonBuilder Subtle()  => Variant(OrigamiVariant.Subtle);

    // ── Style ──────────────────────────────────────────────────────────

    public ButtonBuilder Style(ButtonStyle style) { _style = style; return this; }
    public ButtonBuilder Filled()  => Style(ButtonStyle.Filled);
    public ButtonBuilder Outline() => Style(ButtonStyle.Outline);
    public ButtonBuilder Ghost()   => Style(ButtonStyle.Ghost);
    public ButtonBuilder Soft()    => Style(ButtonStyle.Soft);
    public ButtonBuilder Link()    => Style(ButtonStyle.Link);

    // ── Sizing ─────────────────────────────────────────────────────────

    public ButtonBuilder Width(UnitValue w) { _width = w; return this; }
    public ButtonBuilder Width(float w) { _width = w; return this; }
    public ButtonBuilder Height(float h) { _height = MathF.Max(20, h); return this; }
    public ButtonBuilder FullWidth() { _width = UnitValue.Stretch(); return this; }
    public ButtonBuilder FitContent() { _width = UnitValue.Auto; return this; }

    public ButtonBuilder Small()  { _height = 22; _padX = 10; return this; }
    public ButtonBuilder Medium() { _height = 26; _padX = 14; return this; }
    public ButtonBuilder Large()  { _height = 34; _padX = 20; return this; }

    /// <summary>Square aspect for icon-only buttons. Width/height match, no asymmetric padding.</summary>
    public ButtonBuilder IconOnly()
    {
        _iconOnly = true;
        _padX = 0;
        if (!_width.HasValue) _width = _height;
        return this;
    }

    public ButtonBuilder Rounding(float radius) { _roundingOverride = MathF.Max(0, radius); return this; }

    // ── Content ────────────────────────────────────────────────────────

    public ButtonBuilder Label(string text) { _label = text ?? string.Empty; return this; }
    public ButtonBuilder LeadingIcon(string glyph) { _leadingIcon = glyph; return this; }
    public ButtonBuilder TrailingIcon(string glyph) { _trailingIcon = glyph; return this; }

    /// <summary>Replaces the leading slot with a spinner and suppresses clicks while true.</summary>
    public ButtonBuilder Loading(bool loading = true) { _loading = loading; return this; }

    /// <summary>Caller draws their own content row inside the styled button shell.</summary>
    public ButtonBuilder CustomContent(Action draw) { _customContent = draw; return this; }

    // ── Behaviour ──────────────────────────────────────────────────────

    public ButtonBuilder OnClick(Action click) { _onClick = click; return this; }
    public ButtonBuilder OnRightClick(Action click) { _onRightClick = click; return this; }
    public ButtonBuilder OnDoubleClick(Action click) { _onDoubleClick = click; return this; }

    public ButtonBuilder Disabled(bool disabled = true) { _disabled = disabled; return this; }
    public ButtonBuilder Tooltip(string text) { _tooltip = text; return this; }

    public ButtonBuilder TabIndex(int? index) { _tabIndex = index; return this; }
    public ButtonBuilder NotFocusable() { _tabIndex = null; return this; }
    public ButtonBuilder AutoFocus() { _autoFocus = true; return this; }

    // ── Visuals ────────────────────────────────────────────────────────

    public ButtonBuilder Shadow(bool shadow = true) { _shadow = shadow; return this; }

    /// <summary>Gentle attention-grabbing pulse. For "this is the call to action" usage.</summary>
    public ButtonBuilder Pulse(bool pulse = true) { _pulse = pulse; return this; }

    public ButtonBuilder CustomRender(Action<Canvas, ButtonContext> render) { _customRender = render; return this; }

    /// <summary>Captures the button's element handle (for popover anchoring etc.).</summary>
    public ButtonBuilder WithHandle(Action<ElementHandle> sink) { _handleSink = sink; return this; }

    // ── Content measurement (for auto-sizing) ─────────────────────────

    /// <summary>
    /// Measure the natural width of the button's content row so the auto-sized box has a real
    /// width. Without this the chrome (drawn via Canvas.Draw, not layout children) would size
    /// the parent to 0px and the button would be invisible.
    /// </summary>
    private float MeasureContentWidth(Prowl.Scribe.FontFile? font, float fontSize)
    {
        bool hasLeading = _loading || !string.IsNullOrEmpty(_leadingIcon);
        bool hasTrailing = !string.IsNullOrEmpty(_trailingIcon);
        bool hasLabel = !string.IsNullOrEmpty(_label);
        const float gap = 6f;

        float content = 0f;
        if (hasLeading) content += fontSize;
        if (hasLabel)
        {
            if (font != null)
            {
                var size = _paper.MeasureText(_label, fontSize, font);
                content += (float)size.X;
            }
            else
            {
                content += _label.Length * fontSize * 0.55f; // crude fallback
            }
            if (hasLeading) content += gap;
            if (hasTrailing) content += gap;
        }
        if (hasTrailing) content += fontSize;
        return content + _padX * 2f;
    }

    // ── Terminator ─────────────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _disabled = true;
        var ramp = _theme.Get(_variant);
        var ink = _theme.Ink;
        var font = _theme.Font;
        var metrics = _theme.Metrics;

        bool interactive = !_disabled && !_loading;

        // Resolve width. Auto-size by measuring content + padding when the caller didn't ask
        // for an explicit width — the chrome is painted via Canvas.Draw (no layout children),
        // so leaving the box on UnitValue.Auto would size it to 0px.
        UnitValue widthValue;
        if (_width.HasValue)
        {
            widthValue = _width.Value;
        }
        else if (_iconOnly)
        {
            widthValue = _height;
        }
        else
        {
            widthValue = MeasureContentWidth(font, metrics.FontSize);
        }
        float roundingValue = _roundingOverride ?? metrics.Rounding;

        var box = _paper.Box(_id)
            .Width(widthValue)
            .Height(_height);

        if (interactive)
        {
            if (_tabIndex.HasValue) box.TabIndex(_tabIndex.Value);
            if (_onClick != null)
            {
                var click = _onClick;
                box.OnClick(_ => click());
            }
            if (_onRightClick != null)
            {
                var rc = _onRightClick;
                box.OnRightClick(_ => rc());
            }
            if (_onDoubleClick != null)
            {
                var dc = _onDoubleClick;
                box.OnDoubleClick(_ => dc());
            }
        }

        using (box.Enter())
        {
            var handle = _paper.CurrentParent;
            _handleSink?.Invoke(handle);

            // First-frame autofocus.
            if (_autoFocus)
            {
                bool consumed = _paper.GetElementStorage(handle, "af_done", false);
                if (!consumed)
                {
                    _paper.SetFocus(handle);
                    _paper.SetElementStorage(handle, "af_done", true);
                }
            }

            bool isHovered = interactive && _paper.IsParentHovered;
            bool isPressed = interactive && _paper.IsParentActive;
            bool isFocused = interactive && _paper.IsElementFocused(handle.Data.ID);

            float hoverT = _paper.AnimateBool(isHovered, 0.10f, id: $"{_id}_hov");
            float pressT = _paper.AnimateBool(isPressed, 0.06f, id: $"{_id}_prs");
            float focusT = _paper.AnimateBool(isFocused, 0.12f, id: $"{_id}_foc");

            // Keyboard activation while focused — Space / Enter both trigger click.
            if (interactive && isFocused && _onClick != null)
            {
                if (_paper.IsKeyPressed(PaperKey.Space) || _paper.IsKeyPressed(PaperKey.Enter)
                    || _paper.IsKeyPressed(PaperKey.KeypadEnter))
                {
                    _onClick();
                }
            }

            // Capture all per-frame state for the closures (the builder doesn't survive past Show()).
            var snapshot = new ButtonRenderSnapshot
            {
                Variant = _variant,
                Style = _style,
                Theme = _theme,
                Ramp = ramp,
                Ink = ink,
                Font = font,
                FontSize = metrics.FontSize,
                Label = _label,
                LeadingIcon = _leadingIcon,
                TrailingIcon = _trailingIcon,
                Loading = _loading,
                Disabled = _disabled,
                Shadow = _shadow,
                Pulse = _pulse,
                Rounding = roundingValue,
                PadX = _padX,
                Height = _height,
                IconOnly = _iconOnly,
                HoverT = hoverT,
                PressT = pressT,
                FocusT = focusT,
                IsHovered = isHovered,
                IsPressed = isPressed,
                IsFocused = isFocused,
                Time = (float)_paper.Time,
            };

            // Custom render overrides the entire visual (still inside the button's hit box).
            if (_customRender != null)
            {
                var custom = _customRender;
                _paper.Draw((canvas, rect) =>
                {
                    var ctx = new ButtonContext(rect, isHovered, isPressed, isFocused,
                        snapshot.Loading, snapshot.Disabled,
                        snapshot.HoverT, snapshot.PressT, snapshot.FocusT,
                        ramp, ink, _theme);
                    custom(canvas, ctx);
                });
            }
            else
            {
                _paper.Draw((canvas, rect) => PaintDefault(canvas, rect, in snapshot));
            }

            // Optional content callback runs as actual children (uses normal layout).
            if (_customContent != null) _customContent();
        }

        // Tooltip overlay on Topmost — same pattern as Slider's value bubble.
        if (!string.IsNullOrEmpty(_tooltip) && font != null)
            DrawTooltipOverlay();
    }

    // ── Painting ───────────────────────────────────────────────────────

    private struct ButtonRenderSnapshot
    {
        public OrigamiVariant Variant;
        public ButtonStyle Style;
        public OrigamiTheme Theme;
        public OrigamiRamp Ramp;
        public OrigamiRamp Ink;
        public Prowl.Scribe.FontFile? Font;
        public float FontSize;
        public string Label;
        public string? LeadingIcon;
        public string? TrailingIcon;
        public bool Loading;
        public bool Disabled;
        public bool Shadow;
        public bool Pulse;
        public float Rounding;
        public float PadX;
        public float Height;
        public bool IconOnly;
        public float HoverT;
        public float PressT;
        public float FocusT;
        public bool IsHovered;
        public bool IsPressed;
        public bool IsFocused;
        public float Time;
    }

    private static void PaintDefault(Canvas canvas, Rect rect, in ButtonRenderSnapshot s)
    {
        float x = (float)rect.Min.X;
        float y = (float)rect.Min.Y;
        float w = (float)rect.Size.X;
        float h = (float)rect.Size.Y;

        // Press scale-down (0.97 at full press) + optional pulse.
        float scale = 1f - 0.03f * s.PressT;
        if (s.Pulse) scale += 0.015f * MathF.Sin(s.Time * 4f);
        float dx = w * (1f - scale) * 0.5f;
        float dy = h * (1f - scale) * 0.5f;
        x += dx; y += dy; w *= scale; h *= scale;

        ResolveColors(in s, out Color bg, out Color border, out Color labelCol, out bool drawBorder, out bool drawShadow);

        // Drop shadow under the button (skipped for Ghost / Link).
        if (drawShadow && s.Style != ButtonStyle.Ghost && s.Style != ButtonStyle.Link)
        {
            byte alpha = (byte)Math.Clamp((int)((40 + 20 * s.HoverT) * (s.Disabled ? 0.3f : 1f)), 0, 120);
            canvas.RoundedRectFilled(x + 1f, y + 2f, w, h, s.Rounding, Color.FromArgb(alpha, 0, 0, 0));
        }

        // Background (Filled / Soft / Outline-on-hover / Ghost-on-hover).
        if (bg.A > 0)
            canvas.RoundedRectFilled(x, y, w, h, s.Rounding, bg);

        // Border (Outline only — uses two-rect hairline trick).
        if (drawBorder)
        {
            canvas.RoundedRectFilled(x, y, w, h, s.Rounding, border);
            // Inset fill to expose the rim.
            Color innerBg = bg.A > 0 ? bg : Color.Transparent;
            if (innerBg.A > 0)
                canvas.RoundedRectFilled(x + 1f, y + 1f, w - 2f, h - 2f, MathF.Max(0, s.Rounding - 1), innerBg);
        }

        // Focus ring — sits just outside the button when focused. Painted via two filled
        // rects so we don't need a stroke pass.
        if (s.FocusT > 0.02f && !s.Disabled)
        {
            float ringT = s.FocusT;
            float pad = 1.5f;
            byte ringA = (byte)Math.Clamp((int)(180 * ringT), 0, 255);
            Color ringCol = ChooseFocusRingColor(in s);
            ringCol = Color.FromArgb(ringA, ringCol.R, ringCol.G, ringCol.B);

            canvas.RoundedRectFilled(x - pad, y - pad, w + pad * 2, h + pad * 2,
                s.Rounding + pad, ringCol);
            canvas.RoundedRectFilled(x, y, w, h, s.Rounding, bg.A > 0 ? bg : s.Theme.Neutral.C200);
            // After re-painting the inner rect we need to re-apply the border too.
            if (drawBorder)
            {
                canvas.RoundedRectFilled(x, y, w, h, s.Rounding, border);
                Color innerBg = bg.A > 0 ? bg : Color.Transparent;
                if (innerBg.A > 0)
                    canvas.RoundedRectFilled(x + 1f, y + 1f, w - 2f, h - 2f, MathF.Max(0, s.Rounding - 1), innerBg);
            }
        }

        if (s.Font == null) return;

        // Content layout: optional spinner / leading icon, label, optional trailing icon.
        float padX = s.PadX;
        float contentH = s.FontSize;
        float contentY = y + (h - contentH) * 0.5f;

        // Loading state replaces the leading slot with a spinner.
        bool drawLeading = s.Loading || !string.IsNullOrEmpty(s.LeadingIcon);
        bool drawTrailing = !string.IsNullOrEmpty(s.TrailingIcon);
        bool drawLabel = !string.IsNullOrEmpty(s.Label) && !s.IconOnly;

        float iconSize = s.FontSize;
        float iconLabelGap = 6f;

        // Pre-measure to center.
        Float2 labelSize = drawLabel ? canvas.MeasureText(s.Label, s.FontSize, s.Font) : new Float2(0, 0);
        float contentW = (drawLeading ? iconSize + (drawLabel ? iconLabelGap : 0) : 0)
                       + (drawLabel ? (float)labelSize.X : 0)
                       + (drawTrailing ? iconLabelGap + iconSize : 0);

        float contentX = s.IconOnly
            ? x + (w - iconSize) * 0.5f
            : x + (w - contentW) * 0.5f;

        if (drawLeading)
        {
            float lx = contentX;
            float ly = y + (h - iconSize) * 0.5f;
            if (s.Loading)
                PaintSpinner(canvas, lx + iconSize * 0.5f, ly + iconSize * 0.5f, iconSize * 0.45f, labelCol, s.Time);
            else if (s.LeadingIcon != null)
                canvas.DrawText(s.LeadingIcon, lx, ly, labelCol, s.FontSize, s.Font);
            contentX += iconSize + (drawLabel ? iconLabelGap : 0);
        }

        if (drawLabel)
        {
            canvas.DrawText(s.Label, contentX, contentY, labelCol, s.FontSize, s.Font);

            // Link-style hover underline.
            if (s.Style == ButtonStyle.Link && s.HoverT > 0.05f)
            {
                float ulY = contentY + (float)labelSize.Y - 1f;
                byte ulA = (byte)Math.Clamp((int)(255 * s.HoverT), 0, 255);
                canvas.RectFilled(contentX, ulY, (float)labelSize.X, 1f,
                    Color.FromArgb(ulA, labelCol.R, labelCol.G, labelCol.B));
            }

            contentX += (float)labelSize.X + (drawTrailing ? iconLabelGap : 0);
        }

        if (drawTrailing && !s.IconOnly)
        {
            float ty = y + (h - iconSize) * 0.5f;
            canvas.DrawText(s.TrailingIcon!, contentX, ty, labelCol, s.FontSize, s.Font);
        }
    }

    /// <summary>Resolve background, border, and label colors for the current style + state.</summary>
    private static void ResolveColors(in ButtonRenderSnapshot s,
        out Color bg, out Color border, out Color labelCol, out bool drawBorder, out bool drawShadow)
    {
        var ramp = s.Ramp;
        var ink = s.Ink;
        bool isDefault = s.Variant == OrigamiVariant.Default;
        bool isSubtle  = s.Variant == OrigamiVariant.Subtle;
        var fillRamp = isDefault ? s.Theme.Neutral
                     : isSubtle  ? s.Theme.Neutral
                     : ramp;

        Color baseFill = isSubtle ? s.Theme.Neutral.C300
                       : fillRamp.C500;
        Color hoverFill = OrigamiRamp.LerpColor(baseFill, fillRamp.C600, 0.6f);
        Color pressFill = OrigamiRamp.LerpColor(baseFill, fillRamp.C400, 0.5f);

        // White-ish text on saturated fills, neutral text on subtle/default fills.
        Color filledLabel = (isDefault || isSubtle) ? ink.C500 : ink.C700;

        switch (s.Style)
        {
            case ButtonStyle.Filled:
                bg = OrigamiRamp.LerpColor(baseFill, hoverFill, s.HoverT);
                bg = OrigamiRamp.LerpColor(bg, pressFill, s.PressT);
                border = Color.Transparent;
                drawBorder = false;
                labelCol = filledLabel;
                drawShadow = s.Shadow;
                break;

            case ButtonStyle.Outline:
                Color outlineFill = OrigamiRamp.LerpColor(Color.Transparent, fillRamp.C500, 0.18f * s.HoverT);
                bg = outlineFill;
                border = isDefault ? s.Theme.Neutral.C500 : ramp.C500;
                drawBorder = true;
                labelCol = isDefault ? ink.C500 : ramp.C600;
                drawShadow = false;
                break;

            case ButtonStyle.Ghost:
                Color ghostFill = OrigamiRamp.LerpColor(Color.Transparent, fillRamp.C400, 0.30f * s.HoverT);
                bg = ghostFill;
                border = Color.Transparent;
                drawBorder = false;
                labelCol = isDefault ? ink.C500 : ramp.C600;
                drawShadow = false;
                break;

            case ButtonStyle.Soft:
                Color softBase = isDefault ? s.Theme.Neutral.C300 : ramp.C200;
                Color softHover = isDefault ? s.Theme.Neutral.C400 : ramp.C300;
                bg = OrigamiRamp.LerpColor(softBase, softHover, s.HoverT);
                border = Color.Transparent;
                drawBorder = false;
                labelCol = isDefault ? ink.C500 : ramp.C600;
                drawShadow = false;
                break;

            case ButtonStyle.Link:
            default:
                bg = Color.Transparent;
                border = Color.Transparent;
                drawBorder = false;
                labelCol = isDefault ? ink.C500 : ramp.C600;
                drawShadow = false;
                break;
        }

        if (s.Disabled)
        {
            bg = OrigamiRamp.LerpColor(bg, s.Theme.Neutral.C300, 0.6f);
            border = OrigamiRamp.LerpColor(border, s.Theme.Neutral.C400, 0.6f);
            labelCol = s.Ink.C300;
        }
    }

    private static Color ChooseFocusRingColor(in ButtonRenderSnapshot s)
    {
        bool isDefault = s.Variant == OrigamiVariant.Default;
        bool isSubtle  = s.Variant == OrigamiVariant.Subtle;
        if (isDefault || isSubtle) return s.Theme.Primary.C500;
        return s.Ramp.C500;
    }

    /// <summary>
    /// Simple rotating arc spinner. Two filled circles + an outer arc emulated via filled
    /// pie slices would be heavier than needed; we use Quill's path API for a smooth arc.
    /// </summary>
    private static void PaintSpinner(Canvas canvas, float cx, float cy, float radius, Color color, float time)
    {
        canvas.SaveState();
        // 3/4 arc rotating once per second.
        float rot = (time * 360f) % 360f;
        canvas.TransformBy(Transform2D.CreateTranslation(cx, cy));
        canvas.TransformBy(Transform2D.CreateRotation(rot));

        canvas.BeginPath();
        canvas.Arc(0, 0, radius, 0, Maths.PI * 1.5f);
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(MathF.Max(1.5f, radius * 0.18f));
        canvas.Stroke();
        canvas.RestoreState();
    }

    // ── Tooltip ────────────────────────────────────────────────────────

    private void DrawTooltipOverlay()
    {
        var thandle = _paper.CurrentParent;
        string tt = _tooltip!;
        var font = _theme.Font!;
        float fontSize = _theme.Metrics.FontSize - 1f;
        Color ttBg = _theme.Neutral.C500;
        Color ttFg = _theme.Ink.C500;
        string ttId = _id;

        using (_paper.Box($"{_id}_tt")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Width(1).Height(1)
            .Layer(Layer.Topmost)
            .HookToParent()
            .IsNotInteractable()
            .Enter())
        {
            bool wantTooltip = _paper.IsElementHovered(thandle.Data.ID);
            float ttAnim = _paper.AnimateBool(wantTooltip, 0.16f, id: $"{ttId}_ttf");
            if (ttAnim < 0.01f) return;

            _paper.Draw((canvas, _) =>
            {
                var tr = thandle.Data.LayoutRect;
                float trX = (float)tr.Min.X;
                float trY = (float)tr.Min.Y;
                float trW = (float)tr.Size.X;

                var ts = canvas.MeasureText(tt, fontSize, font);
                float padX = 6f, padY = 2f;
                float bw = (float)ts.X + padX * 2f;
                float bh = (float)ts.Y + padY * 2f;
                float slide = (1f - ttAnim) * 4f;
                float bx = trX + trW * 0.5f - bw * 0.5f;
                float by = trY - bh - 6f + slide;

                byte aShadow = (byte)Math.Clamp((int)(80 * ttAnim), 0, 255);
                byte aBody   = (byte)Math.Clamp((int)(255 * ttAnim), 0, 255);
                byte aText   = (byte)Math.Clamp((int)(255 * ttAnim), 0, 255);

                canvas.RoundedRectFilled(bx + 1f, by + 2f, bw, bh, 3f,
                    Color.FromArgb(aShadow, 0, 0, 0));
                canvas.RoundedRectFilled(bx, by, bw, bh, 3f,
                    Color.FromArgb(aBody, ttBg.R, ttBg.G, ttBg.B));
                canvas.DrawText(tt, bx + padX, by + padY,
                    Color.FromArgb(aText, ttFg.R, ttFg.G, ttFg.B), fontSize, font);
            });
        }
    }
}
