// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime;

public enum MinMaxCurveMode
{
    Constant,
    Curve,
    RandomBetweenTwoConstants,
    RandomBetweenTwoCurves
}

/// <summary>
/// A value that is a constant, a curve, or a random pick between two constants or two curves.
/// Curve modes are sampled at a normalized time and scaled by <see cref="CurveMultiplier"/>.
/// </summary>
[Serializable]
public class MinMaxCurve
{
    public MinMaxCurveMode Mode = MinMaxCurveMode.Constant;

    [ShowIf(nameof(IsConstant))]
    public float ConstantValue = 1.0f;

    [ShowIf(nameof(IsTwoConstants))]
    public float MinValue = 0.0f;
    [ShowIf(nameof(IsTwoConstants))]
    public float MaxValue = 1.0f;

    [ShowIf(nameof(IsSingleCurve))]
    public AnimationCurve Curve = new(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
    [ShowIf(nameof(IsTwoCurves))]
    public AnimationCurve MinCurve = new(new Keyframe(0f, 0f), new Keyframe(1f, 0f));
    [ShowIf(nameof(IsTwoCurves))]
    public AnimationCurve MaxCurve = new(new Keyframe(0f, 1f), new Keyframe(1f, 1f));

    [ShowIf(nameof(IsAnyCurve))]
    public float CurveMultiplier = 1.0f;

    public MinMaxCurve() { }

    public MinMaxCurve(float constant)
    {
        Mode = MinMaxCurveMode.Constant;
        ConstantValue = constant;
    }

    public MinMaxCurve(float min, float max)
    {
        Mode = MinMaxCurveMode.RandomBetweenTwoConstants;
        MinValue = min;
        MaxValue = max;
    }

    public MinMaxCurve(AnimationCurve curve, float multiplier = 1f)
    {
        Mode = MinMaxCurveMode.Curve;
        Curve = curve;
        CurveMultiplier = multiplier;
    }

    public MinMaxCurve(AnimationCurve min, AnimationCurve max, float multiplier = 1f)
    {
        Mode = MinMaxCurveMode.RandomBetweenTwoCurves;
        MinCurve = min;
        MaxCurve = max;
        CurveMultiplier = multiplier;
    }

    public bool IsRandom => Mode is MinMaxCurveMode.RandomBetweenTwoConstants or MinMaxCurveMode.RandomBetweenTwoCurves;

    private bool IsConstant => Mode == MinMaxCurveMode.Constant;
    private bool IsTwoConstants => Mode == MinMaxCurveMode.RandomBetweenTwoConstants;
    private bool IsSingleCurve => Mode == MinMaxCurveMode.Curve;
    private bool IsTwoCurves => Mode == MinMaxCurveMode.RandomBetweenTwoCurves;
    private bool IsAnyCurve => IsSingleCurve || IsTwoCurves;

    /// <summary>
    /// Evaluates at a normalized <paramref name="time"/>. <paramref name="lerp"/> picks between the
    /// min and max side in the random modes and is ignored otherwise.
    /// </summary>
    public float Evaluate(float time, float lerp) => Mode switch
    {
        MinMaxCurveMode.Constant => ConstantValue,
        MinMaxCurveMode.Curve => Curve.Evaluate(time) * CurveMultiplier,
        MinMaxCurveMode.RandomBetweenTwoConstants => MinValue + (MaxValue - MinValue) * lerp,
        MinMaxCurveMode.RandomBetweenTwoCurves => Maths.LerpUnclamped(MinCurve.Evaluate(time), MaxCurve.Evaluate(time), lerp) * CurveMultiplier,
        _ => ConstantValue
    };

    /// <summary>
    /// Evaluates at a normalized <paramref name="time"/>, drawing from <paramref name="random"/> only in the
    /// random modes so a seeded generator stays in step whatever modes are mixed. Null picks the min side.
    /// </summary>
    public float Evaluate(float time, Random? random)
        => Evaluate(time, IsRandom ? random?.NextSingle() ?? 0f : 0f);
}
