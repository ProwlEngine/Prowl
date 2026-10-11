// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Drawing;

namespace Prowl.OrigamiUI;

/// <summary>
/// Complete visual configuration for an Origami widget tree.
///
/// <para>Colour structure mirrors the host editor's theme: a Neutral surface ramp,
/// branded ramps (<see cref="Primary"/>, <see cref="Blue"/>, <see cref="Red"/>,
/// <see cref="Green"/>, <see cref="Amber"/>), and a foreground/text ramp
/// (<see cref="Ink"/>). <see cref="OrigamiVariant"/> picks which surface ramp a
/// widget reads via <see cref="Get(OrigamiVariant)"/>.</para>
///
/// <para>Apply globally with <see cref="Origami.SetTheme"/>; scope to a region with
/// <see cref="Origami.PushTheme"/>.</para>
/// </summary>
public sealed class OrigamiTheme
{
    // ── Surface ramps (7 stops each, dark → light) ──────────────────

    /// <summary>Neutral grayscale ramp — backgrounds, panels, default surfaces.</summary>
    public OrigamiRamp Neutral = null!;

    /// <summary>Brand ramp — used by the <see cref="OrigamiVariant.Primary"/> variant.</summary>
    public OrigamiRamp Primary = null!;

    /// <summary>Blue ramp — used by the <see cref="OrigamiVariant.Info"/> variant.</summary>
    public OrigamiRamp Blue = null!;

    /// <summary>Red ramp — used by the <see cref="OrigamiVariant.Danger"/> variant.</summary>
    public OrigamiRamp Red = null!;

    /// <summary>Green ramp — used by the <see cref="OrigamiVariant.Success"/> variant.</summary>
    public OrigamiRamp Green = null!;

    /// <summary>Amber ramp — used by the <see cref="OrigamiVariant.Warning"/> variant.</summary>
    public OrigamiRamp Amber = null!;

    // ── Foreground ──────────────────────────────────────────────────

    /// <summary>
    /// Text/foreground ramp. Same shape as the surface ramps so widgets read it uniformly.
    /// Conventions: <c>C100</c> dimmest (rarely used as text), <c>C300</c> muted/disabled,
    /// <c>C500</c> primary labels, <c>C600</c>/<c>C700</c> "extra bright" emphasis (typically
    /// used for hover/focused text on dark surfaces).
    /// </summary>
    public OrigamiRamp Ink = null!;

    // ── Semantic surface / state colours ────────────────────────────
    // Surfaces and borders that aren't a single ramp stop. Explicit (not ramp-derived) so a host can
    // retint them independently; the accent-tinted states are computed off Primary so they follow it.

    /// <summary>Inset "glass" fill for toolbars, headers, tag pills and search fields within a panel
    /// (translucent dark, distinct from the panel body).</summary>
    public Color Glass;

    /// <summary>Menu / dropdown / popover surface — more opaque than panels so it reads over anything.</summary>
    public Color Popover;

    /// <summary>Soft hairline border / divider (very low alpha).</summary>
    public Color BorderSoft;

    /// <summary>Stronger border for focused / emphasised edges (e.g. popover outlines).</summary>
    public Color BorderStrong;

    /// <summary>Drop-shadow colour for popovers, dropdowns and modals.</summary>
    public Color Shadow;

    /// <summary>Accent-tinted hover overlay. Tracks <see cref="Primary"/> so a retint carries through.</summary>
    public Color Hover => WithAlpha(Primary.C500, 31);

    /// <summary>Accent-tinted selected / active fill. Tracks <see cref="Primary"/>.</summary>
    public Color Selected => WithAlpha(Primary.C500, 41);

    /// <summary>Accent-tinted for focused elements<see cref="Primary"/>.</summary>
    public Color FocusBorder => Primary.C500;

    /// <summary>Accent-tinted for focused glow<see cref="Primary"/>.</summary>
    public Color FocusGlow => WithAlpha(Primary.C500, 130);

