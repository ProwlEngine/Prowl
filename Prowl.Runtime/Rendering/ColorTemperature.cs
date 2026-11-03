// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>The colour of a black body at a temperature, the warm orange of a candle at 1900 K to the blue of a clear sky past 10000 K.</summary>
public static class ColorTemperature
{
    /// <summary>The temperatures <see cref="Chromaticity"/> is accurate between, in Kelvin.</summary>
    public const float Min = 1000f, Max = 15000f;

    /// <summary>The CIE 1931 xy chromaticity of the Planckian locus at a temperature, by Krystek's rational fit, clamped to <see cref="Min"/> and <see cref="Max"/>.</summary>
    public static Float2 Chromaticity(float kelvin)
    {
        double t = Maths.Clamp(kelvin, Min, Max);
        double u = (0.860117757 + 1.54118254e-4 * t + 1.28641212e-7 * t * t) / (1.0 + 8.42420235e-4 * t + 7.08145163e-7 * t * t);
        double v = (0.317398726 + 4.22806245e-5 * t + 4.20481691e-8 * t * t) / (1.0 - 2.89741816e-5 * t + 1.61456053e-7 * t * t);
        double d = 2.0 * u - 8.0 * v + 4.0;
        return new Float2((float)(3.0 * u / d), (float)(2.0 * v / d));
    }

    /// <summary>The linear Rec.709 colour of a temperature, negative channels clamped to zero and scaled so the brightest channel is 1.</summary>
    public static Color ToLinear(float kelvin)
    {
        Float2 xy = Chromaticity(kelvin);
        double x = xy.X / xy.Y, z = (1.0 - xy.X - xy.Y) / xy.Y;
        double r = Maths.Max(0.0, 3.2404542 * x - 1.5371385 - 0.4985314 * z);
        double g = Maths.Max(0.0, -0.9692660 * x + 1.8760108 + 0.0415560 * z);
        double b = Maths.Max(0.0, 0.0556434 * x - 0.2040259 + 1.0572252 * z);
        double peak = Maths.Max(r, Maths.Max(g, b));
        return new Color((float)(r / peak), (float)(g / peak), (float)(b / peak), 1f);
    }
}
