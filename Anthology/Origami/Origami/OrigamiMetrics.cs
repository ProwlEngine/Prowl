// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.OrigamiUI;

/// <summary>
/// Sizing, padding, and font metrics shared across Origami widgets.
/// All values are in logical pixels.
/// </summary>
public sealed class OrigamiMetrics
{
    // ── Corner rounding ────────────────────────────────────────

    /// <summary>Default corner radius for controls (buttons, inputs, dropdowns).</summary>
    public float Rounding = 8f;

    /// <summary>Larger corner radius for containers (modals, popovers, panels).</summary>
    public float ContainerRounding = 10f;

    /// <summary>Small corner radius for inline elements (badges, highlight bars).</summary>
    public float SmallRounding = 5f;

    // ── Heights ────────────────────────────────────────────────

    /// <summary>Standard row/control height (text fields, dropdowns, sliders).</summary>
    public float RowHeight = 28f;

    /// <summary>Default header (clickable row) height.</summary>
    public float HeaderHeight = 26f;

    /// <summary>Height for compact items (icon buttons, small controls).</summary>
    public float CompactHeight = 20f;

    // ── Spacing ────────────────────────────────────────────────

    /// <summary>Tight spacing between related items in a group (e.g. list elements).</summary>
    public float SpacingSmall = 2f;

    /// <summary>Standard spacing between sibling items (e.g. between fields).</summary>
    public float Spacing = 4f;

    /// <summary>Spacing between row children (label to control gap).</summary>
    public float SpacingMedium = 6f;

    /// <summary>Spacing between sections or groups of fields.</summary>
    public float SpacingLarge = 8f;

    // ── Padding ────────────────────────────────────────────────

    /// <summary>Small inner padding for compact elements.</summary>
    public float PaddingSmall = 4f;

    /// <summary>Standard inner padding for controls and rows.</summary>
    public float Padding = 6f;

    /// <summary>Larger inner padding for containers (modals, dialogs, panels).</summary>
    public float PaddingLarge = 12f;

    /// <summary>Indentation width per nesting depth level.</summary>
    public float IndentWidth = 16f;

    // ── Header metrics ─────────────────────────────────────────

    /// <summary>Inner left/right padding inside a header row.</summary>
    public float HeaderPadX = 6f;

    /// <summary>Width reserved for leading icons (chevron, toggle glyph) inside headers.</summary>
    public float IconWidth = 16f;

    /// <summary>Standard icon box width (icon + surrounding space).</summary>
    public float IconBoxWidth = 20f;

    /// <summary>Left padding before a header badge.</summary>
    public float BadgePadLeft = 6f;

    // ── Font ───────────────────────────────────────────────────

    /// <summary>Base font size for widget text.</summary>
    public float FontSize = 16f;

    /// <summary>Smaller font size for secondary/muted text.</summary>
    public float FontSizeSmall = 13f;

    // ── Label ──────────────────────────────────────────────────

    /// <summary>Default label width in property grids and forms.</summary>
    public float LabelWidth = 120f;

    // ── Docking ───────────────────────────────────────────────

    /// <summary>Tab bar height in dock leaves.</summary>
    public float TabBarHeight = 32f;

    /// <summary>Backdrop-blur radius (px) behind each dock window's glass, for the frosted effect.
    /// Requires a renderer with backdrop-blur support; 0 disables it.</summary>
    public float WindowBackdropBlur = 22f;

    /// <summary>Horizontal padding inside each tab.</summary>
    public float TabPadding = 11f;

    /// <summary>Gap between tabs.</summary>
    public float TabGap = 0f;

    /// <summary>Size of the close button inside a tab.</summary>
    public float TabCloseSize = 14f;

    /// <summary>Width/height of the splitter gap between dock panes (shows the desktop behind).</summary>
    public float SplitterSize = 8f;

    /// <summary>Content padding inside a dock leaf.</summary>
    public float DockPadding = 0f;

    /// <summary>Size of each dock zone indicator square.</summary>
    public float IndicatorSize = 28f;

    /// <summary>Gap between dock zone indicator squares.</summary>
    public float IndicatorGap = 4f;