    /// <summary>Return <paramref name="c"/> with a new alpha (0-255). Handy for state overlays.</summary>
    public static Color WithAlpha(Color c, int a) => Color.FromArgb(a, c.R, c.G, c.B);

    // ── Sizing / icons / font ───────────────────────────────────────

    public OrigamiMetrics Metrics = new();
    public OrigamiIcons Icons = new();

    /// <summary>Regular (400) weight face. Widgets fall back to this when a heavier weight is unset.</summary>
    public Prowl.Scribe.FontFile? Font;

    /// <summary>Medium (500) weight face. Optional — falls back to <see cref="Font"/>.</summary>
    public Prowl.Scribe.FontFile? FontMedium;

    /// <summary>Semi-bold (600) weight face. Optional — falls back to Medium then <see cref="Font"/>.</summary>
    public Prowl.Scribe.FontFile? FontSemiBold;

    /// <summary>Bold (700) weight face. Optional — falls back to SemiBold, Medium, then <see cref="Font"/>.</summary>
    public Prowl.Scribe.FontFile? FontBold;

    /// <summary>Monospace face for value/code fields (numeric, vector, color, hex). Falls back to <see cref="Font"/>.</summary>
    public Prowl.Scribe.FontFile? FontMono;
    /// <summary>Resolved monospace face, or the regular face if none supplied.</summary>
    public Prowl.Scribe.FontFile? Mono => FontMono ?? Font;

    /// <summary>Resolved medium (500) weight, or the regular face if none supplied.</summary>
    public Prowl.Scribe.FontFile? Medium => FontMedium ?? Font;

    /// <summary>Resolved semi-bold (600) weight, or the closest lighter face supplied.</summary>
    public Prowl.Scribe.FontFile? SemiBold => FontSemiBold ?? FontMedium ?? Font;

    /// <summary>Resolved bold (700) weight, or the closest lighter face supplied.</summary>
    public Prowl.Scribe.FontFile? Bold => FontBold ?? FontSemiBold ?? FontMedium ?? Font;

    /// <summary>
    /// Map a variant to its surface ramp. <see cref="OrigamiVariant.Default"/> and
    /// <see cref="OrigamiVariant.Subtle"/> both point at <see cref="Neutral"/>; widgets
    /// distinguish Subtle by treating low ramp stops as transparent (typically suppressing
    /// the idle background entirely).
    /// </summary>
    public OrigamiRamp Get(OrigamiVariant variant) => variant switch
    {
        OrigamiVariant.Primary => Primary,
        OrigamiVariant.Info => Blue,
        OrigamiVariant.Danger => Red,
        OrigamiVariant.Success => Green,
        OrigamiVariant.Warning => Amber,
        OrigamiVariant.Subtle => Neutral,
        _ => Neutral,
    };

    public OrigamiTheme Clone() => new()
    {
        Neutral = Neutral.Clone(),
        Primary = Primary.Clone(),
        Blue = Blue.Clone(),
        Red = Red.Clone(),
        Green = Green.Clone(),
        Amber = Amber.Clone(),
        Ink = Ink.Clone(),
        Glass = Glass,
        Popover = Popover,
        BorderSoft = BorderSoft,
        BorderStrong = BorderStrong,
        Shadow = Shadow,
        Metrics = Metrics.Clone(),
        Icons = Icons.Clone(),
        Font = Font,
        FontMedium = FontMedium,
        FontSemiBold = FontSemiBold,
        FontBold = FontBold,
        FontMono = FontMono,
    };

