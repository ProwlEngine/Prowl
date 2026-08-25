// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace Prowl.Vector.Tests;

public class AnimationCurveTests
{
    private const float Eps = 1e-4f;

    private static void Close(float expected, float actual, float eps = Eps) =>
        Assert.True(Maths.Abs(expected - actual) <= eps, $"Expected {expected} but got {actual}");

    private static void Close(Float3 expected, Float3 actual, float eps = Eps) =>
        Assert.True(
            Maths.Abs(expected.X - actual.X) <= eps &&
            Maths.Abs(expected.Y - actual.Y) <= eps &&
            Maths.Abs(expected.Z - actual.Z) <= eps,
            $"Expected {expected} but got {actual}");

    private static void Close(Quaternion expected, Quaternion actual, float eps = Eps)
    {
        float dot = Maths.Abs(Quaternion.Dot(expected, actual));
        Assert.True(dot >= 1f - eps, $"Expected {expected} but got {actual}");
    }

    /// <summary>Straightforward linear scan used to cross-check the binary search in Locate.</summary>
    private static float ReferenceEvaluate(AnimationCurve curve, float time)
    {
        Keyframe[] keys = curve.GetKeys();
        if (keys.Length == 0) return 0f;
        if (keys.Length == 1) return keys[0].Value;
        if (time <= keys[0].Time) return keys[0].Value;
        if (time >= keys[^1].Time) return keys[^1].Value;

        for (int i = keys.Length - 2; i >= 0; i--)
        {
            if (keys[i].Time > time) continue;

            Keyframe a = keys[i];
            Keyframe b = keys[i + 1];
            float dt = b.Time - a.Time;
            float u = dt > 0f ? (time - a.Time) / dt : 0f;
            switch (a.Interpolation)
            {
                case CurveInterpolation.Step:
                    return a.Value;
                case CurveInterpolation.Linear:
                    return a.Value + (b.Value - a.Value) * u;
                default:
                    float u2 = u * u;
                    float u3 = u2 * u;
                    return (2f * u3 - 3f * u2 + 1f) * a.Value
                         + (u3 - 2f * u2 + u) * dt * a.OutTangent
                         + (-2f * u3 + 3f * u2) * b.Value
                         + (u3 - u2) * dt * b.InTangent;
            }
        }
        return keys[0].Value;
    }

