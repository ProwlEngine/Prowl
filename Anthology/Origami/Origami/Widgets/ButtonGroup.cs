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

/// <summary>Visual style for a button group / segmented control.</summary>
public enum ButtonGroupStyle
{
    /// <summary>Buttons share borders inside one outer rounded frame (prototype <c>.w2bg</c>). Default.</summary>
    Joined,
    /// <summary>Individual rounded pills inside a padded track, selected pill lifts with a glow (prototype <c>.w2seg</c>).</summary>
    Segmented,
}

/// <summary>
/// Segmented control — a row of buttons where one is selected. Construct via
/// <c>Origami.ButtonGroup</c>; chain <see cref="Item(string, string?, string?)"/> for each
/// segment; call <see cref="Show"/>.
/// </summary>
public sealed class ButtonGroupBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly int _selectedIndex;
    private readonly Action<int> _setter;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private ButtonGroupStyle _style = ButtonGroupStyle.Joined;
    private float _height = 30f;
    private UnitValue? _width;
    private bool _stretch;
    private float? _roundingOverride;
    private bool _disabled;

    private readonly List<ButtonGroupItem> _items = new();

    // Prototype glass-in surface (rgba(8,6,14,0.6)) — the recessed track / segment well.

    internal ButtonGroupBuilder(Paper paper, string id, int selectedIndex, Action<int> setter, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _selectedIndex = selectedIndex;
    }

    // ── Variant + style + sizing ──────────────────────────────────────

    public ButtonGroupBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public ButtonGroupBuilder Primary() => Variant(OrigamiVariant.Primary);
    public ButtonGroupBuilder Success() => Variant(OrigamiVariant.Success);
    public ButtonGroupBuilder Warning() => Variant(OrigamiVariant.Warning);
    public ButtonGroupBuilder Danger()  => Variant(OrigamiVariant.Danger);
    public ButtonGroupBuilder Info()    => Variant(OrigamiVariant.Info);
    public ButtonGroupBuilder Subtle()  => Variant(OrigamiVariant.Subtle);

    public ButtonGroupBuilder Style(ButtonGroupStyle style) { _style = style; return this; }
    public ButtonGroupBuilder Joined()    { _style = ButtonGroupStyle.Joined; return this; }
    public ButtonGroupBuilder Segmented() { _style = ButtonGroupStyle.Segmented; _height = 32; return this; }

    public ButtonGroupBuilder Width(UnitValue w) { _width = w; return this; }
    public ButtonGroupBuilder Width(float w) { _width = w; return this; }
    public ButtonGroupBuilder Height(float h) { _height = MathF.Max(20, h); return this; }
    public ButtonGroupBuilder Small()  { _height = 26; return this; }
    public ButtonGroupBuilder Medium() { _height = 30; return this; }
    public ButtonGroupBuilder Large()  { _height = 36; return this; }
    public ButtonGroupBuilder FullWidth() { _stretch = true; _width = UnitValue.Stretch(); return this; }
    public ButtonGroupBuilder Rounding(float radius) { _roundingOverride = radius; return this; }

    public ButtonGroupBuilder Disabled(bool disabled = true) { _disabled = disabled; return this; }

    // ── Items ─────────────────────────────────────────────────────────

    /// <summary>Append an item (optional icon-font glyph).</summary>
    public ButtonGroupBuilder Item(string label, string? leadingIcon = null, string? tooltip = null)
    {
        _items.Add(new ButtonGroupItem(label, leadingIcon, null, tooltip, true));
        return this;
    }

    /// <summary>Append an item with a vector leading icon (host paints into the slot rect).</summary>
    public ButtonGroupBuilder Item(string label, Action<Canvas, Rect> icon, string? tooltip = null)
    {
        _items.Add(new ButtonGroupItem(label, null, icon, tooltip, true));
        return this;
    }

    /// <summary>Append an explicitly disabled item.</summary>
    public ButtonGroupBuilder DisabledItem(string label, string? leadingIcon = null, string? tooltip = null)
    {
        _items.Add(new ButtonGroupItem(label, leadingIcon, null, tooltip, false));
        return this;
    }

    // ── Terminator ─────────────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _disabled = true;
        if (_items.Count == 0) return;

        var ramp = _theme.Get(_variant);
        var ink = _theme.Ink;
        var font = _theme.Font;
        var metrics = _theme.Metrics;
        bool isDefault = _variant == OrigamiVariant.Default;
        bool isSubtle  = _variant == OrigamiVariant.Subtle;
        // Default/Subtle groups select with the accent so the on-state pops (like the prototype).
        var selRamp = (isDefault || isSubtle) ? _theme.Primary : ramp;
        float rounding = _roundingOverride ?? metrics.Rounding;
        bool seg = _style == ButtonGroupStyle.Segmented;
        float padX = seg ? 12f : 13f;
        float segRound = MathF.Max(3f, rounding - 2f);

        UnitValue widthValue = _width ?? UnitValue.Auto;
        var groupBox = _paper.Row(_id).Width(widthValue).Height(_height).Rounded(rounding);

        if (seg)
            groupBox.BackgroundColor(_theme.Glass).BorderWidth(1).BorderColor(_theme.Neutral.C200).Padding(3, 3, 3, 3);
        else
            groupBox.BorderWidth(1).BorderColor(_theme.Neutral.C200).Clip();

        using (groupBox.Enter())
        {
            int count = _items.Count;
            for (int i = 0; i < count; i++)
            {
                var item = _items[i];
                bool isSelected = i == _selectedIndex;
                bool itemEnabled = !_disabled && item.Enabled;

                int idx = i;
                bool isFirst = i == 0;
                bool isLast = i == count - 1;

                float segW = _stretch ? 0 : MeasureSeg(item, font, metrics.FontSize, padX);
                var segBox = _paper.Box($"{_id}_seg_{i}")
                    .Width(_stretch ? UnitValue.Stretch() : segW)
                    .Height(UnitValue.Stretch());

                if (seg)
                    segBox.Rounded(segRound).Margin(isFirst ? 0 : 3, 0, 0, 0);
                else
                    segBox.Rounded(isFirst ? rounding : 0, isLast ? rounding : 0, isLast ? rounding : 0, isFirst ? rounding : 0);

                if (itemEnabled)
                {
                    segBox.TabIndex(0);
                    segBox.OnClick(_ => _setter(idx));
                }

                using (segBox.Enter())
                {
                    var handle = _paper.CurrentParent;
                    bool isHovered = itemEnabled && _paper.IsParentHovered;
                    bool isPressed = itemEnabled && _paper.IsParentActive;
                    bool isFocused = itemEnabled && _paper.IsElementFocused(handle.Data.ID);

                    float hoverT = _paper.AnimateBool(isHovered, 0.10f, id: $"{_id}_h_{i}");
                    float pressT = _paper.AnimateBool(isPressed, 0.06f, id: $"{_id}_p_{i}");
                    float focusT = _paper.AnimateBool(isFocused, 0.12f, id: $"{_id}_f_{i}");

                    if (itemEnabled && isFocused
                        && (_paper.IsKeyPressed(PaperKey.Space) || _paper.IsKeyPressed(PaperKey.Enter)
                            || _paper.IsKeyPressed(PaperKey.KeypadEnter)))
                    {
                        _setter(idx);
                    }

                    var snapshot = new SegmentSnapshot
                    {
                        Segmented = seg,
                        IsSelected = isSelected,
                        IsDisabled = !itemEnabled,
                        HoverT = hoverT,
                        PressT = pressT,
                        FocusT = focusT,
                        IsFirst = isFirst,
                        IsLast = isLast,
                        Rounding = rounding,
                        SegRound = segRound,
                        Label = item.Label,
                        LeadingIcon = item.LeadingIcon,
                        IconDraw = item.IconDraw,
                        Theme = _theme,
                        SelRamp = selRamp,
                        Ink = ink,
                        Font = font,
                        FontSize = metrics.FontSize,
                    };

                    _paper.Draw((canvas, rect) => PaintSegment(canvas, rect, in snapshot));

                    if (!string.IsNullOrEmpty(item.Tooltip) && font != null)
                        DrawSegmentTooltip(handle, item.Tooltip!, font, metrics.FontSize - 1f);
                }
            }
        }
    }

    private float MeasureSeg(ButtonGroupItem item, Prowl.Scribe.FontFile? font, float fontSize, float padX)
    {
        float content = 0f;
        bool hasIcon = !string.IsNullOrEmpty(item.LeadingIcon) || item.IconDraw != null;
        bool hasLabel = !string.IsNullOrEmpty(item.Label);
        if (hasIcon) content += fontSize;
        if (hasLabel)
        {
            content += font != null ? (float)_paper.MeasureText(item.Label, fontSize, font).X : item.Label.Length * fontSize * 0.55f;
            if (hasIcon) content += 6f;
        }
        return content + padX * 2f;
    }

    // ── Painting ───────────────────────────────────────────────────────

    private struct SegmentSnapshot
    {
        public bool Segmented;
        public bool IsSelected;
        public bool IsDisabled;
        public float HoverT;
        public float PressT;
        public float FocusT;
        public bool IsFirst;
        public bool IsLast;
        public float Rounding;
        public float SegRound;
        public string Label;
        public string? LeadingIcon;
        public Action<Canvas, Rect>? IconDraw;
        public OrigamiTheme Theme;
        public OrigamiRamp SelRamp;
        public OrigamiRamp Ink;
        public Prowl.Scribe.FontFile? Font;
        public float FontSize;
    }

    private static void PaintSegment(Canvas canvas, Rect rect, in SegmentSnapshot s)
    {
        float x = (float)rect.Min.X, y = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;

        var neutral = s.Theme.Neutral;
        var accent = s.SelRamp;
        float rr = s.Segmented ? s.SegRound : 0f;
        // Corner radii: segmented pills round all corners; joined rounds only the outer ends.
        float tl = s.Segmented ? rr : (s.IsFirst ? s.Rounding : 0f);
        float tr = s.Segmented ? rr : (s.IsLast  ? s.Rounding : 0f);
        float bl = tl, br = tr;

        Color selBg = accent.C500;
        Color labelCol;

        if (s.IsSelected)
        {
            // Selected pill lifts with a tight downward glow (segmented only, like the prototype).
            if (s.Segmented && !s.IsDisabled)
                PillGlow(canvas, x, y, w, h, rr, selBg);
            Color bg = OrigamiRamp.LerpColor(selBg, accent.C600, s.HoverT);
            bg = OrigamiRamp.LerpColor(bg, accent.C400, s.PressT * 0.5f);
            if (s.IsDisabled) bg = OrigamiRamp.LerpColor(bg, neutral.C400, 0.5f);
            canvas.RoundedRectFilled(x, y, w, h, tl, tr, br, bl, bg);
            labelCol = s.Ink.C700;
        }
        else
        {
            // Unselected: deep glass fill (joined) with an accent-tint overlay on hover.
            if (!s.Segmented)
                canvas.RoundedRectFilled(x, y, w, h, tl, tr, br, bl, Origami.Current.Glass);
            if (s.HoverT > 0.001f)
            {
                Color tint = Color.FromArgb((int)(0.14f * 255 * s.HoverT), selBg.R, selBg.G, selBg.B);
                canvas.RoundedRectFilled(x, y, w, h, tl, tr, br, bl, tint);
            }
            labelCol = OrigamiRamp.LerpColor(s.Ink.C300, s.Ink.C400, s.HoverT);
            if (s.IsDisabled) labelCol = s.Ink.C300;

            // Divider between joined segments.
            if (!s.Segmented && !s.IsLast)
                canvas.RectFilled(x + w - 1f, y + 4f, 1f, h - 8f, neutral.C200);
        }

        // (No focus ring — the solid selected fill is enough; a ring around the segment read as noise.)

        if (s.Font == null) return;

        // Content: optional icon then label, centred.
        float iconSize = s.FontSize;
        const float gap = 6f;
        bool drawIcon = !string.IsNullOrEmpty(s.LeadingIcon) || s.IconDraw != null;
        bool drawLabel = !string.IsNullOrEmpty(s.Label);

        Float2 labelSize = drawLabel ? canvas.MeasureText(s.Label, s.FontSize, s.Font) : new Float2(0, 0);
        float contentW = (drawIcon ? iconSize : 0) + (drawIcon && drawLabel ? gap : 0) + (float)labelSize.X;
        float cx = x + (w - contentW) * 0.5f;
        float cy = y + (h - s.FontSize) * 0.5f;

        if (drawIcon)
        {
            float iy = y + (h - iconSize) * 0.5f;
            if (s.IconDraw != null)
                s.IconDraw(canvas, new Rect(new Float2(cx, iy), new Float2(cx + iconSize, iy + iconSize)));
            else
                canvas.DrawText(s.LeadingIcon!, cx, iy, labelCol, s.FontSize, s.Font);
            cx += iconSize + (drawLabel ? gap : 0);
        }
        if (drawLabel)
            canvas.DrawText(s.Label, cx, cy, labelCol, s.FontSize, s.Font);
    }

    // Tight coloured drop-glow beneath a selected pill (prototype box-shadow: 0 2px 8px -2px).
    // Wrapped in Save/Restore so the box brush never leaks into the following text/fill draws.
    internal static void PillGlow(Canvas canvas, float x, float y, float w, float h, float r, Color c)
    {
        Color glow = Color.FromArgb(140, c.R, c.G, c.B);
        canvas.SaveState();
        canvas.SetBoxBrush(x + w * 0.5f, y + h * 0.5f + 3f, w - 4f, h - 4f, r, 13f, glow, Color.FromArgb(0, c.R, c.G, c.B));
        canvas.BeginPath();
        canvas.Rect(x - 16f, y - 8f, w + 32f, h + 28f);
        canvas.Fill();
        canvas.RestoreState();
    }

    private void DrawSegmentTooltip(ElementHandle segHandle, string text, Prowl.Scribe.FontFile font, float fontSize)
    {
        Color ttBg = _theme.Neutral.C500;
        Color ttFg = _theme.Ink.C500;
        string ttId = $"{_id}_seg_tt_{segHandle.Data.ID}";

        using (_paper.Box(ttId)
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Width(1).Height(1)
            .Layer(Layer.Topmost)
            .HookToParent()
            .IsNotInteractable()
            .Enter())
        {
            bool wantTooltip = _paper.IsElementHovered(segHandle.Data.ID);
            float ttAnim = _paper.AnimateBool(wantTooltip, 0.16f, id: ttId);
            if (ttAnim < 0.01f) return;

            _paper.Draw((canvas, _) =>
            {
                var tr = segHandle.Data.LayoutRect;
                float trX = (float)tr.Min.X;
                float trY = (float)tr.Min.Y;
                float trW = (float)tr.Size.X;

                var ts = canvas.MeasureText(text, fontSize, font);
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
                canvas.DrawText(text, bx + padX, by + padY,
                    Color.FromArgb(aText, ttFg.R, ttFg.G, ttFg.B), fontSize, font);
            });
        }
    }

    private readonly record struct ButtonGroupItem(string Label, string? LeadingIcon, Action<Canvas, Rect>? IconDraw, string? Tooltip, bool Enabled);
}