    /// <summary>
    /// Linearly interpolate the lerpable parts (ramps, ink, metrics) between two themes.
    /// Non-lerpable members (font, icons) snap to <paramref name="b"/> at the start of the
    /// transition.
    /// </summary>
    public static OrigamiTheme Lerp(OrigamiTheme a, OrigamiTheme b, float t) => new()
    {
        Neutral = OrigamiRamp.Lerp(a.Neutral, b.Neutral, t),
        Primary = OrigamiRamp.Lerp(a.Primary, b.Primary, t),
        Blue = OrigamiRamp.Lerp(a.Blue, b.Blue, t),
        Red = OrigamiRamp.Lerp(a.Red, b.Red, t),
        Green = OrigamiRamp.Lerp(a.Green, b.Green, t),
        Amber = OrigamiRamp.Lerp(a.Amber, b.Amber, t),
        Ink = OrigamiRamp.Lerp(a.Ink, b.Ink, t),
        Glass = OrigamiRamp.LerpColor(a.Glass, b.Glass, t),
        Popover = OrigamiRamp.LerpColor(a.Popover, b.Popover, t),
        BorderSoft = OrigamiRamp.LerpColor(a.BorderSoft, b.BorderSoft, t),
        BorderStrong = OrigamiRamp.LerpColor(a.BorderStrong, b.BorderStrong, t),
        Shadow = OrigamiRamp.LerpColor(a.Shadow, b.Shadow, t),
        Metrics = OrigamiMetrics.Lerp(a.Metrics, b.Metrics, t),
        FontMedium = b.FontMedium,
        FontSemiBold = b.FontSemiBold,
        FontBold = b.FontBold,
        FontMono = b.FontMono,
        Icons = b.Icons,
        Font = b.Font,
    };

    /// <summary>The default theme: Prowl's dark palette, Geist fonts and Font Awesome icons.</summary>
    public static OrigamiTheme CreateDefaults() => new()
    {
        Font = OrigamiFonts.Regular,
        FontMedium = OrigamiFonts.Medium,
        FontSemiBold = OrigamiFonts.SemiBold,
        FontBold = OrigamiFonts.Bold,
        FontMono = OrigamiFonts.Mono,

        Neutral = new OrigamiRamp
        {
            C100 = Color.FromArgb(235, 12, 12, 14),
            C200 = Color.FromArgb(33, 160, 160, 170),
            C300 = Color.FromArgb(220, 24, 24, 27),
            C400 = Color.FromArgb(205, 20, 20, 23),
            C500 = Color.FromArgb(235, 31, 31, 35),
            C600 = Color.FromArgb(245, 39, 39, 43),
            C700 = Color.FromArgb(255, 52, 52, 58),
        },
        Primary = Ramp("#00313F", "#003B52", "#064C77", "#1E67A3", "#3B82F6", "#59A8FD", "#7DD0FF"),
        Blue = Ramp("#005826", "#005E3A", "#006959", "#00777F", "#0EA5E9", "#2AD4ED", "#47FEF0"),
        Red = Ramp("#1F0E10", "#3A181E", "#5A242C", "#8C3442", "#FB7185", "#FC8C9C", "#FAAFBA"),
        Green = Ramp("#0F1F15", "#162C20", "#1F4530", "#2D6446", "#4ADE80", "#78E6A0", "#AAF0C3"),
        Amber = Ramp("#1F1808", "#3A2A10", "#5C4017", "#825C28", "#FBBF24", "#FCD060", "#FAE0A0"),
        Ink = Ramp("#4A4A50", "#6B6B72", "#8E8E96", "#B8B8BF", "#E4E4E7", "#FFFFFF", "#FFFFFF"),

        Glass = Color.FromArgb(153, 12, 12, 14),
        Popover = Color.FromArgb(250, 24, 24, 27),
        BorderSoft = Color.FromArgb(18, 160, 160, 170),
        BorderStrong = Color.FromArgb(66, 160, 160, 170),
        Shadow = Color.FromArgb(150, 0, 0, 0),
    };

    private static Color Hex(string s) => ColorTranslator.FromHtml(s);

    private static OrigamiRamp Ramp(string c1, string c2, string c3, string c4, string c5, string c6, string c7) => new()
    {
        C100 = Hex(c1),
        C200 = Hex(c2),
        C300 = Hex(c3),
        C400 = Hex(c4),
        C500 = Hex(c5),
        C600 = Hex(c6),
        C700 = Hex(c7),
    };
}
