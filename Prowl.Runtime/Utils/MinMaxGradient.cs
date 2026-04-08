// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Mode for MinMaxGradient evaluation.
/// </summary>
public enum MinMaxGradientMode
{
    Color,
    Gradient,
    RandomBetweenTwoColors,
    RandomBetweenTwoGradients
}

/// <summary>
/// A gradient that can be constant, animated, or random between two values.
/// Used for particle color properties that change over time.
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

    /// <summary>
    /// Evaluates the gradient at the given normalized time (0-1).
    /// </summary>
    public Color Evaluate(float normalizedTime, Random random)
    {
        return Mode switch
        {
            MinMaxGradientMode.Color => ConstantColor,
            MinMaxGradientMode.Gradient => Gradient.Evaluate(normalizedTime),
            MinMaxGradientMode.RandomBetweenTwoColors => Color.Lerp(MinColor, MaxColor, (float)random.NextDouble()),
            MinMaxGradientMode.RandomBetweenTwoGradients => Color.Lerp(
                MinGradient.Evaluate(normalizedTime),
                MaxGradient.Evaluate(normalizedTime),
                (float)random.NextDouble()
            ),
            _ => ConstantColor
        };
    }

    /// <summary>
    /// Evaluates the gradient for initial particle spawn.
    /// </summary>
    public Color EvaluateInitial(Random random)
    {
        return Mode switch
        {
            MinMaxGradientMode.Color => ConstantColor,
            MinMaxGradientMode.Gradient => Gradient.Evaluate(0),
            MinMaxGradientMode.RandomBetweenTwoColors => Color.Lerp(MinColor, MaxColor, (float)random.NextDouble()),
            MinMaxGradientMode.RandomBetweenTwoGradients => Color.Lerp(
                MinGradient.Evaluate(0),
                MaxGradient.Evaluate(0),
                (float)random.NextDouble()
            ),
            _ => ConstantColor
        };
    }
}
