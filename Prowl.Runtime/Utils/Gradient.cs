// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Represents a color key in a gradient.
/// </summary>
[Serializable]
public struct GradientColorKey
{
    public Color Color;
    public float Time;

    public GradientColorKey(Color color, float time)
    {
        Color = color;
        Time = time;
    }
}

/// <summary>
/// Represents an alpha key in a gradient.
/// </summary>
[Serializable]
public struct GradientAlphaKey
{
    public float Alpha;
    public float Time;

    public GradientAlphaKey(float alpha, float time)
    {
        Alpha = alpha;
        Time = time;
    }
}

/// <summary>
/// A gradient of colors over time.
/// </summary>
[Serializable]
public class Gradient
{
    public List<GradientColorKey> ColorKeys = new() { new GradientColorKey(Color.White, 0), new GradientColorKey(Color.White, 1) };
    public List<GradientAlphaKey> AlphaKeys = new() { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) };

    public Gradient() { }

    /// <summary>
    /// Evaluates the gradient at the given time (0-1).
    /// </summary>
    public Color Evaluate(float time)
    {
        time = Maths.Clamp(time, 0, 1);

        // Evaluate color
        Color color = ColorKeys[0].Color;
        for (int i = 0; i < ColorKeys.Count - 1; i++)
        {
            if (time >= ColorKeys[i].Time && time <= ColorKeys[i + 1].Time)
            {
                float t = (time - ColorKeys[i].Time) / (ColorKeys[i + 1].Time - ColorKeys[i].Time);
                color = Color.Lerp(ColorKeys[i].Color, ColorKeys[i + 1].Color, t);
                break;
            }
        }

        // Evaluate alpha
        float alpha = AlphaKeys[0].Alpha;
        for (int i = 0; i < AlphaKeys.Count - 1; i++)
        {
            if (time >= AlphaKeys[i].Time && time <= AlphaKeys[i + 1].Time)
            {
                float t = (time - AlphaKeys[i].Time) / (AlphaKeys[i + 1].Time - AlphaKeys[i].Time);
                alpha = AlphaKeys[i].Alpha + (AlphaKeys[i + 1].Alpha - AlphaKeys[i].Alpha) * t;
                break;
            }
        }

        return new Color(color.R, color.G, color.B, alpha);
    }
}
