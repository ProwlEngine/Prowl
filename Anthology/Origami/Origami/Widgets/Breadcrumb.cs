// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Style of separator drawn between breadcrumb segments.
/// </summary>
public enum BreadcrumbSeparator
{
    /// <summary>Chevron right glyph between segments.</summary>
    Chevron,
    /// <summary>Forward slash "/" between segments.</summary>
    Slash,
    /// <summary>Backslash "\" between segments.</summary>
    Backslash,
    /// <summary>Dot between segments.</summary>
    Dot,
    /// <summary>Arrow right between segments.</summary>
    Arrow,
    /// <summary>Custom string set via CustomSeparator().</summary>
    Custom,
    /// <summary>No separator.</summary>
    None,
}

/// <summary>
/// A single segment in a breadcrumb trail.
/// </summary>
public sealed class BreadcrumbItem
{
    public string Label;
    public string Icon;
    public object? UserData;

    /// <summary>Optional leading icon, drawn tinted to the crumb's text colour.</summary>
    public IOrigamiIcon? IconDraw;

    public BreadcrumbItem(string label, string icon = "", object? userData = null)
    {
        Label = label;
        Icon = icon;
        UserData = userData;
    }

    public BreadcrumbItem(string label, IOrigamiIcon iconDraw, object? userData = null)
    {
        Label = label;
        Icon = string.Empty;
        IconDraw = iconDraw;
        UserData = userData;
    }
}

