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

    // ── Sizing / icons / font ───────────────────────────────────────

    public OrigamiMetrics Metrics = new();
    public OrigamiIcons Icons = new();
    public Prowl.Scribe.FontFile? Font;

    /// <summary>
    /// Map a variant to its surface ramp. <see cref="OrigamiVariant.Default"/> and
    /// <see cref="OrigamiVariant.Subtle"/> both point at <see cref="Neutral"/>; widgets
    /// distinguish Subtle by treating low ramp stops as transparent (typically suppressing
    /// the idle background entirely).
    /// </summary>
    public OrigamiRamp Get(OrigamiVariant variant) => variant switch
    {
        OrigamiVariant.Primary => Primary,
        OrigamiVariant.Info    => Blue,
        OrigamiVariant.Danger  => Red,
        OrigamiVariant.Success => Green,
        OrigamiVariant.Warning => Amber,
        OrigamiVariant.Subtle  => Neutral,
        _ => Neutral,
    };

    public OrigamiTheme Clone() => new()
    {
        Neutral = Neutral.Clone(),
        Primary = Primary.Clone(),
        Blue    = Blue.Clone(),
        Red     = Red.Clone(),
        Green   = Green.Clone(),
        Amber   = Amber.Clone(),
        Ink     = Ink.Clone(),
        Metrics = Metrics.Clone(),
        Icons   = Icons.Clone(),
        Font    = Font,
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
        Blue    = OrigamiRamp.Lerp(a.Blue,    b.Blue,    t),
        Red     = OrigamiRamp.Lerp(a.Red,     b.Red,     t),
        Green   = OrigamiRamp.Lerp(a.Green,   b.Green,   t),
        Amber   = OrigamiRamp.Lerp(a.Amber,   b.Amber,   t),
        Ink     = OrigamiRamp.Lerp(a.Ink,     b.Ink,     t),
        Metrics = OrigamiMetrics.Lerp(a.Metrics, b.Metrics, t),
        Icons   = b.Icons,
        Font    = b.Font,
    };

    /// <summary>
    /// Reasonable standalone defaults — a dark-theme palette with the standard branded ramps.
    /// Used when no host has called <see cref="Origami.SetTheme"/>.
    /// </summary>
    public static OrigamiTheme CreateDefaults() => new()
    {
        // Stops chosen to match Prowl's editor palette so the look is consistent out of the box.
        Neutral = Ramp("#101116", "#16151A", "#18191D", "#1D1E22", "#2E2D35", "#3E3D47", "#6C6A7A"),
        Primary = Ramp("#1D1010", "#271D36", "#3D2660", "#563784", "#7252AA", "#8E6FCC", "#A78EE2"),
        Blue    = Ramp("#0E1A2E", "#152343", "#1F365E", "#2D4F88", "#4070BC", "#5C8AE0", "#82A8F0"),
        Red     = Ramp("#1F0E0E", "#3A1818", "#5A2424", "#7A2F2F", "#A04040", "#E05858", "#EC7878"),
        Green   = Ramp("#0F1F15", "#162C20", "#1F4530", "#2D5C42", "#3D7A57", "#5DC07F", "#A6E5B7"),
        Amber   = Ramp("#1F1808", "#3A2A10", "#5C4017", "#7A5520", "#9B7332", "#E0A954", "#F4D8A8"),
        // Ink ramp: 5 editor-equivalent stops + 2 extra-bright headroom for emphasis text.
        Ink     = Ramp("#2E2D35", "#3E3D47", "#6C6A7A", "#B0ADBE", "#F0EEF8", "#FFFFFF", "#FFFFFF"),
    };

    private static Color Hex(string s) => ColorTranslator.FromHtml(s);

    private static OrigamiRamp Ramp(string c1, string c2, string c3, string c4, string c5, string c6, string c7) => new()
    {
        C100 = Hex(c1), C200 = Hex(c2), C300 = Hex(c3), C400 = Hex(c4),
        C500 = Hex(c5), C600 = Hex(c6), C700 = Hex(c7),
    };
}
