// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

/// <summary>
/// Mode for MinMaxCurve evaluation.
/// </summary>
public enum MinMaxCurveMode
{
    Constant,
    Curve,
    Random
}

/// <summary>
/// A curve that can be constant, animated, or random between two values.
/// Used for particle properties that change over time.
/// </summary>
[Serializable]
public class MinMaxCurve
{
    public MinMaxCurveMode Mode = MinMaxCurveMode.Constant;
    public float ConstantValue = 1.0f;
    public float MinValue = 0.0f;
    public float MaxValue = 1.0f;
    public AnimationCurve Curve = new();
    public AnimationCurve MinCurve = new();
    public AnimationCurve MaxCurve = new();

    public MinMaxCurve() { }

    public MinMaxCurve(float constant)
    {
        Mode = MinMaxCurveMode.Constant;
        ConstantValue = constant;
    }

    /// <summary>
    /// Evaluates the curve at the given normalized time (0-1).
    /// </summary>
    public float Evaluate(float normalizedTime, Random? random)
    {
        // Null random falls back to the Min side of Random mode (deterministic).
        float t = random?.NextSingle() ?? 0f;
        return Mode switch
        {
            MinMaxCurveMode.Constant => ConstantValue,
            MinMaxCurveMode.Curve => (float)Curve.Evaluate(normalizedTime),
            MinMaxCurveMode.Random => Lerp(
                (float)MinCurve.Evaluate(normalizedTime),
                (float)MaxCurve.Evaluate(normalizedTime),
                t
            ),
            _ => ConstantValue
        };
    }

    /// <summary>
    /// Evaluates the curve for initial particle spawn.
    /// </summary>
    public float EvaluateInitial(Random? random)
    {
        float t = random?.NextSingle() ?? 0f;
        return Mode switch
        {
            MinMaxCurveMode.Constant => ConstantValue,
            MinMaxCurveMode.Curve => (float)Curve.Evaluate(0),
            MinMaxCurveMode.Random => Lerp(MinValue, MaxValue, t),
            _ => ConstantValue
        };
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
