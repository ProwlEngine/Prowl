// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// A menu item for the Origami app bar. Supports nested submenus, separators,
/// checked state, enabled state, and dynamic labels.
/// </summary>
public sealed class AppMenuItem
{
    public string Label;
    public Action? OnClick;
    public List<AppMenuItem> SubItems = [];
    public bool IsSeparator;
    public Func<bool>? IsCheckedFunc;
    public Func<bool>? IsEnabledFunc;
    public Func<string>? DynamicLabelFunc;
    public string Icon = "";
    private bool _enabled = true;

    public bool IsEnabled { get => IsEnabledFunc?.Invoke() ?? _enabled; set => _enabled = value; }
    public string DisplayLabel => DynamicLabelFunc?.Invoke() ?? Label;
    public bool IsChecked => IsCheckedFunc?.Invoke() ?? false;
    public bool HasSubItems => SubItems.Count > 0;

    public AppMenuItem(string label, Action? onClick = null) { Label = label; OnClick = onClick; }
    public static AppMenuItem Separator() => new("") { IsSeparator = true };
}

/// <summary>
/// Fluent builder for an Origami application bar (menubar or footer/statusbar).
/// Supports three sections: left, center, right. The left section can hold menu items
/// that open dropdown menus. Center and right sections accept arbitrary content callbacks.
///
/// Usage:
/// <code>
/// Origami.AppBar(paper, "main_menu")
///     .Menu("File", fileMenuItems)
///     .Menu("Edit", editMenuItems)
///     .Center(p => { /* flap, play buttons */ })
///     .Right(p => { /* version label, FPS */ })
///     .Show();
/// </code>
/// </summary>
public sealed class AppBarBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;

    private readonly List<(string Label, List<AppMenuItem> Items)> _menus = [];
    private Action<Paper>? _leftContent;
    private Action<Paper>? _centerContent;
    private Action<Paper>? _rightContent;
    private float _height = 28f;
    private bool _bottom; // true = position at bottom of screen
    private Color? _bgOverride;

    internal AppBarBuilder(Paper paper, string id, OrigamiTheme theme)
    {
        _paper = paper; _id = id; _theme = theme;
    }

    /// <summary>Add a top-level menu with dropdown items.</summary>
    public AppBarBuilder Menu(string label, List<AppMenuItem> items) { _menus.Add((label, items)); return this; }

    /// <summary>Custom content drawn in the left section (after menus).</summary>
    public AppBarBuilder Left(Action<Paper> draw) { _leftContent = draw; return this; }

    /// <summary>Custom content drawn centered in the bar.</summary>
    public AppBarBuilder Center(Action<Paper> draw) { _centerContent = draw; return this; }

    /// <summary>Custom content drawn right-aligned in the bar.</summary>
    public AppBarBuilder Right(Action<Paper> draw) { _rightContent = draw; return this; }

    /// <summary>Bar height in pixels. Default 28.</summary>
    public AppBarBuilder Height(float h) { _height = h; return this; }

    /// <summary>Position at the bottom of the screen instead of the top.</summary>
    public AppBarBuilder Bottom() { _bottom = true; return this; }

    /// <summary>Override the background color.</summary>
    public AppBarBuilder Background(Color color) { _bgOverride = color; return this; }

    public void Show()
    {
        var font = _theme.Font;
        var ink = _theme.Ink;
        var icons = _theme.Icons;
        var metrics = _theme.Metrics;
        if (font == null) return;

        float posY = _bottom ? _paper.Height - _height : 0;
        var bg = _bgOverride ?? _theme.Neutral.C200;

        using (_paper.Row(_id)
            .PositionType(PositionType.SelfDirected)
            .Position(0, posY)
            .Size(_paper.Percent(100), _height)
            .BackgroundColor(bg)
            .ChildLeft(metrics.Padding).RowBetween(0)
            .Enter())
        {
            var barHandle = _paper.CurrentParent;

            // ── Left: Menus ─────────────────────────────────
            float menuPad = metrics.HeaderPadX + metrics.Spacing;
            int openMenuIdx = _paper.GetElementStorage(barHandle, "openMenu", -1);

            for (int i = 0; i < _menus.Count; i++)
            {
                int idx = i;
                var (label, items) = _menus[i];
                bool isOpen = openMenuIdx == idx;

                float textW = (float)_paper.MeasureText(label, metrics.FontSize, font).X;
                float btnW = textW + menuPad * 2;

                using (_paper.Box($"{_id}_m_{idx}")
                    .Height(_height).Width(btnW)
                    .BackgroundColor(isOpen ? ink.C200 : Color.Transparent)
                    .Hovered.BackgroundColor(ink.C200).End()
                    .Rounded(metrics.Rounding)
                    .OnClick(idx, (ci, _) =>
                    {
                        int cur = _paper.GetElementStorage(barHandle, "openMenu", -1);
                        _paper.SetElementStorage(barHandle, "openMenu", cur == ci ? -1 : ci);
                    })
                    .Enter())
                {
                    // Draw label via canvas for crisp centering
                    var capturedLabel = label;
                    var capturedTextW = textW;
                    _paper.Draw((canvas, rect) =>
                    {
                        float x = (float)rect.Min.X + ((float)rect.Size.X - capturedTextW) * 0.5f;
                        var sz = canvas.MeasureText(capturedLabel, metrics.FontSize, font);
                        float y = (float)rect.Min.Y + ((float)rect.Size.Y - (float)sz.Y) * 0.5f;
                        canvas.DrawText(capturedLabel, x, y, ink.C500, metrics.FontSize, font);
                    });

                    // Hover-switch while a menu is open
                    if (openMenuIdx >= 0 && openMenuIdx != idx && _paper.IsParentHovered)
                        _paper.SetElementStorage(barHandle, "openMenu", idx);
                }
            }

            // Left custom content
            _leftContent?.Invoke(_paper);

            // ── Center ──────────────────────────────────────
            if (_centerContent != null)
            {
                _paper.Box($"{_id}_csp").Width(UnitValue.Stretch()); // left spacer
                _centerContent(_paper);
                _paper.Box($"{_id}_csp2").Width(UnitValue.Stretch()); // right spacer
            }
            else
            {
                _paper.Box($"{_id}_sp").Width(UnitValue.Stretch()); // fill remaining
            }

            // ── Right ───────────────────────────────────────
            _rightContent?.Invoke(_paper);

            // ── Dropdown rendering (outside the row content but reads state) ────
            int openMenu = _paper.GetElementStorage(barHandle, "openMenu", -1);
            if (openMenu >= 0 && openMenu < _menus.Count)
            {
                var openItems = _menus[openMenu].Items;
                if (openItems.Count > 0)
                {
                    // Backdrop
                    _paper.Box($"{_id}_dd_bg")
                        .PositionType(PositionType.SelfDirected)
                        .Position(-9999, -9999).Size(99999, 99999)
                        .BackgroundColor(Color.FromArgb(85, 0, 0, 0))
                        .Layer(Layer.Topmost)
                        .StopEventPropagation()
                        .OnClick(0, (_, _) => _paper.SetElementStorage(barHandle, "openMenu", -1));

                    // Calculate X position from measured menu button widths
                    float ddX = metrics.Padding;
                    for (int i = 0; i < openMenu; i++)
                    {
                        float tw = (float)_paper.MeasureText(_menus[i].Label, metrics.FontSize, font).X;
                        ddX += tw + menuPad * 2;
                    }

                    float ddY = _bottom ? -1 : _height - 1; // above for bottom bar, below for top

                    RenderDropdown(_paper, $"{_id}_dd", openItems, ddX,
                        _bottom ? 0 : ddY, font, ink, _theme,
                        () => _paper.SetElementStorage(barHandle, "openMenu", -1),
                        _bottom);
                }
            }
        }
    }

    // ── Dropdown rendering ───────────────────────────────────

    private const float DropdownW = 220f;

    private static void RenderDropdown(Paper paper, string id, List<AppMenuItem> items,
        float x, float y, Scribe.FontFile font, OrigamiRamp ink, OrigamiTheme theme,
        Action close, bool openUpward = false)
    {
        var m = theme.Metrics;

        using (paper.Column(id)
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(DropdownW).Height(UnitValue.Auto)
            .BackgroundColor(theme.Neutral.C300)
            .BorderColor(ink.C200).BorderWidth(1)
            .Rounded(m.ContainerRounding)
            .Padding(m.PaddingSmall, m.PaddingSmall, m.PaddingSmall, m.PaddingSmall)
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .BoxShadow(0, 2, 16, -4, Color.FromArgb(100, 0, 0, 0))
            .StopEventPropagation()
            .Enter())
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int idx = i;

                if (item.IsSeparator)
                {
                    paper.Box($"{id}_sep_{idx}").Height(1).Margin(m.Spacing, m.Spacing, m.Spacing, m.Spacing)
                        .BackgroundColor(ink.C200);
                    continue;
                }

                bool enabled = item.IsEnabled;
                var textColor = enabled ? ink.C500 : ink.C300;
                string label = item.DisplayLabel;

                var row = paper.Row($"{id}_i_{idx}").Height(m.RowHeight)
                    .Rounded(m.SmallRounding)
                    .Hovered.BackgroundColor(enabled ? theme.Primary.C400 : Color.Transparent).End();

                if (enabled && item.OnClick != null && !item.HasSubItems)
                    row.OnClick(item, (it, _) => { it.OnClick?.Invoke(); close(); });

                using (row.Enter())
                {
                    // Check mark
                    paper.Box($"{id}_chk_{idx}").Width(m.RowHeight).Height(m.RowHeight)
                        .Text(item.IsChecked ? theme.Icons.Check : "", font)
                        .TextColor(textColor).FontSize(m.FontSize - 1)
                        .Alignment(TextAlignment.MiddleCenter);

                    // Icon
                    if (!string.IsNullOrEmpty(item.Icon))
                    {
                        paper.Box($"{id}_ico_{idx}").Width(m.IconBoxWidth).Height(m.RowHeight)
                            .Text(item.Icon, font).TextColor(textColor)
                            .FontSize(m.FontSize - 1)
                            .Alignment(TextAlignment.MiddleCenter);
                    }

                    // Label
                    paper.Box($"{id}_lbl_{idx}")
                        .Width(UnitValue.Stretch()).Height(m.RowHeight)
                        .Text(label, font).TextColor(textColor)
                        .FontSize(m.FontSize)
                        .Alignment(TextAlignment.MiddleLeft);

                    // Submenu arrow
                    if (item.HasSubItems)
                    {
                        paper.Box($"{id}_arr_{idx}").Width(m.IconBoxWidth).Height(m.RowHeight)
                            .Text(theme.Icons.ChevronRight, font).TextColor(ink.C400)
                            .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

                        if (paper.IsParentHovered)
                            RenderDropdown(paper, $"{id}_s_{idx}", item.SubItems,
                                DropdownW - m.SpacingLarge, 0, font, ink, theme, close);
                    }
                }
            }
        }
    }
}