/// <summary>
/// Fluent builder for a breadcrumb navigation trail. Construct via
/// <see cref="Origami.Breadcrumb(Paper, string, IReadOnlyList{BreadcrumbItem}, Action{BreadcrumbItem})"/>
/// and call <see cref="Show"/> to render.
/// </summary>
public sealed class BreadcrumbBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly IReadOnlyList<BreadcrumbItem> _items;
    private readonly Action<BreadcrumbItem> _onClick;

    private BreadcrumbSeparator _separator = BreadcrumbSeparator.Chevron;
    private string _customSeparator = "/";
    private UnitValue _width = UnitValue.Stretch();
    private float _height = 24f;
    private bool _showIcons = true;
    private bool _highlightLast = true;
    private bool _truncateFirst;
    private int? _activeIndex;
    private OrigamiVariant _variant = OrigamiVariant.Default;

    internal BreadcrumbBuilder(Paper paper, string id, IReadOnlyList<BreadcrumbItem> items,
        Action<BreadcrumbItem> onClick, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _items = items;
        _onClick = onClick;
        _theme = theme;
    }

    // ── Separator style ────────────────────────────────────────

    /// <summary>Set the separator style between segments (default Chevron).</summary>
    public BreadcrumbBuilder Separator(BreadcrumbSeparator sep) { _separator = sep; return this; }

    /// <summary>Use chevron right glyphs as separators.</summary>
    public BreadcrumbBuilder Chevrons() => Separator(BreadcrumbSeparator.Chevron);

    /// <summary>Use forward slashes as separators.</summary>
    public BreadcrumbBuilder Slashes() => Separator(BreadcrumbSeparator.Slash);

    /// <summary>Use dots as separators.</summary>
    public BreadcrumbBuilder Dots() => Separator(BreadcrumbSeparator.Dot);

    /// <summary>Use arrows as separators.</summary>
    public BreadcrumbBuilder Arrows() => Separator(BreadcrumbSeparator.Arrow);

    /// <summary>Use a custom string as separator.</summary>
    public BreadcrumbBuilder CustomSeparator(string sep)
    {
        _separator = BreadcrumbSeparator.Custom;
        _customSeparator = sep;
        return this;
    }

    /// <summary>Hide separators entirely.</summary>
    public BreadcrumbBuilder NoSeparator() => Separator(BreadcrumbSeparator.None);

    // ── Sizing ─────────────────────────────────────────────────

    /// <summary>Override breadcrumb width (default Stretch).</summary>
    public BreadcrumbBuilder Width(UnitValue width) { _width = width; return this; }

    /// <summary>Override breadcrumb height (default 24).</summary>
    public BreadcrumbBuilder Height(float height) { _height = MathF.Max(14, height); return this; }

    // ── Appearance ─────────────────────────────────────────────

    /// <summary>Show/hide leading icons on segments that have them (default true).</summary>
    public BreadcrumbBuilder ShowIcons(bool show = true) { _showIcons = show; return this; }

    /// <summary>Whether the last segment is visually highlighted as "current" (default true).</summary>
    public BreadcrumbBuilder HighlightLast(bool highlight = true) { _highlightLast = highlight; return this; }

    /// <summary>Show only the icon (no label) for the first segment. Useful for a "home" root.</summary>
    public BreadcrumbBuilder TruncateFirst(bool truncate = true) { _truncateFirst = truncate; return this; }

    /// <summary>Explicitly mark a segment as active by index (overrides HighlightLast).</summary>
    public BreadcrumbBuilder ActiveIndex(int index) { _activeIndex = index; return this; }

    /// <summary>Set the color variant for the active segment.</summary>
    public BreadcrumbBuilder Variant(OrigamiVariant variant) { _variant = variant; return this; }

    /// <summary>Use primary color for active segment.</summary>
    public BreadcrumbBuilder Primary() => Variant(OrigamiVariant.Primary);

    // ── Terminator ─────────────────────────────────────────────

    /// <summary>Render the breadcrumb trail.</summary>
    public void Show()
    {
        var m = _theme.Metrics;
        var font = _theme.Font;
        var ink = _theme.Ink;
        var ramp = _theme.Get(_variant);
        if (font == null) return;

        int activeIdx = _activeIndex ?? (_highlightLast ? _items.Count - 1 : -1);
        float fontSize = m.FontSize;

        IOrigamiIcon? sepIcon = _separator switch
        {
            BreadcrumbSeparator.Chevron => _theme.Icons.ChevronRight,
            BreadcrumbSeparator.Arrow => _theme.Icons.ArrowRight,
            _ => null,
        };

        string sepText = _separator switch
        {
            BreadcrumbSeparator.Slash => "/",
            BreadcrumbSeparator.Backslash => "\\",
            BreadcrumbSeparator.Dot => ".",
            BreadcrumbSeparator.Custom => _customSeparator,
            _ => "",
        };

        using (_paper.Row(_id).Width(_width).Height(_height).RowBetween(0).Enter())
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                int idx = i;
                bool isActive = i == activeIdx;
                bool isLast = i == _items.Count - 1;
                bool hasIcon = _showIcons && (!string.IsNullOrEmpty(item.Icon) || item.IconDraw != null);
                bool showLabel = !(i == 0 && _truncateFirst && hasIcon);

                // Separator (before all except first)
                if (i > 0 && _separator != BreadcrumbSeparator.None)
                {
                    if (sepIcon != null)
                    {
                        Color sepCol = ink.C300;
                        _paper.Box($"{_id}_sep_{i}")
                            .Width(14).Height(_height)
                            .IsNotInteractable()
                            .OnPostLayout((h2, r2) => _paper.Draw(ref h2, (canvas, rr) =>
                            {
                                float sz = 12f;
                                float cx = (float)(rr.Min.X + rr.Size.X / 2), cy = (float)(rr.Min.Y + rr.Size.Y / 2);
                                var ir = new Prowl.Vector.Rect(new Prowl.Vector.Float2(cx - sz / 2, cy - sz / 2), new Prowl.Vector.Float2(cx + sz / 2, cy + sz / 2));
                                sepIcon.Draw(canvas, ir, sepCol, 1.3f);
                            }));
                    }
                    else if (!string.IsNullOrEmpty(sepText))
                    {
                        _paper.Box($"{_id}_sep_{i}")
                            .Width(UnitValue.Auto).Height(_height)
                            .Padding(1, 1, 0, 0)
                            .IsNotInteractable()
                            .Text(sepText, font)
                            .TextColor(ink.C300)
                            .FontSize(fontSize)
                            .Alignment(TextAlignment.MiddleCenter);
                    }
                }

                // Segment button
                Color textColor = isActive
                    ? (_variant == OrigamiVariant.Default ? _theme.Primary.C700 : ramp.C500) // acc-300 for the current crumb
                    : ink.C300;
                Color hoverBg = _theme.Neutral.C500;

                using (_paper.Row($"{_id}_seg_{i}")
                    .Width(UnitValue.Auto).Height(_height)
                    .Padding(m.PaddingSmall, m.PaddingSmall, 0, 0)
                    .Rounded(m.SmallRounding)
                    .Hovered.BackgroundColor(isLast ? Color.Transparent : hoverBg).End()
                    .OnClick(idx, (ci, _) => _onClick(_items[ci]))
                    .Enter())
                {
                    // Icon
                    if (hasIcon)
                    {
                        if (item.IconDraw != null)
                        {
                            var icon = item.IconDraw;
                            Color icoCol = isActive ? textColor : ink.C400;
                            float isz = fontSize;
                            _paper.Box($"{_id}_ico_{i}")
                                .Width(m.IconWidth).Height(_height)
                                .IsNotInteractable()
                                .OnPostLayout((h2, r2) => _paper.Draw(ref h2, (canvas, rr) =>
                                {
                                    float ix = (float)(rr.Min.X + (rr.Size.X - isz) * 0.5f);
                                    float iy = (float)(rr.Min.Y + (rr.Size.Y - isz) * 0.5f);
                                    icon.Draw(canvas, new Prowl.Vector.Rect(new Prowl.Vector.Float2(ix, iy), new Prowl.Vector.Float2(ix + isz, iy + isz)), icoCol);
                                }));
                        }
                        else
                        {
                            _paper.Box($"{_id}_ico_{i}")
                                .Width(m.IconWidth).Height(_height)
                                .IsNotInteractable()
                                .Text(item.Icon, font)
                                .TextColor(isActive ? textColor : ink.C400)
                                .FontSize(fontSize - 1)
                                .Alignment(TextAlignment.MiddleCenter);
                        }
                    }

                    // Label
                    if (showLabel)
                    {
                        _paper.Box($"{_id}_lbl_{i}")
                            .Width(UnitValue.Auto).Height(_height)
                            .IsNotInteractable()
                            .Text(item.Label, font)
                            .TextColor(textColor)
                            .FontSize(fontSize)
                            .Alignment(TextAlignment.MiddleLeft);
                    }
                }
            }
        }
    }
}
