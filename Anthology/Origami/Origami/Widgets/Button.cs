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
    private float _height = 32f;
    private float _padX = 14f;
    private float _fontScale = 1f;
    private bool _iconOnly;
    private float? _roundingOverride;

    private string? _leadingIcon;
    private string? _trailingIcon;
    private IOrigamiIcon? _leadingIconObj;
    private IOrigamiIcon? _trailingIconObj;
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

    public ButtonBuilder Small()  { _height = 26; _padX = 10; _fontScale = 0.92f; return this; }
    public ButtonBuilder Medium() { _height = 32; _padX = 14; _fontScale = 1f;    return this; }
    public ButtonBuilder Large()  { _height = 38; _padX = 18; _fontScale = 1.08f; return this; }

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

    /// <summary>Leading icon drawn by an <see cref="IOrigamiIcon"/> (vector / font / custom), tinted to the label colour.</summary>
    public ButtonBuilder LeadingIcon(IOrigamiIcon? icon) { _leadingIconObj = icon; return this; }
    public ButtonBuilder TrailingIcon(IOrigamiIcon? icon) { _trailingIconObj = icon; return this; }

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
        bool hasLeading = _loading || !string.IsNullOrEmpty(_leadingIcon) || _leadingIconObj != null;
        bool hasTrailing = !string.IsNullOrEmpty(_trailingIcon) || _trailingIconObj != null;
        bool hasLabel = !string.IsNullOrEmpty(_label);
        const float gap = 7f;

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
        var font = _theme.Medium ?? _theme.Font;   // .w2btn is weight 500
        var metrics = _theme.Metrics;
        float fontSize = metrics.FontSize * _fontScale;

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
            widthValue = MeasureContentWidth(font, fontSize);
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
                FontSize = fontSize,
                Label = _label,
                LeadingIcon = _leadingIcon,
                TrailingIcon = _trailingIcon,
                LeadingIconObj = _leadingIconObj,
                TrailingIconObj = _trailingIconObj,
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

            // Hover tooltip routed through the shared TooltipSystem (drawn in Origami.EndFrame).
            if (!string.IsNullOrEmpty(_tooltip) && isHovered)
                TooltipSystem.Hover(handle.Data.ID, _tooltip!);
        }
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
        public IOrigamiIcon? LeadingIconObj;
        public IOrigamiIcon? TrailingIconObj;
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
        float x = (float)rect.Min.X, y = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;

        BtnColors c = ResolveColors(in s);

        // Press scale-down + optional pulse + hover lift (filled buttons rise 1px like the prototype).
        float scale = 1f - 0.03f * s.PressT;
        if (s.Pulse) scale += 0.015f * MathF.Sin(s.Time * 4f);
        float dx = w * (1f - scale) * 0.5f, dy = h * (1f - scale) * 0.5f;
        x += dx; y += dy; w *= scale; h *= scale;
        if (c.Lift) y -= 1.5f * s.HoverT;

        float r = s.Rounding;

        // Coloured glow (filled variants) or a soft drop shadow.
        if (c.DrawGlow)
            PaintGlow(canvas, x, y, w, h, r, c.Glow, 4f, 18f, -4f);
        else if (s.Shadow && s.Style != ButtonStyle.Ghost && s.Style != ButtonStyle.Link)
        {
            byte a = (byte)Math.Clamp((int)(50 + 20 * s.HoverT), 0, 120);
            canvas.RoundedRectFilled(x + 1f, y + 3f, w, h, r, Color.FromArgb(a, 0, 0, 0));
        }

        // Background: gradient (filled variants) or solid fill.
        if (c.Gradient)
        {
            canvas.SaveState();
            canvas.SetLinearBrush(x, y, x + w, y + h, c.BgTop, c.BgBottom);
            canvas.BeginPath();
            canvas.RoundedRect(x, y, w, h, r);
            canvas.Fill();
            canvas.RestoreState();
        }
        else if (c.BgTop.A > 0)
        {
            canvas.RoundedRectFilled(x, y, w, h, r, c.BgTop);
        }

        // Border hairline (secondary / soft / outline).
        if (c.DrawBorder && c.Border.A > 0)
        {
            canvas.SaveState();
            canvas.SetStrokeColor(c.Border);
            canvas.SetStrokeWidth(1f);
            canvas.BeginPath();
            canvas.RoundedRect(x + 0.5f, y + 0.5f, w - 1f, h - 1f, r);
            canvas.Stroke();
            canvas.RestoreState();
        }

        // Focus ring just outside the button.
        if (s.FocusT > 0.02f && !s.Disabled)
        {
            float pad = 2.5f;
            byte ringA = (byte)Math.Clamp((int)(200 * s.FocusT), 0, 255);
            Color ring = ChooseFocusRingColor(in s);
            canvas.SaveState();
            canvas.SetStrokeColor(Color.FromArgb(ringA, ring.R, ring.G, ring.B));
            canvas.SetStrokeWidth(2f);
            canvas.BeginPath();
            canvas.RoundedRect(x - pad, y - pad, w + pad * 2f, h + pad * 2f, r + pad);
            canvas.Stroke();
            canvas.RestoreState();
        }

        if (s.Font == null) return;

        // Content row: leading (spinner / vector / glyph), label, trailing.
        bool drawLeading = s.Loading || !string.IsNullOrEmpty(s.LeadingIcon) || s.LeadingIconObj != null;
        bool drawTrailing = (!string.IsNullOrEmpty(s.TrailingIcon) || s.TrailingIconObj != null) && !s.IconOnly;
        bool drawLabel = !string.IsNullOrEmpty(s.Label) && !s.IconOnly;

        float iconSize = s.FontSize;
        const float gap = 7f;
        Color labelCol = c.Label;

        Float2 labelSize = drawLabel ? canvas.MeasureText(s.Label, s.FontSize, s.Font) : new Float2(0, 0);
        float contentW = (drawLeading ? iconSize + (drawLabel ? gap : 0) : 0)
                       + (drawLabel ? (float)labelSize.X : 0)
                       + (drawTrailing ? gap + iconSize : 0);
        float contentX = s.IconOnly ? x + (w - iconSize) * 0.5f : x + (w - contentW) * 0.5f;
        float contentY = y + (h - s.FontSize) * 0.5f;

        if (drawLeading)
        {
            float lx = contentX, ly = y + (h - iconSize) * 0.5f;
            if (s.Loading)
                PaintSpinner(canvas, lx + iconSize * 0.5f, ly + iconSize * 0.5f, iconSize * 0.45f, labelCol, s.Time);
            else if (s.LeadingIconObj != null)
                s.LeadingIconObj.Draw(canvas, new Rect(new Float2(lx, ly), new Float2(lx + iconSize, ly + iconSize)), labelCol);
            else if (s.LeadingIcon != null)
                canvas.DrawText(s.LeadingIcon, lx, ly, labelCol, s.FontSize, s.Font);
            contentX += iconSize + (drawLabel ? gap : 0);
        }

        if (drawLabel)
        {
            canvas.DrawText(s.Label, contentX, contentY, labelCol, s.FontSize, s.Font);
            if (s.Style == ButtonStyle.Link && s.HoverT > 0.05f)
            {
                float ulY = contentY + (float)labelSize.Y - 1f;
                byte ulA = (byte)Math.Clamp((int)(255 * s.HoverT), 0, 255);
                canvas.RectFilled(contentX, ulY, (float)labelSize.X, 1f, Color.FromArgb(ulA, labelCol.R, labelCol.G, labelCol.B));
            }
            contentX += (float)labelSize.X + (drawTrailing ? gap : 0);
        }

        if (drawTrailing)
        {
            float ty = y + (h - iconSize) * 0.5f;
            if (s.TrailingIconObj != null)
                s.TrailingIconObj.Draw(canvas, new Rect(new Float2(contentX, ty), new Float2(contentX + iconSize, ty + iconSize)), labelCol);
            else if (s.TrailingIcon != null)
                canvas.DrawText(s.TrailingIcon, contentX, ty, labelCol, s.FontSize, s.Font);
        }
    }

    private struct BtnColors
    {
        public Color BgTop, BgBottom; public bool Gradient;
        public Color Border; public bool DrawBorder;
        public Color Label;
        public Color Glow; public bool DrawGlow;
        public bool Lift;
    }

    private static Color Alpha(Color c, float a) => Color.FromArgb((int)Math.Clamp(a * 255f, 0, 255), c.R, c.G, c.B);
    private static Color MulA(Color c, float f) => Color.FromArgb((int)Math.Clamp(c.A * f, 0, 255), c.R, c.G, c.B);

    /// <summary>Resolve the full paint recipe (fill, gradient, border, glow, label) for the style + state.</summary>
    private static BtnColors ResolveColors(in ButtonRenderSnapshot s)
    {
        var ramp = s.Ramp; var ink = s.Ink; var neutral = s.Theme.Neutral; var primary = s.Theme.Primary;
        bool isDefault = s.Variant == OrigamiVariant.Default;
        bool isSubtle  = s.Variant == OrigamiVariant.Subtle;
        bool saturated = !isDefault && !isSubtle;
        var r = new BtnColors { Label = ink.C500 };

        switch (s.Style)
        {
            case ButtonStyle.Filled:
                if (saturated)
                {
                    // 135deg gradient (C500 -> C600) + coloured glow, brightening on hover, dipping on press.
                    Color top = OrigamiRamp.LerpColor(ramp.C500, ramp.C700, 0.22f * s.HoverT);
                    Color bot = OrigamiRamp.LerpColor(ramp.C600, ramp.C700, 0.22f * s.HoverT);
                    top = OrigamiRamp.LerpColor(top, ramp.C400, 0.5f * s.PressT);
                    bot = OrigamiRamp.LerpColor(bot, ramp.C400, 0.5f * s.PressT);
                    r.BgTop = top; r.BgBottom = bot; r.Gradient = true;
                    r.Label = ink.C700;
                    r.Glow = Alpha(ramp.C500, 0.45f + 0.2f * s.HoverT); r.DrawGlow = true;
                    r.Lift = true;
                }
                else
                {
                    // Secondary: raised neutral surface + subtle border that warms toward the accent on hover.
                    Color bg = OrigamiRamp.LerpColor(neutral.C500, neutral.C600, s.HoverT);
                    r.BgTop = r.BgBottom = bg;
                    r.Border = OrigamiRamp.LerpColor(neutral.C200, primary.C500, 0.5f * s.HoverT);
                    r.DrawBorder = true;
                    r.Label = ink.C500;
                }
                break;

            case ButtonStyle.Outline:
                r.BgTop = r.BgBottom = Alpha(saturated ? ramp.C500 : neutral.C500, 0.16f * s.HoverT);
                r.Border = saturated ? ramp.C500 : neutral.C500;
                r.DrawBorder = true;
                r.Label = saturated ? ramp.C600 : ink.C500;
                break;

            case ButtonStyle.Ghost:
                Color ghostTint = saturated ? ramp.C500 : primary.C500; // accent hover for the neutral ghost
                r.BgTop = r.BgBottom = Alpha(ghostTint, 0.14f * s.HoverT);
                r.Label = saturated ? ramp.C600 : OrigamiRamp.LerpColor(ink.C400, ink.C500, s.HoverT);
                break;

            case ButtonStyle.Soft:
                // Tinted fill + coloured rim + coloured label (the prototype's "Delete" button).
                Color tint = saturated ? ramp.C500 : neutral.C700;
                r.BgTop = r.BgBottom = Alpha(tint, 0.14f + 0.08f * s.HoverT);
                r.Border = Alpha(tint, 0.30f);
                r.DrawBorder = true;
                r.Label = saturated ? ramp.C500 : ink.C500;
                break;

            case ButtonStyle.Link:
            default:
                r.Label = saturated ? ramp.C600 : ink.C500;
                break;
        }

        if (s.Disabled)
        {
            r.Gradient = false; r.DrawGlow = false; r.Lift = false;
            r.BgTop = MulA(r.BgTop, 0.4f); r.BgBottom = r.BgTop;
            r.Border = MulA(r.Border, 0.4f);
            r.Label = Alpha(r.Label, 0.4f);
        }
        return r;
    }

    /// <summary>Feathered coloured glow under a filled button (emulates the prototype box-shadow).</summary>
    private static void PaintGlow(Canvas canvas, float x, float y, float w, float h, float rounding, Color glow, float offY, float blur, float spread)
    {
        float cx = x + w * 0.5f, cy = y + h * 0.5f + offY;
        float bw = w + spread * 2f, bh = h + spread * 2f;
        canvas.SaveState();
        canvas.SetBoxBrush(cx, cy, bw, bh, rounding, blur, glow, Color.FromArgb(0, glow.R, glow.G, glow.B));
        canvas.BeginPath();
        float pad = blur + 6f;
        canvas.Rect(x - pad, y + offY - pad, w + pad * 2f, h + pad * 2f);
        canvas.Fill();
        canvas.RestoreState();
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

}
