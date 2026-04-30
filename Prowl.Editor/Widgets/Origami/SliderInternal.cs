// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Numerics;

namespace Prowl.OrigamiUI;

/// <summary>
/// Numeric helpers shared between <see cref="SliderBuilder{T}"/> and
/// <see cref="RangeSliderBuilder{T}"/>. Math goes through <see cref="double"/> for everything
/// except <see cref="decimal"/>, which has its own path to preserve precision.
/// </summary>
internal static class SliderInternal
{
    /// <summary>0..1 fraction along the track for the given value, with optional log mapping.</summary>
    public static double ValueToT<T>(T value, T min, T max, bool logarithmic) where T : struct, INumber<T>
    {
        double v = ToDouble(value);
        double mn = ToDouble(min);
        double mx = ToDouble(max);
        if (mx <= mn) return 0.0;

        if (logarithmic && mn > 0.0)
        {
            double ratio = Math.Log(v / mn) / Math.Log(mx / mn);
            return Math.Clamp(ratio, 0.0, 1.0);
        }
        return Math.Clamp((v - mn) / (mx - mn), 0.0, 1.0);
    }

    /// <summary>Inverse of <see cref="ValueToT{T}"/> — turn a 0..1 fraction back into a value.</summary>
    public static T TToValue<T>(double t, T min, T max, bool logarithmic) where T : struct, INumber<T>
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double mn = ToDouble(min);
        double mx = ToDouble(max);
        double v;

        if (logarithmic && mn > 0.0)
            v = mn * Math.Exp(t * Math.Log(mx / mn));
        else
            v = mn + t * (mx - mn);

        return FromDouble<T>(v);
    }

    /// <summary>Snap to nearest multiple of <paramref name="step"/>, anchored at <paramref name="origin"/>.</summary>
    public static T Snap<T>(T value, T origin, T step) where T : struct, INumber<T>
    {
        if (step == T.Zero) return value;
        double v = ToDouble(value);
        double o = ToDouble(origin);
        double s = ToDouble(step);
        double snapped = o + Math.Round((v - o) / s) * s;
        return FromDouble<T>(snapped);
    }

    public static T Clamp<T>(T value, T min, T max) where T : struct, INumber<T>
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static T MultiplyByDouble<T>(T value, double factor) where T : struct, INumber<T>
        => FromDouble<T>(ToDouble(value) * factor);

    /// <summary>Converts any INumber to double for math. Handles decimal explicitly to dodge a runtime check.</summary>
    public static double ToDouble<T>(T value) where T : struct, INumber<T>
    {
        if (typeof(T) == typeof(decimal))
            return (double)(decimal)(object)value;
        return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Converts a double back into <typeparamref name="T"/>. Integer types round; others truncate via T.CreateChecked.</summary>
    public static T FromDouble<T>(double value) where T : struct, INumber<T>
    {
        if (typeof(T) == typeof(decimal))
            return (T)(object)(decimal)value;

        // Integer types should round-to-nearest so a slider position that lands on 4.7 gives 5,
        // not 4. CreateChecked truncates toward zero on integer targets, which feels wrong for a
        // continuous slider; round through Math.Round first.
        if (T.IsInteger(T.One))
            return T.CreateChecked((long)Math.Round(value, MidpointRounding.AwayFromZero));

        return T.CreateChecked(value);
    }
}
