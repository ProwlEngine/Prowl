// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime;

public enum MinMaxGradientMode
{
    Color,
    Gradient,
    RandomBetweenTwoColors,
    RandomBetweenTwoGradients,
    /// <summary>A random point on <see cref="MinMaxGradient.Gradient"/>, ignoring time.</summary>
    RandomColor
}

/// <summary>
/// A color that is a constant, a gradient over time, or a random pick between two colors or two gradients.
/// </summary>
[Serializable]
public class MinMaxGradient
{
    public MinMaxGradientMode Mode = MinMaxGradientMode.Color;
    public Color ConstantColor = Color.White;
    public Color MinColor = Color.White;
    public Color MaxColor = Color.White;
    public Gradient Gradient = new();
    public Gradient MinGradient = new();
    public Gradient MaxGradient = new();

    public MinMaxGradient() { }

    public MinMaxGradient(Color constant)
    {
        Mode = MinMaxGradientMode.Color;
        ConstantColor = constant;
    }

    public MinMaxGradient(Color min, Color max)
    {
        Mode = MinMaxGradientMode.RandomBetweenTwoColors;
        MinColor = min;
        MaxColor = max;
    }

    public MinMaxGradient(Gradient gradient)
    {
        Mode = MinMaxGradientMode.Gradient;
        Gradient = gradient;
    }

    public MinMaxGradient(Gradient min, Gradient max)
    {
        Mode = MinMaxGradientMode.RandomBetweenTwoGradients;
        MinGradient = min;
        MaxGradient = max;
    }

    public bool IsRandom => Mode is MinMaxGradientMode.RandomBetweenTwoColors or MinMaxGradientMode.RandomBetweenTwoGradients or MinMaxGradientMode.RandomColor;

    /// <summary>
    /// Evaluates at a normalized <paramref name="time"/>. <paramref name="lerp"/> picks the random side
    /// in the random modes and is ignored otherwise.
    /// </summary>
    public Color Evaluate(float time, float lerp) => Mode switch
    {
        MinMaxGradientMode.Color => ConstantColor,
        MinMaxGradientMode.Gradient => Gradient.Evaluate(time),
        MinMaxGradientMode.RandomBetweenTwoColors => Color.Lerp(MinColor, MaxColor, lerp),
        MinMaxGradientMode.RandomBetweenTwoGradients => Color.Lerp(MinGradient.Evaluate(time), MaxGradient.Evaluate(time), lerp),
        MinMaxGradientMode.RandomColor => Gradient.Evaluate(lerp),
        _ => ConstantColor
    };

    /// <summary>
    /// Evaluates at a normalized <paramref name="time"/>, drawing from <paramref name="random"/> only in
    /// the random modes so a seeded generator stays in step. Null picks the min side.
    /// </summary>
    public Color Evaluate(float time, Random? random)
        => Evaluate(time, IsRandom ? random?.NextSingle() ?? 0f : 0f);
}