    /// <summary>Linearly interpolate between two metrics blocks. Used during theme transitions.</summary>
    public static OrigamiMetrics Lerp(OrigamiMetrics a, OrigamiMetrics b, float t) => new()
    {
        Rounding          = LerpF(a.Rounding,          b.Rounding,          t),
        ContainerRounding = LerpF(a.ContainerRounding, b.ContainerRounding, t),
        SmallRounding     = LerpF(a.SmallRounding,     b.SmallRounding,     t),
        RowHeight         = LerpF(a.RowHeight,         b.RowHeight,         t),
        HeaderHeight      = LerpF(a.HeaderHeight,      b.HeaderHeight,      t),
        CompactHeight     = LerpF(a.CompactHeight,     b.CompactHeight,     t),
        SpacingSmall      = LerpF(a.SpacingSmall,      b.SpacingSmall,      t),
        Spacing           = LerpF(a.Spacing,           b.Spacing,           t),
        SpacingMedium     = LerpF(a.SpacingMedium,     b.SpacingMedium,     t),
        SpacingLarge      = LerpF(a.SpacingLarge,      b.SpacingLarge,      t),
        PaddingSmall      = LerpF(a.PaddingSmall,      b.PaddingSmall,      t),
        Padding           = LerpF(a.Padding,           b.Padding,           t),
        PaddingLarge      = LerpF(a.PaddingLarge,      b.PaddingLarge,      t),
        IndentWidth       = LerpF(a.IndentWidth,       b.IndentWidth,       t),
        HeaderPadX        = LerpF(a.HeaderPadX,        b.HeaderPadX,        t),
        IconWidth         = LerpF(a.IconWidth,         b.IconWidth,         t),
        IconBoxWidth      = LerpF(a.IconBoxWidth,      b.IconBoxWidth,      t),
        BadgePadLeft      = LerpF(a.BadgePadLeft,      b.BadgePadLeft,      t),
        FontSize          = LerpF(a.FontSize,          b.FontSize,          t),
        FontSizeSmall     = LerpF(a.FontSizeSmall,     b.FontSizeSmall,     t),
        LabelWidth        = LerpF(a.LabelWidth,        b.LabelWidth,        t),
        TabBarHeight      = LerpF(a.TabBarHeight,      b.TabBarHeight,      t),
        TabPadding        = LerpF(a.TabPadding,        b.TabPadding,        t),
        TabGap            = LerpF(a.TabGap,            b.TabGap,            t),
        TabCloseSize      = LerpF(a.TabCloseSize,      b.TabCloseSize,      t),
        SplitterSize      = LerpF(a.SplitterSize,      b.SplitterSize,      t),
        DockPadding       = LerpF(a.DockPadding,       b.DockPadding,       t),
        IndicatorSize     = LerpF(a.IndicatorSize,     b.IndicatorSize,     t),
        IndicatorGap      = LerpF(a.IndicatorGap,      b.IndicatorGap,      t),
    };

    /// <summary>Shallow copy.</summary>
    public OrigamiMetrics Clone() => new()
    {
        Rounding          = Rounding,
        ContainerRounding = ContainerRounding,
        SmallRounding     = SmallRounding,
        RowHeight         = RowHeight,
        HeaderHeight      = HeaderHeight,
        CompactHeight     = CompactHeight,
        SpacingSmall      = SpacingSmall,
        Spacing           = Spacing,
        SpacingMedium     = SpacingMedium,
        SpacingLarge      = SpacingLarge,
        PaddingSmall      = PaddingSmall,
        Padding           = Padding,
        PaddingLarge      = PaddingLarge,
        IndentWidth       = IndentWidth,
        HeaderPadX        = HeaderPadX,
        IconWidth         = IconWidth,
        IconBoxWidth      = IconBoxWidth,
        BadgePadLeft      = BadgePadLeft,
        FontSize          = FontSize,
        FontSizeSmall     = FontSizeSmall,
        LabelWidth        = LabelWidth,
        TabBarHeight      = TabBarHeight,
        TabPadding        = TabPadding,
        TabGap            = TabGap,
        TabCloseSize      = TabCloseSize,
        SplitterSize      = SplitterSize,
        DockPadding       = DockPadding,
        IndicatorSize     = IndicatorSize,
        IndicatorGap      = IndicatorGap,
    };

    private static float LerpF(float a, float b, float t) => a + (b - a) * t;
}
