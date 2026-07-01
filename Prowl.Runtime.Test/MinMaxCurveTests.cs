// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for <see cref="MinMaxCurve"/> / <see cref="MinMaxGradient"/> - particularly that non-random
/// modes do not consume the RNG (so seeded particle playback stays deterministic when modes are mixed).
/// </summary>
public class MinMaxCurveTests
{
    [Fact]
    public void MinMaxCurve_ConstantMode_DoesNotConsumeRandom()
    {
        var used = new Random(123);
        var reference = new Random(123);

        var curve = new MinMaxCurve(5f); // Constant mode
        for (int i = 0; i < 5; i++)
            curve.Evaluate(0.5f, used);

        // The Constant evaluations must not have advanced the RNG.
        Assert.Equal(reference.NextSingle(), used.NextSingle());
    }

    [Fact]
    public void MinMaxCurve_RandomMode_ConsumesRandom()
    {
        var used = new Random(123);
        var reference = new Random(123);

        var curve = new MinMaxCurve { Mode = MinMaxCurveMode.Random, MinValue = 0f, MaxValue = 1f };
        curve.EvaluateInitial(used);   // consumes exactly one draw
        reference.NextSingle();         // match it

        Assert.Equal(reference.NextSingle(), used.NextSingle());
    }

    [Fact]
    public void MinMaxGradient_ColorMode_DoesNotConsumeRandom()
    {
        var used = new Random(7);
        var reference = new Random(7);

        var grad = new MinMaxGradient(Color.White); // Color mode
        for (int i = 0; i < 5; i++)
            grad.Evaluate(0.5f, used);

        Assert.Equal(reference.NextSingle(), used.NextSingle());
    }
}
