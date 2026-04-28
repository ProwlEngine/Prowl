// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.OrigamiUI;

/// <summary>
/// Sizing, padding, and font metrics shared across Origami widgets.
/// All values are in logical pixels.
/// </summary>
public sealed class OrigamiMetrics
{
    /// <summary>Default corner radius for headers and outlined bodies.</summary>
    public float Rounding = 4f;

    /// <summary>Default header (clickable row) height.</summary>
    public float HeaderHeight = 22f;

    /// <summary>Inner left/right padding inside a header row.</summary>
    public float HeaderPadX = 6f;

    /// <summary>Width reserved for leading icons (chevron, toggle glyph) inside headers.</summary>
    public float IconWidth = 16f;

    /// <summary>Left padding before a header badge.</summary>
    public float BadgePadLeft = 6f;

    /// <summary>Base font size for widget text.</summary>
    public float FontSize = 12f;

    /// <summary>Linearly interpolate between two metrics blocks. Used during theme transitions.</summary>
    public static OrigamiMetrics Lerp(OrigamiMetrics a, OrigamiMetrics b, float t) => new()
    {
        Rounding     = LerpF(a.Rounding,     b.Rounding,     t),
        HeaderHeight = LerpF(a.HeaderHeight, b.HeaderHeight, t),
        HeaderPadX   = LerpF(a.HeaderPadX,   b.HeaderPadX,   t),
        IconWidth    = LerpF(a.IconWidth,    b.IconWidth,    t),
        BadgePadLeft = LerpF(a.BadgePadLeft, b.BadgePadLeft, t),
        FontSize     = LerpF(a.FontSize,     b.FontSize,     t),
    };

    /// <summary>Shallow copy.</summary>
    public OrigamiMetrics Clone() => new()
    {
        Rounding     = Rounding,
        HeaderHeight = HeaderHeight,
        HeaderPadX   = HeaderPadX,
        IconWidth    = IconWidth,
        BadgePadLeft = BadgePadLeft,
        FontSize     = FontSize,
    };

    private static float LerpF(float a, float b, float t) => a + (b - a) * t;
}
