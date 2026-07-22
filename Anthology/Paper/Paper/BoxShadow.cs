// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Drawing;

namespace Prowl.PaperUI;

/// <summary>
/// Represents a simple box shadow similar to CSS box-shadow.
/// </summary>
public struct BoxShadow
{
    public float OffsetX;
    public float OffsetY;
    public float Blur;
    public float Spread;
    public Color Color;

    public BoxShadow(float offsetX, float offsetY, float blur, float spread, Color color)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        Blur = blur;
        Spread = spread;
        Color = color;
    }

    /// <summary>Default empty shadow.</summary>
    public static readonly BoxShadow None = new BoxShadow(0, 0, 0, 0, Color.Transparent);

    /// <summary>Returns true if the shadow has a visible effect.</summary>
    public bool IsVisible => Color.A > 0;

    /// <summary>
    /// Interpolates between two box shadows.
    /// </summary>
    public static BoxShadow Lerp(BoxShadow start, BoxShadow end, float t)
    {
        Color LerpColor(Color a, Color b)
        {
            // A fully-transparent endpoint carries no meaningful RGB (System.Drawing's Transparent
            // is white), so adopt the other endpoint's RGB and only fade alpha. Otherwise a shadow
            // animating in from None would sweep through a white glow before reaching its colour.
            if (a.A == 0) a = Color.FromArgb(0, b.R, b.G, b.B);
            if (b.A == 0) b = Color.FromArgb(0, a.R, a.G, a.B);
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bVal = (int)(a.B + (b.B - a.B) * t);
            int aVal = (int)(a.A + (b.A - a.A) * t);
            return Color.FromArgb(aVal, r, g, bVal);
        }

        return new BoxShadow(
            start.OffsetX + (end.OffsetX - start.OffsetX) * t,
            start.OffsetY + (end.OffsetY - start.OffsetY) * t,
            start.Blur + (end.Blur - start.Blur) * t,
            start.Spread + (end.Spread - start.Spread) * t,
            LerpColor(start.Color, end.Color)
        );
    }
}