    #region Construction

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    public void Constructor_RejectsDimensionsOutsideOneToFour(int dimension) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurve(dimension));

    [Fact]
    public void EmptyCurve_EvaluatesToZero()
    {
        var curve = new AnimationCurve();

        Assert.Empty(curve);
        Close(0f, curve.Evaluate(0f));
        Close(0f, curve.Evaluate(123f));
        Assert.Equal(0f, curve.StartTime);
        Assert.Equal(0f, curve.EndTime);
        Assert.Equal(0f, curve.Duration);
    }

    [Fact]
    public void EmptyVectorCurve_EvaluatesToZeroVector()
    {
        var curve = new AnimationCurve(3);

        Close(Float3.Zero, curve.EvaluateFloat3(4f));
    }

    [Fact]
    public void SingleKey_HoldsItsValueEverywhere()
    {
        var curve = new AnimationCurve(new Keyframe(2f, 7f));

        Close(7f, curve.Evaluate(-100f));
        Close(7f, curve.Evaluate(2f));
        Close(7f, curve.Evaluate(100f));
    }

    [Fact]
    public void Duration_SpansFirstToLastKey()
    {
        var curve = new AnimationCurve(new Keyframe(1f, 0f), new Keyframe(4.5f, 1f));

        Close(1f, curve.StartTime);
        Close(4.5f, curve.EndTime);
        Close(3.5f, curve.Duration);
    }

    [Fact]
    public void Factories_ProduceExpectedShapes()
    {
        Close(3f, AnimationCurve.Constant(3f).Evaluate(99f));
        Close(3f, AnimationCurve.Constant(0f, 1f, 3f).Evaluate(0.5f));
        Close(0.5f, AnimationCurve.Linear(0f, 0f, 1f, 1f).Evaluate(0.5f));

        AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        Close(0.5f, ease.Evaluate(0.5f));
        Close(0.15625f, ease.Evaluate(0.25f));
        Close(0.84375f, ease.Evaluate(0.75f));
    }

    #endregion

    #region Key management

    [Fact]
    public void AddKey_KeepsKeysSortedAndReturnsInsertIndex()
    {
        var curve = new AnimationCurve();

        Assert.Equal(0, curve.AddKey(5f, 5f));
        Assert.Equal(0, curve.AddKey(1f, 1f));
        Assert.Equal(1, curve.AddKey(3f, 3f));
        Assert.Equal(3, curve.AddKey(9f, 9f));

        Assert.Equal(new[] { 1f, 3f, 5f, 9f }, curve.GetKeys().Select(k => k.Time));
        Assert.Equal(new[] { 1f, 3f, 5f, 9f }, curve.GetKeys().Select(k => k.Value));
    }

    [Fact]
    public void AddKey_ManyOutOfOrderKeys_StaySorted()
    {
        var rng = new RNG(1234);
        var curve = new AnimationCurve();
        for (int i = 0; i < 200; i++)
            curve.AddKey(rng.NextFloat() * 100f, rng.NextFloat());

        Assert.Equal(200, curve.Count);
        for (int i = 1; i < curve.Count; i++)
            Assert.True(curve[i - 1].Time <= curve[i].Time, "Keys must stay sorted by time.");
    }

    [Fact]
    public void AddKey_DuplicateTime_AppendsAfterExisting()
    {
        var curve = new AnimationCurve();
        curve.AddKey(0f, 0f);
        curve.AddKey(1f, 1f);

        Assert.Equal(2, curve.AddKey(1f, 5f));
        Assert.Equal(1f, curve[1].Value);
        Assert.Equal(5f, curve[2].Value);
    }

    [Fact]
    public void Indexer_Assignment_Reorders()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f), new Keyframe(2f, 2f));

        curve[0] = new Keyframe(10f, 10f);

        Assert.Equal(new[] { 1f, 2f, 10f }, curve.GetKeys().Select(k => k.Time));
        Assert.Equal(new[] { 1f, 2f, 10f }, curve.GetKeys().Select(k => k.Value));
    }

    [Fact]
    public void MoveKey_SameTime_UpdatesInPlace()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));

        int index = curve.MoveKey(1, new Keyframe(1f, 42f));

        Assert.Equal(1, index);
        Assert.Equal(42f, curve[1].Value);
    }

    [Fact]
    public void MoveKey_NewTime_ReturnsNewIndex()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f), new Keyframe(2f, 2f));

        int index = curve.MoveKey(0, new Keyframe(5f, 0f));

        Assert.Equal(2, index);
        Assert.Equal(new[] { 1f, 2f, 5f }, curve.GetKeys().Select(k => k.Time));
    }

    [Fact]
    public void RemoveKey_ShiftsRemainingKeysDown()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f), new Keyframe(2f, 2f));

        curve.RemoveKey(1);

        Assert.Equal(2, curve.Count);
        Assert.Equal(new[] { 0f, 2f }, curve.GetKeys().Select(k => k.Time));
        Close(1f, curve.Evaluate(1f));
    }

    [Fact]
    public void Clear_RemovesEveryKeyButKeepsDimension()
    {
        var curve = new AnimationCurve(3, new Keyframe(0f, Float3.One));

        curve.Clear();

        Assert.Empty(curve);
        Assert.Equal(3, curve.Dimension);
    }

    [Fact]
    public void OutOfRangeAccess_Throws()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f));

        Assert.Throws<ArgumentOutOfRangeException>(() => curve[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => curve[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.RemoveKey(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.MoveKey(3, new Keyframe(0f, 0f)));
    }

    [Fact]
    public void SetKeys_ReplacesContentAndSorts()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f));

        curve.SetKeys(new[] { new Keyframe(3f, 3f), new Keyframe(1f, 1f) });

        Assert.Equal(2, curve.Count);
        Assert.Equal(new[] { 1f, 3f }, curve.GetKeys().Select(k => k.Time));
    }

    [Fact]
    public void KeysSurviveInternalArrayGrowth()
    {
        var curve = new AnimationCurve(2);
        for (int i = 0; i < 64; i++)
            curve.AddKey(new Keyframe(i, new Float4(i, i * 2f, 0f, 0f), Float4.One, Float4.One, CurveInterpolation.CubicSpline));

        Assert.Equal(64, curve.Count);
        for (int i = 0; i < 64; i++)
        {
            Keyframe key = curve[i];
            Close(i, key.Time);
            Close(i, key.Value);
            Close(i * 2f, key.Value2.Y);
            Close(1f, key.InTangent);
            Close(1f, key.OutTangent);
        }
    }

    [Fact]
    public void Enumeration_YieldsKeysInTimeOrder()
    {
        var curve = new AnimationCurve(new Keyframe(2f, 2f), new Keyframe(0f, 0f), new Keyframe(1f, 1f));

        var times = new List<float>();
        foreach (Keyframe key in curve)
            times.Add(key.Time);

        Assert.Equal(new[] { 0f, 1f, 2f }, times);
        Assert.Equal(3, ((IReadOnlyList<Keyframe>)curve).Count);
    }

    [Fact]
    public void Enumeration_ThrowsWhenTheCurveIsModifiedMidWalk()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f), new Keyframe(2f, 2f));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (Keyframe key in curve)
                curve.RemoveKey(0);
        });
    }

    [Fact]
    public void Enumeration_SurvivesReadOnlyAccessMidWalk()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));

        float total = 0f;
        foreach (Keyframe key in curve)
            total += curve.Evaluate(key.Time);

        Close(1f, total);
    }

    [Fact]
    public void Clear_DropsTangentStorage()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f));
        Assert.False(curve.GetInTangents().IsEmpty);

        curve.Clear();

        Assert.True(curve.GetInTangents().IsEmpty);
        Assert.True(curve.GetOutTangents().IsEmpty);
    }

    [Fact]
    public void Clear_ThenRebuild_StillEvaluates()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f));

        curve.Clear();
        curve.AddKey(new Keyframe(0f, 0f, 1f, 1f));
        curve.AddKey(new Keyframe(2f, 2f, 1f, 1f));

        Close(1f, curve.Evaluate(1f));
    }

    [Fact]
    public void GetInterpolations_MirrorsTheKeys()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f) { Interpolation = CurveInterpolation.Step },
            new Keyframe(1f, 1f),
            new Keyframe(2f, 2f, 0f, 0f));

        Assert.Equal(
            new[] { CurveInterpolation.Step, CurveInterpolation.Linear, CurveInterpolation.CubicSpline },
            curve.GetInterpolations().ToArray());
    }

    [Fact]
    public void TwoComponentCurve_HasItsOwnKeyOverloads()
    {
        var curve = new AnimationCurve(2);
        curve.AddKey(0f, new Float2(0f, 10f));
        curve.AddKey(1f, new Float2(2f, 0f));

        Assert.Equal(new Float2(1f, 5f), curve.EvaluateFloat2(0.5f));
        Assert.Equal(new Float2(0f, 10f), curve[0].Value2);
    }

    [Fact]
    public void TrimExcess_KeepsTheCurveIntact()
    {
        var curve = new AnimationCurve();
        for (int i = 0; i < 5; i++)
            curve.AddKey(i, i * i);
        curve.SmoothTangents(CurveTangentMode.Auto);

        Keyframe[] before = curve.GetKeys();
        float sample = curve.Evaluate(2.5f);
        curve.TrimExcess();

        Assert.Equal(before, curve.GetKeys());
        Assert.Equal(5, curve.GetTimes().Length);
        Close(sample, curve.Evaluate(2.5f));
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f))
        {
            PreWrap = CurveWrapMode.Loop,
            PostWrap = CurveWrapMode.PingPong
        };

        AnimationCurve clone = curve.Clone();
        curve.AddKey(2f, 9f);
        curve[0] = new Keyframe(0f, 100f);

        Assert.Equal(2, clone.Count);
        Assert.Equal(CurveWrapMode.Loop, clone.PreWrap);
        Assert.Equal(CurveWrapMode.PingPong, clone.PostWrap);
        Close(0f, clone[0].Value);
        Close(1f, clone[0].OutTangent);
        Close(0.5f, clone.Evaluate(0.5f));
    }

    #endregion

    #region Interpolation

    [Fact]
    public void Step_HoldsPreviousValueUntilNextKey()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f) { Interpolation = CurveInterpolation.Step },
            new Keyframe(5f, 1f) { Interpolation = CurveInterpolation.Step },
            new Keyframe(10f, 2f) { Interpolation = CurveInterpolation.Step });

        Close(0f, curve.Evaluate(0f));
        Close(0f, curve.Evaluate(4.999f));
        Close(1f, curve.Evaluate(5f));
        Close(1f, curve.Evaluate(7f));
        Close(2f, curve.Evaluate(10f));
    }

    [Fact]
    public void Linear_InterpolatesEvenly()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(4f, 8f));

        Close(0f, curve.Evaluate(0f));
        Close(2f, curve.Evaluate(1f));
        Close(4f, curve.Evaluate(2f));
        Close(8f, curve.Evaluate(4f));
    }

    [Fact]
    public void InterpolationIsPerKey_SoOneCurveCanMixModes()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f) { Interpolation = CurveInterpolation.Step },
            new Keyframe(1f, 10f),
            new Keyframe(2f, 20f));

        Close(0f, curve.Evaluate(0.5f));
        Close(15f, curve.Evaluate(1.5f));
    }

    [Fact]
    public void CubicSpline_WithMatchingSlopes_ProducesStraightLine()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f));

        for (float t = 0f; t <= 1f; t += 0.125f)
            Close(t, curve.Evaluate(t));
    }

    [Fact]
    public void CubicSpline_WithZeroTangents_ProducesSmoothstep()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

        for (float t = 0f; t <= 1f; t += 0.1f)
        {
            float expected = t * t * (3f - 2f * t);
            Close(expected, curve.Evaluate(t));
        }
    }

    [Fact]
    public void CubicSpline_TangentsAreSlopesPerSecond_NotPerSegment()
    {
        // Both curves have slope 1 everywhere, so widening the segment must not change the shape.
        var narrow = new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f));
        var wide = new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(10f, 10f, 1f, 1f));

        for (float t = 0f; t <= 1f; t += 0.25f)
            Close(narrow.Evaluate(t), wide.Evaluate(t));
    }

    [Fact]
    public void CubicSpline_MissingTangentArrays_BehaveAsZero()
    {
        var curve = AnimationCurve.FromPacked(1, new[] { 0f, 1f }, new[] { 0f, 1f }, CurveInterpolation.CubicSpline);

        Assert.True(curve.GetInTangents().IsEmpty);
        Close(0.5f, curve.Evaluate(0.5f));
        Close(0.15625f, curve.Evaluate(0.25f));
    }

    [Fact]
    public void EvaluationAtExactKeyTimes_ReturnsKeyValues()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 3f, 5f, -2f),
            new Keyframe(1.5f, -4f, 1f, 1f),
            new Keyframe(2.25f, 8f, 0f, 0f));

        Close(3f, curve.Evaluate(0f));
        Close(-4f, curve.Evaluate(1.5f));
        Close(8f, curve.Evaluate(2.25f));
    }

    [Fact]
    public void DuplicateKeyTimes_ProduceAnInstantJump()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(1f, 1f),
            new Keyframe(1f, 5f),
            new Keyframe(2f, 6f));

        Close(0.999f, curve.Evaluate(0.999f));
        Close(5f, curve.Evaluate(1f));
        Close(5.5f, curve.Evaluate(1.5f));
    }

    [Fact]
    public void AllKeysAtTheSameTime_DoNotDivideByZero()
    {
        var curve = new AnimationCurve(new Keyframe(2f, 1f), new Keyframe(2f, 4f)) { PreWrap = CurveWrapMode.Loop, PostWrap = CurveWrapMode.Loop };

        Assert.False(float.IsNaN(curve.Evaluate(0f)));
        Assert.False(float.IsNaN(curve.Evaluate(2f)));
        Assert.False(float.IsNaN(curve.Evaluate(50f)));
    }

    [Fact]
    public void BinarySearch_MatchesLinearScanAcrossALargeCurve()
    {
        var rng = new RNG(9876);
        var curve = new AnimationCurve();
        float time = 0f;
        for (int i = 0; i < 500; i++)
        {
            time += 0.01f + rng.NextFloat();
            var key = new Keyframe(time, rng.NextFloat() * 10f - 5f, rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f);
            key.Interpolation = (CurveInterpolation)(i % 3);
            curve.AddKey(key);
        }

        for (int i = 0; i < 2000; i++)
        {
            float t = rng.NextFloat() * (curve.Duration + 4f) + curve.StartTime - 2f;
            Close(ReferenceEvaluate(curve, t), curve.Evaluate(t), 1e-3f);
        }
    }

    #endregion

    #region Wrapping

    [Fact]
    public void Clamp_HoldsTheEndValues()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 5f));

        Close(1f, curve.Evaluate(-10f));
        Close(5f, curve.Evaluate(10f));
    }

    [Fact]
    public void Loop_RepeatsTheCurve()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(2f, 4f))
        {
            PreWrap = CurveWrapMode.Loop,
            PostWrap = CurveWrapMode.Loop
        };

        Close(1f, curve.Evaluate(0.5f));
        Close(1f, curve.Evaluate(2.5f));
        Close(1f, curve.Evaluate(6.5f));
        Close(1f, curve.Evaluate(-1.5f));
    }

    [Fact]
    public void Loop_AtExactCycleBoundary_MapsToStart()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 10f))
        {
            PreWrap = CurveWrapMode.Loop,
            PostWrap = CurveWrapMode.Loop
        };

        Close(0f, curve.Evaluate(-1f));
        Close(0f, curve.Evaluate(-3f));
        Close(0f, curve.Evaluate(2f));
    }

    [Fact]
    public void CycleOffset_StacksTheValueDeltaEachCycle()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 10f))
        {
            PreWrap = CurveWrapMode.CycleOffset,
            PostWrap = CurveWrapMode.CycleOffset
        };

        Close(15f, curve.Evaluate(1.5f));
        Close(25f, curve.Evaluate(2.5f));
        Close(-5f, curve.Evaluate(-0.5f));
    }

    [Fact]
    public void CycleOffset_IsContinuousAcrossTheSeam()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 10f))
        {
            PostWrap = CurveWrapMode.CycleOffset
        };

        Close(curve.Evaluate(0.999f) + 0.02f, curve.Evaluate(1.001f), 1e-2f);
    }

    [Fact]
    public void PingPong_ReversesOnAlternateCycles()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 10f))
        {
            PreWrap = CurveWrapMode.PingPong,
            PostWrap = CurveWrapMode.PingPong
        };

        Close(2.5f, curve.Evaluate(0.25f));
        Close(7.5f, curve.Evaluate(1.25f));
        Close(2.5f, curve.Evaluate(2.25f));
        Close(2.5f, curve.Evaluate(-0.25f));
    }

    [Fact]
    public void Extrapolate_FollowsCubicEndTangents()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f, 3f, 1f), new Keyframe(1f, 5f, 1f, 2f))
        {
            PreWrap = CurveWrapMode.Extrapolate,
            PostWrap = CurveWrapMode.Extrapolate
        };

        Close(-3f, curve.Evaluate(-1f));
        Close(7f, curve.Evaluate(2f));
    }

    [Fact]
    public void Extrapolate_OnLinearCurve_FollowsTheEndSegmentSlope()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(2f, 4f))
        {
            PreWrap = CurveWrapMode.Extrapolate,
            PostWrap = CurveWrapMode.Extrapolate
        };

        Close(-2f, curve.Evaluate(-1f));
        Close(6f, curve.Evaluate(3f));
    }

    [Fact]
    public void Extrapolate_OnSteppedCurve_StaysFlat()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 1f) { Interpolation = CurveInterpolation.Step },
            new Keyframe(1f, 5f) { Interpolation = CurveInterpolation.Step })
        {
            PreWrap = CurveWrapMode.Extrapolate,
            PostWrap = CurveWrapMode.Extrapolate
        };

        Close(1f, curve.Evaluate(-4f));
        Close(5f, curve.Evaluate(4f));
    }

    [Fact]
    public void PreAndPostWrap_AreIndependent()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 10f))
        {
            PreWrap = CurveWrapMode.Clamp,
            PostWrap = CurveWrapMode.Loop
        };

        Close(0f, curve.Evaluate(-5f));
        Close(5f, curve.Evaluate(3.5f));
    }

    [Fact]
    public void Wrapping_AppliesToEveryComponent()
    {
        var curve = new AnimationCurve(3,
            new Keyframe(0f, new Float3(0f, 0f, 0f)),
            new Keyframe(1f, new Float3(1f, 2f, 3f)))
        {
            PostWrap = CurveWrapMode.Loop
        };

        Close(new Float3(0.5f, 1f, 1.5f), curve.EvaluateFloat3(3.5f));
    }

    #endregion

    #region Multiple components

    [Fact]
    public void Float3Curve_InterpolatesPerComponent()
    {
        var curve = new AnimationCurve(3,
            new Keyframe(0f, new Float3(0f, 10f, -4f)),
            new Keyframe(2f, new Float3(4f, 0f, 4f)));

        Close(new Float3(2f, 5f, 0f), curve.EvaluateFloat3(1f));
    }

    [Fact]
    public void EvaluateComponent_ReadsIndividualChannels()
    {
        var curve = new AnimationCurve(3,
            new Keyframe(0f, new Float3(0f, 10f, -4f)),
            new Keyframe(2f, new Float3(4f, 0f, 4f)));

        Close(2f, curve.EvaluateComponent(0, 1f));
        Close(5f, curve.EvaluateComponent(1, 1f));
        Close(0f, curve.EvaluateComponent(2, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.EvaluateComponent(3, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.EvaluateComponent(-1, 1f));
    }

    [Fact]
    public void ComponentsBeyondDimension_ReadAsZero()
    {
        var curve = new AnimationCurve(2, new Keyframe(0f, new Float4(1f, 2f, 9f, 9f)), new Keyframe(1f, new Float4(3f, 4f, 9f, 9f)));

        Float4 value = curve.EvaluateFloat4(0.5f);

        Close(2f, value.X);
        Close(3f, value.Y);
        Close(0f, value.Z);
        Close(0f, value.W);
    }

    [Fact]
    public void EvaluateIntoSpan_FillsEveryComponent()
    {
        var curve = new AnimationCurve(3,
            new Keyframe(0f, new Float3(0f, 0f, 0f)),
            new Keyframe(1f, new Float3(1f, 2f, 3f)));

        Span<float> destination = stackalloc float[3];
        curve.Evaluate(0.5f, destination);

        Close(0.5f, destination[0]);
        Close(1f, destination[1]);
        Close(1.5f, destination[2]);
    }

    [Fact]
    public void EvaluateIntoSpan_RejectsUndersizedDestination()
    {
        var curve = new AnimationCurve(3, new Keyframe(0f, Float3.One));

        Assert.Throws<ArgumentException>(() =>
        {
            float[] destination = new float[2];
            curve.Evaluate(0f, destination);
        });
    }

    [Fact]
    public void VectorCubicSpline_UsesPerComponentTangents()
    {
        var curve = new AnimationCurve(3,
            new Keyframe(0f, new Float3(0f, 0f, 0f), new Float3(0f, 0f, 0f), new Float3(1f, 0f, 1f)),
            new Keyframe(1f, new Float3(1f, 1f, 1f), new Float3(1f, 0f, 1f), new Float3(0f, 0f, 0f)));

        Float3 value = curve.EvaluateFloat3(0.5f);

        Close(0.5f, value.X);          // straight line
        Close(0.5f, value.Y);          // smoothstep, also 0.5 at the midpoint
        Close(0.5f, value.Z);
        Close(0.15625f, curve.EvaluateFloat3(0.25f).Y);
        Close(0.25f, curve.EvaluateFloat3(0.25f).X);
    }

    #endregion

    #region Rotation

    [Fact]
    public void EvaluateQuaternion_RequiresFourComponents()
    {
        var curve = new AnimationCurve(3, new Keyframe(0f, Float3.One));

        Assert.Throws<InvalidOperationException>(() => curve.EvaluateQuaternion(0f));
    }

    [Fact]
    public void EvaluateQuaternion_EmptyCurve_ReturnsIdentity()
    {
        var curve = new AnimationCurve(4);

        Close(Quaternion.Identity, curve.EvaluateQuaternion(0f));
    }

    [Fact]
    public void EvaluateQuaternion_LinearUsesSlerp()
    {
        Quaternion a = Quaternion.AxisAngle(Float3.UnitY, 0f);
        Quaternion b = Quaternion.AxisAngle(Float3.UnitY, Maths.PI * 0.5f);
        var curve = new AnimationCurve(4,
            new Keyframe(0f, new Float4(a.X, a.Y, a.Z, a.W)),
            new Keyframe(1f, new Float4(b.X, b.Y, b.Z, b.W)));

        Quaternion mid = curve.EvaluateQuaternion(0.5f);

        Close(Quaternion.AxisAngle(Float3.UnitY, Maths.PI * 0.25f), mid);
        Close(1f, Maths.Sqrt(mid.X * mid.X + mid.Y * mid.Y + mid.Z * mid.Z + mid.W * mid.W));
    }

    [Fact]
    public void EvaluateQuaternion_SlerpHasConstantAngularVelocity()
    {
        Quaternion a = Quaternion.AxisAngle(Float3.UnitZ, 0f);
        Quaternion b = Quaternion.AxisAngle(Float3.UnitZ, Maths.PI * 0.5f);
        var curve = new AnimationCurve(4,
            new Keyframe(0f, new Float4(a.X, a.Y, a.Z, a.W)),
            new Keyframe(1f, new Float4(b.X, b.Y, b.Z, b.W)));

        for (int i = 0; i <= 8; i++)
        {
            float u = i / 8f;
            Close(Quaternion.AxisAngle(Float3.UnitZ, Maths.PI * 0.5f * u), curve.EvaluateQuaternion(u), 1e-3f);
        }
    }

    [Fact]
    public void EvaluateQuaternion_StepHoldsTheRotation()
    {
        Quaternion a = Quaternion.AxisAngle(Float3.UnitY, 0f);
        Quaternion b = Quaternion.AxisAngle(Float3.UnitY, Maths.PI * 0.5f);
        var curve = new AnimationCurve(4,
            new Keyframe(0f, new Float4(a.X, a.Y, a.Z, a.W)) { Interpolation = CurveInterpolation.Step },
            new Keyframe(1f, new Float4(b.X, b.Y, b.Z, b.W)) { Interpolation = CurveInterpolation.Step });

        Close(a, curve.EvaluateQuaternion(0.9f));
        Close(b, curve.EvaluateQuaternion(1f));
    }

    [Fact]
    public void EvaluateQuaternion_CubicResultIsNormalised()
    {
        var curve = new AnimationCurve(4,
            new Keyframe(0f, new Float4(0f, 0f, 0f, 1f), Float4.Zero, new Float4(0.4f, 0.2f, 0f, 0f)),
            new Keyframe(1f, new Float4(0f, 0.7071f, 0f, 0.7071f), new Float4(0.1f, 0.3f, 0f, 0f), Float4.Zero));

        for (float t = 0f; t <= 1f; t += 0.2f)
        {
            Quaternion q = curve.EvaluateQuaternion(t);
            Close(1f, Maths.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W), 1e-3f);
        }
    }

    [Fact]
    public void EvaluateQuaternion_HonoursLoopWrapping()
    {
        Quaternion a = Quaternion.AxisAngle(Float3.UnitY, 0f);
        Quaternion b = Quaternion.AxisAngle(Float3.UnitY, Maths.PI * 0.5f);
        var curve = new AnimationCurve(4,
            new Keyframe(0f, new Float4(a.X, a.Y, a.Z, a.W)),
            new Keyframe(1f, new Float4(b.X, b.Y, b.Z, b.W)))
        {
            PostWrap = CurveWrapMode.Loop
        };

        Close(curve.EvaluateQuaternion(0.5f), curve.EvaluateQuaternion(4.5f));
    }

    [Fact]
    public void EvaluateQuaternion_CycleOffsetBehavesAsLoop()
    {
        Quaternion a = Quaternion.AxisAngle(Float3.UnitY, 0f);
        Quaternion b = Quaternion.AxisAngle(Float3.UnitY, Maths.PI * 0.5f);
        var keys = new[]
        {
            new Keyframe(0f, new Float4(a.X, a.Y, a.Z, a.W)),
            new Keyframe(1f, new Float4(b.X, b.Y, b.Z, b.W))
        };
        var offset = new AnimationCurve(4, keys) { PostWrap = CurveWrapMode.CycleOffset };
        var loop = new AnimationCurve(4, keys) { PostWrap = CurveWrapMode.Loop };

        Close(loop.EvaluateQuaternion(3.5f), offset.EvaluateQuaternion(3.5f));
    }

    [Fact]
    public void EvaluateQuaternion_ExtrapolateBehavesAsClamp()
    {
        Quaternion a = Quaternion.AxisAngle(Float3.UnitY, 0f);
        Quaternion b = Quaternion.AxisAngle(Float3.UnitY, Maths.PI * 0.5f);
        var curve = new AnimationCurve(4,
            new Keyframe(0f, new Float4(a.X, a.Y, a.Z, a.W), Float4.One, Float4.One),
            new Keyframe(1f, new Float4(b.X, b.Y, b.Z, b.W), Float4.One, Float4.One))
        {
            PreWrap = CurveWrapMode.Extrapolate,
            PostWrap = CurveWrapMode.Extrapolate
        };

        Close(a, curve.EvaluateQuaternion(-5f));
        Close(b, curve.EvaluateQuaternion(5f));
    }

    #endregion

    #region Packed and glTF construction

    [Fact]
    public void FromPacked_AdoptsFlatDataAndEvaluates()
    {
        float[] times = { 0f, 1f, 2f };
        float[] values = { 0f, 0f, 0f, 1f, 2f, 3f, 2f, 4f, 6f };

        AnimationCurve curve = AnimationCurve.FromPacked(3, times, values, CurveInterpolation.Linear);

        Assert.Equal(3, curve.Count);
        Assert.Equal(3, curve.Dimension);
        Close(new Float3(0.5f, 1f, 1.5f), curve.EvaluateFloat3(0.5f));
        Close(new Float3(1.5f, 3f, 4.5f), curve.EvaluateFloat3(1.5f));
    }

    [Fact]
    public void FromPacked_ExposesRawSpans()
    {
        float[] times = { 0f, 1f };
        float[] values = { 3f, 4f };

        AnimationCurve curve = AnimationCurve.FromPacked(1, times, values, CurveInterpolation.Linear);

        Assert.Equal(new[] { 0f, 1f }, curve.GetTimes().ToArray());
        Assert.Equal(new[] { 3f, 4f }, curve.GetValues().ToArray());
        Assert.True(curve.GetOutTangents().IsEmpty);
    }

    [Fact]
    public void FromPacked_RejectsMismatchedValueCount() =>
        Assert.Throws<ArgumentException>(() => AnimationCurve.FromPacked(3, new[] { 0f, 1f }, new float[5], CurveInterpolation.Linear));

    [Fact]
    public void FromPacked_RejectsMismatchedTangentCount() =>
        Assert.Throws<ArgumentException>(() =>
            AnimationCurve.FromPacked(1, new[] { 0f, 1f }, new float[2], new float[3], null, CurveInterpolation.CubicSpline));

    [Fact]
    public void FromPacked_RejectsUnsortedTimes() =>
        Assert.Throws<ArgumentException>(() => AnimationCurve.FromPacked(1, new[] { 0f, 2f, 1f }, new float[3], CurveInterpolation.Linear));

    [Fact]
    public void FromPacked_RejectsNullArrays()
    {
        Assert.Throws<ArgumentNullException>(() => AnimationCurve.FromPacked(1, null!, new float[1], CurveInterpolation.Linear));
        Assert.Throws<ArgumentNullException>(() => AnimationCurve.FromPacked(1, new float[1], null!, CurveInterpolation.Linear));
    }

    [Fact]
    public void FromPacked_CurveRemainsEditable()
    {
        AnimationCurve curve = AnimationCurve.FromPacked(1, new[] { 0f, 1f }, new[] { 0f, 1f }, CurveInterpolation.Linear);

        curve.AddKey(2f, 4f);

        Assert.Equal(3, curve.Count);
        Close(2.5f, curve.Evaluate(1.5f));
    }

    [Fact]
    public void FromGltfCubicSpline_DeinterleavesTangentValueTangent()
    {
        // Layout per key: inTangent, value, outTangent.
        float[] times = { 0f, 1f };
        float[] interleaved = { 5f, 0f, 1f, 2f, 1f, 7f };

        AnimationCurve curve = AnimationCurve.FromGltfCubicSpline(1, times, interleaved);

        Close(5f, curve[0].InTangent);
        Close(0f, curve[0].Value);
        Close(1f, curve[0].OutTangent);
        Close(2f, curve[1].InTangent);
        Close(1f, curve[1].Value);
        Close(7f, curve[1].OutTangent);
        Close(0.375f, curve.Evaluate(0.5f));
    }

    [Fact]
    public void FromGltfCubicSpline_MatchesTheSpecHermiteBasis()
    {
        float[] times = { 0f, 2f };
        float[] interleaved = { 0f, 1f, 3f, -1f, 5f, 0f };

        AnimationCurve curve = AnimationCurve.FromGltfCubicSpline(1, times, interleaved);

        const float u = 0.35f;
        float u2 = u * u;
        float u3 = u2 * u;
        float expected = (2f * u3 - 3f * u2 + 1f) * 1f
                       + (u3 - 2f * u2 + u) * 2f * 3f
                       + (-2f * u3 + 3f * u2) * 5f
                       + (u3 - u2) * 2f * -1f;

        Close(expected, curve.Evaluate(u * 2f));
    }

    [Fact]
    public void FromGltfCubicSpline_HandlesVectorChannels()
    {
        float[] times = { 0f, 1f };
        float[] interleaved =
        {
            0f, 0f, 0f,   0f, 0f, 0f,   0f, 0f, 0f,
            0f, 0f, 0f,   1f, 2f, 3f,   0f, 0f, 0f
        };

        AnimationCurve curve = AnimationCurve.FromGltfCubicSpline(3, times, interleaved);

        Close(new Float3(0.5f, 1f, 1.5f), curve.EvaluateFloat3(0.5f));
        Close(new Float3(1f, 2f, 3f), curve.EvaluateFloat3(1f));
    }

    [Fact]
    public void FromGltfCubicSpline_RejectsMismatchedOutputLength() =>
        Assert.Throws<ArgumentException>(() => AnimationCurve.FromGltfCubicSpline(3, new[] { 0f, 1f }, new float[12]));

    #endregion

    #region Tangent smoothing

    [Fact]
    public void SmoothTangents_SwitchesEveryKeyToCubic()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));

        curve.SmoothTangents(CurveTangentMode.Auto);

        Assert.All(curve.GetKeys(), k => Assert.Equal(CurveInterpolation.CubicSpline, k.Interpolation));
    }

    [Fact]
    public void SmoothTangents_Flat_ProducesSmoothstep()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));

        curve.SmoothTangents(CurveTangentMode.Flat);

        Close(0f, curve[0].OutTangent);
        Close(0f, curve[1].InTangent);
        Close(0.15625f, curve.Evaluate(0.25f));
    }

    [Fact]
    public void SmoothTangents_Auto_ReproducesAStraightLine()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(1f, 2f),
            new Keyframe(3f, 6f),
            new Keyframe(4f, 8f));

        curve.SmoothTangents(CurveTangentMode.Auto);

        for (float t = 0f; t <= 4f; t += 0.25f)
            Close(t * 2f, curve.Evaluate(t));
    }

    [Fact]
    public void SmoothTangents_Auto_UsesTheCatmullRomSlope()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(1f, 3f),
            new Keyframe(3f, 1f));

        curve.SmoothTangents(CurveTangentMode.Auto);

        Close((1f - 0f) / 3f, curve[1].InTangent);
        Close((1f - 0f) / 3f, curve[1].OutTangent);
        Close(3f, curve[0].OutTangent);      // one sided at the ends
        Close(-1f, curve[2].InTangent);
    }

    [Fact]
    public void SmoothTangents_Linear_UsesTheAdjacentSegmentSlopes()
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(1f, 4f),
            new Keyframe(3f, 6f));

        curve.SmoothTangents(CurveTangentMode.Linear);

        Close(4f, curve[1].InTangent);
        Close(1f, curve[1].OutTangent);
        Close(4f, curve[0].InTangent);       // falls back to the only available side
        Close(4f, curve[0].OutTangent);
    }

    [Fact]
    public void SmoothTangents_ClampedAuto_FlattensLocalExtremaSoTheCurveDoesNotOvershoot()
    {
        var peak = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f), new Keyframe(2f, 0.9f));

        peak.SmoothTangents(CurveTangentMode.ClampedAuto);

        Close(0f, peak[1].InTangent);
        Close(0f, peak[1].OutTangent);
        for (float t = 0f; t <= 2f; t += 0.05f)
            Assert.True(peak.Evaluate(t) <= 1f + Eps, $"Overshot the peak at {t}");
    }

    [Fact]
    public void SmoothTangents_ClampedAuto_LeavesMonotonicSectionsAlone()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f), new Keyframe(2f, 2f));

        curve.SmoothTangents(CurveTangentMode.ClampedAuto);

        Close(1f, curve[1].OutTangent);
    }

    [Fact]
    public void SmoothTangents_SeparateInAndOutModes()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 4f), new Keyframe(3f, 6f));

        curve.SmoothTangents(CurveTangentMode.Flat, CurveTangentMode.Linear);

        Close(0f, curve[1].InTangent);
        Close(1f, curve[1].OutTangent);
    }

    [Fact]
    public void SmoothTangent_TouchesOnlyTheRequestedKey()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 4f), new Keyframe(3f, 6f));

        curve.SmoothTangent(1, CurveTangentMode.Flat);

        Assert.Equal(CurveInterpolation.CubicSpline, curve[1].Interpolation);
        Assert.Equal(CurveInterpolation.Linear, curve[0].Interpolation);
        Assert.Equal(CurveInterpolation.Linear, curve[2].Interpolation);
    }

    [Fact]
    public void SmoothTangents_HandlesDuplicateTimesWithoutProducingNaN()
    {
        var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f), new Keyframe(1f, 5f), new Keyframe(2f, 6f));

        curve.SmoothTangents(CurveTangentMode.Auto);
        curve.SmoothTangents(CurveTangentMode.Linear);

        foreach (Keyframe key in curve)
        {
            Assert.False(float.IsNaN(key.InTangent));
            Assert.False(float.IsNaN(key.OutTangent));
        }
        Assert.False(float.IsNaN(curve.Evaluate(0.5f)));
    }

    [Fact]
    public void SmoothTangents_AppliesToEveryComponent()
    {
        var curve = new AnimationCurve(3,
            new Keyframe(0f, new Float3(0f, 0f, 0f)),
            new Keyframe(1f, new Float3(1f, 2f, 3f)),
            new Keyframe(2f, new Float3(2f, 4f, 6f)));

        curve.SmoothTangents(CurveTangentMode.Auto);

        Close(new Float3(1f, 2f, 3f), curve[1].OutTangent3);
        Close(new Float3(0.5f, 1f, 1.5f), curve.EvaluateFloat3(0.5f));
    }

    [Fact]
    public void SmoothTangents_OnEmptyCurve_IsANoOp()
    {
        var curve = new AnimationCurve();

        curve.SmoothTangents(CurveTangentMode.Auto);

        Assert.Empty(curve);
    }

    #endregion

    #region Keyframe

    [Fact]
    public void Keyframe_DefaultsToLinear()
    {
        var key = new Keyframe(1f, 2f);

        Assert.Equal(CurveInterpolation.Linear, key.Interpolation);
        Assert.Equal(0f, key.InTangent);
        Assert.Equal(0f, key.OutTangent);
    }

    [Fact]
    public void Keyframe_TangentConstructorImpliesCubic() =>
        Assert.Equal(CurveInterpolation.CubicSpline, new Keyframe(1f, 2f, 3f, 4f).Interpolation);

    [Fact]
    public void Keyframe_ComponentAccessorsAgree()
    {
        var key = new Keyframe(0f, new Float4(1f, 2f, 3f, 4f));

        Assert.Equal(1f, key.Value);
        Assert.Equal(new Float2(1f, 2f), key.Value2);
        Assert.Equal(new Float3(1f, 2f, 3f), key.Value3);
        Assert.Equal(new Float4(1f, 2f, 3f, 4f), key.Value4);

        key.Value3 = new Float3(9f, 8f, 7f);
        Assert.Equal(new Float4(9f, 8f, 7f, 4f), key.Value4);
    }

    [Fact]
    public void Keyframe_WithHelpers_ReturnCopies()
    {
        var key = new Keyframe(1f, 2f);

        Keyframe stepped = key.WithInterpolation(CurveInterpolation.Step);
        Keyframe moved = key.WithTime(5f);

        Assert.Equal(CurveInterpolation.Linear, key.Interpolation);
        Assert.Equal(CurveInterpolation.Step, stepped.Interpolation);
        Assert.Equal(1f, key.Time);
        Assert.Equal(5f, moved.Time);
    }

    [Fact]
    public void Keyframe_EqualityComparesEveryField()
    {
        var a = new Keyframe(1f, 2f, 3f, 4f);
        var b = new Keyframe(1f, 2f, 3f, 4f);
        var c = new Keyframe(1f, 2f, 3f, 5f);

        Assert.True(a == b);
        Assert.True(a != c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Keyframe_SortsByTime()
    {
        var keys = new List<Keyframe> { new Keyframe(3f, 0f), new Keyframe(1f, 0f), new Keyframe(2f, 0f) };

        keys.Sort();

        Assert.Equal(new[] { 1f, 2f, 3f }, keys.Select(k => k.Time));
    }

    #endregion
}
