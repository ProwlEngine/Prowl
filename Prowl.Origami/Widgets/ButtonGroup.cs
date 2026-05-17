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

/// <summary>
/// Segmented control — a row of buttons sharing borders, where one is selected and the
/// others read as siblings. Construct via <c>Origami.ButtonGroup</c>; chain
/// <see cref="Item(string, string?, string?)"/> for each segment; call <see cref="Show"/>.
/// </summary>
/// <remarks>
/// The selected segment paints with the variant ramp's fill; unselected segments stay
/// neutral. The whole group paints inside one outer rounded-rect border so the segments
/// share corners cleanly.
/// </remarks>
public sealed class ButtonGroupBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly int _selectedIndex;
    private readonly Action<int> _setter;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private float _height = 26f;
    private UnitValue? _width;
    private bool _stretch;
    private float? _roundingOverride;
    private bool _disabled;

    private readonly List<ButtonGroupItem> _items = new();

    internal ButtonGroupBuilder(Paper paper, string id, int selectedIndex, Action<int> setter, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _selectedIndex = selectedIndex;
    }

    // ── Variant + sizing ──────────────────────────────────────────────

    public ButtonGroupBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public ButtonGroupBuilder Primary() => Variant(OrigamiVariant.Primary);
    public ButtonGroupBuilder Success() => Variant(OrigamiVariant.Success);
    public ButtonGroupBuilder Warning() => Variant(OrigamiVariant.Warning);
    public ButtonGroupBuilder Danger()  => Variant(OrigamiVariant.Danger);
    public ButtonGroupBuilder Info()    => Variant(OrigamiVariant.Info);
    public ButtonGroupBuilder Subtle()  => Variant(OrigamiVariant.Subtle);

    public ButtonGroupBuilder Width(UnitValue w) { _width = w; return this; }
    public ButtonGroupBuilder Width(float w) { _width = w; return this; }
    public ButtonGroupBuilder Height(float h) { _height = MathF.Max(20, h); return this; }
    public ButtonGroupBuilder Small()  { _height = 22; return this; }
    public ButtonGroupBuilder Medium() { _height = 26; return this; }
    public ButtonGroupBuilder Large()  { _height = 34; return this; }
    public ButtonGroupBuilder FullWidth() { _stretch = true; _width = UnitValue.Stretch(); return this; }
    public ButtonGroupBuilder Rounding(float radius) { _roundingOverride = radius; return this; }

    public ButtonGroupBuilder Disabled(bool disabled = true) { _disabled = disabled; return this; }

    // ── Items ─────────────────────────────────────────────────────────

    /// <summary>Append an item. The index used for selection is its position in the call order.</summary>
    public ButtonGroupBuilder Item(string label, string? leadingIcon = null, string? tooltip = null)
    {
        _items.Add(new ButtonGroupItem(label, leadingIcon, tooltip, true));
        return this;
    }

    /// <summary>Append an explicitly disabled item.</summary>
    public ButtonGroupBuilder DisabledItem(string label, string? leadingIcon = null, string? tooltip = null)
    {
        _items.Add(new ButtonGroupItem(label, leadingIcon, tooltip, false));
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
        var fillRamp = (isDefault || isSubtle) ? _theme.Neutral : ramp;
        float rounding = _roundingOverride ?? metrics.Rounding;

        UnitValue widthValue = _width ?? UnitValue.Auto;
        var groupBox = _paper.Row(_id)
            .Width(widthValue)
            .Height(_height);

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
                float r0 = isFirst ? rounding : 0;
                float r1 = isLast  ? rounding : 0;

                var seg = _paper.Box($"{_id}_seg_{i}")
                    .Width(_stretch ? UnitValue.Stretch() : UnitValue.Auto)
                    .Height(_height)
                    .Rounded(r0, r1, r1, r0);

                if (itemEnabled)
                {
                    seg.TabIndex(0);
                    seg.OnClick(_ => _setter(idx));
                }

                using (seg.Enter())
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
                        IsSelected = isSelected,
                        IsHovered = isHovered,
                        IsPressed = isPressed,
                        IsDisabled = !itemEnabled,
                        HoverT = hoverT,
                        PressT = pressT,
                        FocusT = focusT,
                        IsFirst = isFirst,
                        IsLast = isLast,
                        Rounding = rounding,
                        Label = item.Label,
                        LeadingIcon = item.LeadingIcon,
                        Theme = _theme,
                        FillRamp = fillRamp,
                        Ink = ink,
                        Font = font,
                        FontSize = metrics.FontSize,
                        IsDefault = isDefault,
                        IsSubtle = isSubtle,
                    };

                    _paper.Draw((canvas, rect) => PaintSegment(canvas, rect, in snapshot));

                    // Tooltip overlay (Topmost) when an item declares one.
                    if (!string.IsNullOrEmpty(item.Tooltip) && font != null)
                        DrawSegmentTooltip(handle, item.Tooltip!, font, metrics.FontSize - 1f);
                }
            }
        }
    }

    // ── Painting ───────────────────────────────────────────────────────

    private struct SegmentSnapshot
    {
        public bool IsSelected;
        public bool IsHovered;
        public bool IsPressed;
        public bool IsDisabled;
        public float HoverT;
        public float PressT;
        public float FocusT;
        public bool IsFirst;
        public bool IsLast;
        public float Rounding;
        public string Label;
        public string? LeadingIcon;
        public OrigamiTheme Theme;
        public OrigamiRamp FillRamp;
        public OrigamiRamp Ink;
        public Prowl.Scribe.FontFile? Font;
        public float FontSize;
        public bool IsDefault;
        public bool IsSubtle;
    }

    private static void PaintSegment(Canvas canvas, Rect rect, in SegmentSnapshot s)
    {
        float x = (float)rect.Min.X;
        float y = (float)rect.Min.Y;
        float w = (float)rect.Size.X;
        float h = (float)rect.Size.Y;

        // Backing for unselected segments — neutral, slightly darker on hover.
        Color baseFill = s.Theme.Neutral.C200;
        Color hoverFill = s.Theme.Neutral.C300;
        Color selectedFill = s.IsSubtle ? s.Theme.Neutral.C400 : s.FillRamp.C500;
        Color selectedHover = s.IsSubtle ? s.Theme.Neutral.C500 : s.FillRamp.C600;

        Color bg;
        Color labelCol;
        if (s.IsSelected)
        {
            bg = OrigamiRamp.LerpColor(selectedFill, selectedHover, s.HoverT);
            bg = OrigamiRamp.LerpColor(bg, s.FillRamp.C400, s.PressT * 0.5f);
            labelCol = (s.IsDefault || s.IsSubtle) ? s.Ink.C500 : s.Ink.C700;
        }
        else
        {
            bg = OrigamiRamp.LerpColor(baseFill, hoverFill, s.HoverT);
            labelCol = s.Ink.C500;
        }

        if (s.IsDisabled)
        {
            bg = OrigamiRamp.LerpColor(bg, s.Theme.Neutral.C300, 0.6f);
            labelCol = s.Ink.C300;
        }

        // Per-segment rounding only on the outer corners.
        float r0 = s.IsFirst ? s.Rounding : 0;
        float r1 = s.IsLast  ? s.Rounding : 0;
        canvas.RoundedRectFilled(x, y, w, h, r0, r1, r1, r0, bg);

        // Sibling divider — a 1px inset on the right edge except for the last segment.
        if (!s.IsLast)
        {
            Color div = s.Theme.Neutral.C400;
            canvas.RectFilled(x + w - 1f, y + 3f, 1f, h - 6f, div);
        }

        // Focus ring — sits on top of the segment when focused.
        if (s.FocusT > 0.02f && !s.IsDisabled)
        {
            byte ringA = (byte)Math.Clamp((int)(180 * s.FocusT), 0, 255);
            Color ringCol = (s.IsDefault || s.IsSubtle) ? s.Theme.Primary.C500 : s.FillRamp.C500;
            ringCol = Color.FromArgb(ringA, ringCol.R, ringCol.G, ringCol.B);
            float pad = 1.5f;
            // Outer ring
            canvas.RoundedRectFilled(x - pad, y - pad, w + pad * 2, h + pad * 2,
                r0 + pad, r1 + pad, r1 + pad, r0 + pad, ringCol);
            // Re-paint the segment fill on top (otherwise the ring covers it).
            canvas.RoundedRectFilled(x, y, w, h, r0, r1, r1, r0, bg);
            if (!s.IsLast)
                canvas.RectFilled(x + w - 1f, y + 3f, 1f, h - 6f, s.Theme.Neutral.C400);
        }

        if (s.Font == null) return;

        // Layout: optional leading icon then label, centered.
        float iconSize = s.FontSize;
        float gap = 6f;
        bool drawIcon = !string.IsNullOrEmpty(s.LeadingIcon);
        bool drawLabel = !string.IsNullOrEmpty(s.Label);

        Float2 labelSize = drawLabel ? canvas.MeasureText(s.Label, s.FontSize, s.Font) : new Float2(0, 0);
        float contentW = (drawIcon ? iconSize : 0)
                       + (drawIcon && drawLabel ? gap : 0)
                       + (float)labelSize.X;

        float cx = x + (w - contentW) * 0.5f;
        float cy = y + (h - s.FontSize) * 0.5f;

        if (drawIcon)
        {
            canvas.DrawText(s.LeadingIcon!, cx, y + (h - iconSize) * 0.5f, labelCol, s.FontSize, s.Font);
            cx += iconSize + (drawLabel ? gap : 0);
        }
        if (drawLabel)
            canvas.DrawText(s.Label, cx, cy, labelCol, s.FontSize, s.Font);
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

    private readonly record struct ButtonGroupItem(string Label, string? LeadingIcon, string? Tooltip, bool Enabled);
}
