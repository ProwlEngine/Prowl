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

/// <summary>Visual style for a tab strip.</summary>
public enum TabsStyle
{
    /// <summary>Flat tabs with an accent underline under the active one (prototype <c>.w2tabs</c>). Default.</summary>
    Underline,
    /// <summary>Rounded pills inside a padded track (prototype <c>.w2tabpills</c>).</summary>
    Pills,
}

/// <summary>
/// A horizontal tab strip. Controlled — the caller owns the selected index and Origami calls
/// the setter on change. Optionally closeable (X per tab) and draggable (for docking hosts).
/// Construct via <c>Origami.Tabs</c>; add tabs with <see cref="Tab(string)"/> overloads; call
/// <see cref="Show"/>.
/// </summary>
public sealed class TabsBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly int _selectedIndex;
    private readonly Action<int> _onSelect;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private TabsStyle _style = TabsStyle.Underline;
    private float _height;
    private UnitValue? _width;

    private Action<int>? _onClose;
    private Action<int, Float2>? _onTabPress;

    private readonly List<TabItem> _items = new();

    internal TabsBuilder(Paper paper, string id, int selectedIndex, Action<int> onSelect, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _onSelect = onSelect ?? throw new ArgumentNullException(nameof(onSelect));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _selectedIndex = selectedIndex;
    }

    // ── Style / variant / sizing ──────────────────────────────────────

    public TabsBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public TabsBuilder Style(TabsStyle s) { _style = s; return this; }
    public TabsBuilder Underline() { _style = TabsStyle.Underline; return this; }
    public TabsBuilder Pills() { _style = TabsStyle.Pills; return this; }
    public TabsBuilder Height(float h) { _height = MathF.Max(20, h); return this; }
    public TabsBuilder Width(UnitValue w) { _width = w; return this; }

    // ── Tabs ──────────────────────────────────────────────────────────

    public TabsBuilder Tab(string label) { _items.Add(new TabItem(label, null, null, null)); return this; }
    public TabsBuilder Tab(string label, IOrigamiIcon icon) { _items.Add(new TabItem(label, icon, null, null)); return this; }
    public TabsBuilder Tab(string label, IOrigamiIcon? icon, string? badge) { _items.Add(new TabItem(label, icon, null, badge)); return this; }
    public TabsBuilder GlyphTab(string label, string glyph, string? badge = null) { _items.Add(new TabItem(label, null, glyph, badge)); return this; }

    // ── Optional behaviours ───────────────────────────────────────────

    /// <summary>Show a close affordance on each tab; the callback fires with the tab index.</summary>
    public TabsBuilder Closeable(Action<int> onClose) { _onClose = onClose; return this; }

    /// <summary>Fires when a tab is pressed (pointer-down) — docking hosts use this to start a drag-out.</summary>
    public TabsBuilder OnTabPress(Action<int, Float2> onPress) { _onTabPress = onPress; return this; }

    // ── Terminator ─────────────────────────────────────────────────────

    public void Show()
    {
        if (_items.Count == 0) return;
        var metrics = _theme.Metrics;
        var font = _theme.Font;
        bool isDefault = _variant == OrigamiVariant.Default;
        bool isSubtle = _variant == OrigamiVariant.Subtle;
        var accent = (isDefault || isSubtle) ? _theme.Primary : _theme.Get(_variant);
        float fs = metrics.FontSize;

        if (_style == TabsStyle.Pills)
            ShowPills(fs, accent, font);
        else
            ShowUnderline(fs, accent, font);
    }

    // ── Underline ─────────────────────────────────────────────────────

    private void ShowUnderline(float fs, OrigamiRamp accent, Prowl.Scribe.FontFile? font)
    {
        float th = _height > 0 ? _height : fs + 16f;
        const float padX = 13f;

        using (_paper.Row(_id).Width(_width ?? UnitValue.Auto).Height(th).Enter())
        {
            _paper.Box($"{_id}_bb").PositionType(PositionType.SelfDirected)
                .Left(0).Top(th - 1).Width(UnitValue.Stretch()).Height(1)
                .IsNotInteractable().BackgroundColor(_theme.Neutral.C200);

            for (int i = 0; i < _items.Count; i++)
                Tab(i, th, padX, fs, accent, font, pills: false);
        }
    }

    // ── Pills ─────────────────────────────────────────────────────────

    private void ShowPills(float fs, OrigamiRamp accent, Prowl.Scribe.FontFile? font)
    {
        float th = _height > 0 ? _height : fs + 16f;
        const float padX = 14f;

        using (_paper.Row(_id).Width(_width ?? UnitValue.Auto).Height(th)
            .Rounded(th * 0.5f).BackgroundColor(_theme.Glass) // glass-in track
            .BorderWidth(1).BorderColor(_theme.Neutral.C200).Padding(3, 3, 3, 3).Enter())
        {
            for (int i = 0; i < _items.Count; i++)
                Tab(i, th - 6f, padX, fs, accent, font, pills: true);
        }
    }

    // ── Shared tab ────────────────────────────────────────────────────

    private void Tab(int i, float th, float padX, float fs, OrigamiRamp accent, Prowl.Scribe.FontFile? font, bool pills)
    {
        var item = _items[i];
        bool isSelected = i == _selectedIndex;
        bool closeable = _onClose != null;
        float closeSize = closeable ? 15f : 0f;

        float badgeW = 0f;
        if (!string.IsNullOrEmpty(item.Badge) && font != null)
            badgeW = 8f + (float)_paper.MeasureText(item.Badge!, fs - 2f, font).X + 14f;

        bool hasIcon = item.Icon != null || !string.IsNullOrEmpty(item.Glyph);
        float labelW = font != null && !string.IsNullOrEmpty(item.Label) ? (float)_paper.MeasureText(item.Label, fs, font).X : 0f;
        float tabW = padX * 2f + (hasIcon ? fs + 7f : 0f) + labelW + badgeW + (closeable ? closeSize + 4f : 0f);

        int idx = i;
        var tabBox = _paper.Box($"{_id}_t{i}")
            .Width(tabW).Height(pills ? UnitValue.Stretch() : th)
            .OnClick(_ => _onSelect(idx));
        if (pills) tabBox.Rounded(th * 0.5f).Margin(i == 0 ? 0 : 4, 0, 0, 0);
        if (_onTabPress != null)
            tabBox.OnPress(e => _onTabPress!(idx, e.PointerPosition));

        using (tabBox.Enter())
        {
            var handle = _paper.CurrentParent;
            bool hovered = _paper.IsParentHovered;
            float hoverT = _paper.AnimateBool(hovered, 0.10f, id: $"{_id}_h{i}");
            float selT = _paper.AnimateBool(isSelected, 0.15f, id: $"{_id}_s{i}");

            var snap = new TabSnapshot
            {
                Pills = pills, PadX = padX, FontSize = fs, HoverT = hoverT, SelT = selT,
                Label = item.Label, Icon = item.Icon, Glyph = item.Glyph, Badge = item.Badge,
                CloseInset = closeable ? closeSize + 4f : 0f,
                Theme = _theme, Accent = accent, Font = font,
            };
            _paper.Draw((canvas, rect) => PaintTab(canvas, rect, in snap));

            if (closeable && (isSelected || hovered))
            {
                var onClose = _onClose!;
                _paper.Box($"{_id}_x{i}")
                    .PositionType(PositionType.SelfDirected)
                    .Position(tabW - closeSize - padX + 4f, (th - closeSize) * 0.5f)
                    .Size(closeSize, closeSize).Rounded(closeSize * 0.5f)
                    .Hovered.BackgroundColor(Color.FromArgb(60, 168, 85, 247)).End()
                    .StopEventPropagation()
                    .OnClick(_ => onClose(idx))
                    .OnPostLayout((h2, r) => _paper.Draw(ref h2, (canvas, rr) =>
                    {
                        float cx = (float)(rr.Min.X + rr.Size.X / 2), cy = (float)(rr.Min.Y + rr.Size.Y / 2);
                        canvas.SaveState();
                        canvas.SetStrokeColor(_theme.Ink.C400);
                        canvas.SetStrokeWidth(1.2f);
                        canvas.SetStrokeCap(EndCapStyle.Round);
                        canvas.BeginPath();
                        canvas.MoveTo(cx - 3, cy - 3); canvas.LineTo(cx + 3, cy + 3);
                        canvas.MoveTo(cx + 3, cy - 3); canvas.LineTo(cx - 3, cy + 3);
                        canvas.Stroke();
                        canvas.RestoreState();
                    }));
            }
        }
    }

    private struct TabSnapshot
    {
        public bool Pills;
        public float PadX, FontSize, HoverT, SelT, CloseInset;
        public string Label;
        public IOrigamiIcon? Icon;
        public string? Glyph, Badge;
        public OrigamiTheme Theme;
        public OrigamiRamp Accent;
        public Prowl.Scribe.FontFile? Font;
    }

    private static void PaintTab(Canvas canvas, Rect rect, in TabSnapshot s)
    {
        float x = (float)rect.Min.X, y = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        var ink = s.Theme.Ink;
        Color acc = s.Accent.C500;

        Color labelCol;
        if (s.Pills)
        {
            // Selected pill: accent fill + tight drop-glow. Unselected: transparent + hover tint.
            if (s.SelT > 0.01f)
                Glow(canvas, x, y, w, h, h * 0.5f, acc, s.SelT);
            if (s.HoverT > 0.001f && s.SelT < 0.99f)
                canvas.RoundedRectFilled(x, y, w, h, h * 0.5f, Color.FromArgb((int)(0.12f * 255 * s.HoverT * (1f - s.SelT)), acc.R, acc.G, acc.B));
            if (s.SelT > 0.01f)
                canvas.RoundedRectFilled(x, y, w, h, h * 0.5f, Color.FromArgb((int)(255 * s.SelT), acc.R, acc.G, acc.B));
            labelCol = OrigamiRamp.LerpColor(OrigamiRamp.LerpColor(ink.C300, ink.C400, s.HoverT), ink.C700, s.SelT);
        }
        else
        {
            labelCol = OrigamiRamp.LerpColor(OrigamiRamp.LerpColor(ink.C300, ink.C400, s.HoverT), ink.C500, s.SelT);
            // Accent underline for the active tab (soft glow behind, crisp line on top).
            if (s.SelT > 0.01f)
            {
                float uy = y + h - 2f;
                Color glow = Color.FromArgb((int)(150 * s.SelT), acc.R, acc.G, acc.B);
                canvas.SaveState();
                canvas.SetBoxBrush(x + w * 0.5f, uy + 1f, w - 2f, 2f, 1f, 10f, glow, Color.FromArgb(0, acc.R, acc.G, acc.B));
                canvas.BeginPath(); canvas.Rect(x - 14f, uy - 11f, w + 28f, 24f); canvas.Fill();
                canvas.RestoreState();
                canvas.RectFilled(x, uy, w, 2f, Color.FromArgb((int)(255 * s.SelT), acc.R, acc.G, acc.B));
            }
        }

        if (s.Font == null) return;

        // Content row: icon, label, badge — left group, close reserved on the right.
        float iconSize = s.FontSize;
        const float gap = 7f;
        bool hasIcon = s.Icon != null || !string.IsNullOrEmpty(s.Glyph);
        Float2 labelSize = !string.IsNullOrEmpty(s.Label) ? canvas.MeasureText(s.Label, s.FontSize, s.Font) : new Float2(0, 0);
        float badgeW = 0f, badgeTextW = 0f;
        float bfs = s.FontSize - 2f;
        if (!string.IsNullOrEmpty(s.Badge))
        {
            badgeTextW = (float)canvas.MeasureText(s.Badge!, bfs, s.Font).X;
            badgeW = 8f + badgeTextW + 14f;
        }

        float contentW = (hasIcon ? iconSize + gap : 0f) + (float)labelSize.X + badgeW;
        float avail = w - s.CloseInset;
        float cx = x + (avail - contentW) * 0.5f;
        if (cx < x + s.PadX) cx = x + s.PadX;
        float cy = y + (h - s.FontSize) * 0.5f;

        if (hasIcon)
        {
            float iy = y + (h - iconSize) * 0.5f;
            if (s.Icon != null) s.Icon.Draw(canvas, new Rect(new Float2(cx, iy), new Float2(cx + iconSize, iy + iconSize)), labelCol);
            else canvas.DrawText(s.Glyph!, cx, iy, labelCol, s.FontSize, s.Font);
            cx += iconSize + gap;
        }
        if (!string.IsNullOrEmpty(s.Label))
        {
            canvas.DrawText(s.Label, cx, cy, labelCol, s.FontSize, s.Font);
            cx += (float)labelSize.X;
        }
        if (!string.IsNullOrEmpty(s.Badge))
        {
            float bh = bfs + 7f, bw = badgeTextW + 14f;
            float bx = cx + 8f, by = y + (h - bh) * 0.5f;
            canvas.RoundedRectFilled(bx, by, bw, bh, bh * 0.5f, Color.FromArgb(45, acc.R, acc.G, acc.B));
            var bts = canvas.MeasureText(s.Badge!, bfs, s.Font);
            canvas.DrawText(s.Badge!, bx + (bw - (float)bts.X) * 0.5f, by + (bh - (float)bts.Y) * 0.5f, s.Accent.C700, bfs, s.Font);
        }
    }

    // Tight coloured drop-glow beneath a selected pill; Save/Restore so the box brush never
    // leaks into the fill/text draws that follow.
    private static void Glow(Canvas canvas, float x, float y, float w, float h, float r, Color c, float t)
    {
        Color glow = Color.FromArgb((int)(140 * t), c.R, c.G, c.B);
        canvas.SaveState();
        canvas.SetBoxBrush(x + w * 0.5f, y + h * 0.5f + 3f, w - 4f, h - 4f, r, 13f, glow, Color.FromArgb(0, c.R, c.G, c.B));
        canvas.BeginPath();
        canvas.Rect(x - 16f, y - 8f, w + 32f, h + 28f);
        canvas.Fill();
        canvas.RestoreState();
    }

    private readonly record struct TabItem(string Label, IOrigamiIcon? Icon, string? Glyph, string? Badge);
}
