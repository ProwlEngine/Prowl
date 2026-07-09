// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Interface for input processors that transform input values.
/// </summary>
public interface IInputProcessor
{
    /// <summary>
    /// Process a float value.
    /// </summary>
    float Process(float value);

    /// <summary>
    /// Process a Vector2 value.
    /// </summary>
    Float2 Process(Float2 value);
}

/// <summary>
/// Normalizes vector input to have a magnitude of at most 1.
/// </summary>
public class NormalizeProcessor : IInputProcessor
{
    public float Process(float value) => Maths.Clamp(value, -1.0f, 1.0f);

    public Float2 Process(Float2 value)
    {
        float magnitude = Maths.Sqrt(value.X * value.X + value.Y * value.Y);
        if (magnitude > 1.0f)
            return value / magnitude;
        return value;
    }
}

/// <summary>
/// Inverts the input value.
/// </summary>
public class InvertProcessor : IInputProcessor
{
    public float Process(float value) => -value;
    public Float2 Process(Float2 value) => -value;
}

/// <summary>
/// Scales the input value by a multiplier.
/// </summary>
public class ScaleProcessor : IInputProcessor
{
    public float Scale { get; set; } = 1.0f;

    public ScaleProcessor(float scale)
    {
        Scale = scale;
    }

    public float Process(float value) => value * Scale;
    public Float2 Process(Float2 value) => value * Scale;
}

/// <summary>
/// Clamps the input value to a specified range.
/// </summary>
public class ClampProcessor : IInputProcessor
{
    public float Min { get; set; } = 0.0f;
    public float Max { get; set; } = 1.0f;

    public ClampProcessor(float min, float max)
    {
        Min = min;
        Max = max;
    }

    public float Process(float value) => Maths.Clamp(value, Min, Max);

    public Float2 Process(Float2 value)
    {
        return new Float2(
            Maths.Clamp(value.X, Min, Max),
            Maths.Clamp(value.Y, Min, Max)
        );
    }
}

/// <summary>
/// Applies a deadzone to the input value. Values below the threshold are set to zero.
/// </summary>
public class DeadzoneProcessor : IInputProcessor
{
    public float Threshold { get; set; } = 0.2f;

    public DeadzoneProcessor(float threshold = 0.2f)
    {
        Threshold = threshold;
    }

    public float Process(float value)
    {
        if (Maths.Abs(value) < Threshold)
            return 0f;

        // Rescale the value to start from 0 after the deadzone
        float sign = Maths.Sign(value);
        float adjustedValue = (Maths.Abs(value) - Threshold) / (1.0f - Threshold);
        return sign * adjustedValue;
    }

    public Float2 Process(Float2 value)
    {
        float magnitude = Maths.Sqrt(value.X * value.X + value.Y * value.Y);
        if (magnitude < Threshold)
            return Float2.Zero;

        // Radial deadzone - preserve direction
        float adjustedMagnitude = (magnitude - Threshold) / (1.0f - Threshold);
        return value * (adjustedMagnitude / magnitude);
    }
}

/// <summary>
/// Applies an exponential curve to the input for more precise control at low values.
/// </summary>
public class ExponentialProcessor : IInputProcessor
{
    public float Exponent { get; set; } = 2.0f;

    public ExponentialProcessor(float exponent = 2.0f)
    {
        Exponent = exponent;
    }

    public float Process(float value)
    {
        float sign = Maths.Sign(value);
        return sign * Maths.Pow(Maths.Abs(value), Exponent);
    }

    public Float2 Process(Float2 value)
    {
        return new Float2(Process(value.X), Process(value.Y));
    }
}
