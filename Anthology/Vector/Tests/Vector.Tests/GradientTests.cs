// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Xunit;

namespace Prowl.Vector.Tests;

public class GradientTests
{
    private const float Eps = 1e-4f;

    private static void Close(float expected, float actual, float eps = Eps) =>
        Assert.True(Maths.Abs(expected - actual) <= eps, $"Expected {expected} but got {actual}");

    private static void Close(Color expected, Color actual, float eps = Eps) =>
        Assert.True(
            Maths.Abs(expected.R - actual.R) <= eps &&
            Maths.Abs(expected.G - actual.G) <= eps &&
            Maths.Abs(expected.B - actual.B) <= eps &&
            Maths.Abs(expected.A - actual.A) <= eps,
            $"Expected {expected} but got {actual}");

    private static Gradient RedToBlue() =>
        new Gradient(
            new[] { new GradientColorKey(0f, Color.Red), new GradientColorKey(1f, Color.Blue) },
            new[] { new GradientAlphaKey(0f, 1f), new GradientAlphaKey(1f, 1f) });

    #region Construction

    [Fact]
    public void DefaultGradient_IsOpaqueWhite()
    {
        var gradient = new Gradient();

        Close(Color.White, gradient.Evaluate(0f));
        Close(Color.White, gradient.Evaluate(0.5f));
        Close(Color.White, gradient.Evaluate(1f));
        Assert.Equal(2, gradient.ColorKeys.Count);
        Assert.Equal(2, gradient.AlphaKeys.Count);
    }

    [Fact]
    public void Constructor_SortsUnorderedKeys()
    {
        var gradient = new Gradient(
            new[] { new GradientColorKey(1f, Color.Blue), new GradientColorKey(0f, Color.Red) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });

        Assert.Equal(new[] { 0f, 1f }, gradient.ColorKeys.Select(k => k.Time));
        Assert.Equal(new[] { 0f, 1f }, gradient.AlphaKeys.Select(k => k.Time));
        Close(Color.Red, gradient.Evaluate(0f));
    }

    [Fact]
    public void Solid_IsOneColourEverywhere()
    {
        var gradient = Gradient.Solid(new Color(0.25f, 0.5f, 0.75f, 0.5f));

        Close(new Color(0.25f, 0.5f, 0.75f, 0.5f), gradient.Evaluate(-5f));
        Close(new Color(0.25f, 0.5f, 0.75f, 0.5f), gradient.Evaluate(0.5f));
        Close(new Color(0.25f, 0.5f, 0.75f, 0.5f), gradient.Evaluate(5f));
    }

    [Fact]
    public void Between_RampsAcrossTheGivenRange()
    {
        var gradient = Gradient.Between(Color.Black, Color.White, 2f, 4f);

        Close(Color.Black, gradient.Evaluate(2f));
        Close(new Color(0.5f, 0.5f, 0.5f, 1f), gradient.Evaluate(3f));
        Close(Color.White, gradient.Evaluate(4f));
        Close(2f, gradient.StartTime);
        Close(4f, gradient.EndTime);
    }

    [Fact]
    public void Between_CarriesTheEndpointAlphas()
    {
        var gradient = Gradient.Between(new Color(1f, 0f, 0f, 0f), new Color(0f, 0f, 1f, 1f));

        Close(0f, gradient.EvaluateAlpha(0f));
        Close(0.5f, gradient.EvaluateAlpha(0.5f));
        Close(1f, gradient.EvaluateAlpha(1f));
    }

    #endregion

    #region Evaluation

    [Fact]
    public void Evaluate_BlendsBetweenColourKeys()
    {
        Gradient gradient = RedToBlue();

        Close(Color.Red, gradient.Evaluate(0f));
        Close(new Color(0.5f, 0f, 0.5f, 1f), gradient.Evaluate(0.5f));
        Close(Color.Blue, gradient.Evaluate(1f));
    }

    [Fact]
    public void Evaluate_PastTheLastKey_HoldsTheLastColour()
    {
        var gradient = new Gradient(
            new[] { new GradientColorKey(0f, Color.Red), new GradientColorKey(0.5f, Color.Blue) },
            new[] { new GradientAlphaKey(0f, 1f), new GradientAlphaKey(0.5f, 0f) });

        Close(Color.Blue, gradient.EvaluateRgb(0.8f));
        Close(Color.Blue, gradient.EvaluateRgb(1f));
        Close(0f, gradient.EvaluateAlpha(0.8f));
    }

    [Fact]
    public void Evaluate_BeforeTheFirstKey_HoldsTheFirstColour()
    {
        var gradient = new Gradient(
            new[] { new GradientColorKey(0.5f, Color.Red), new GradientColorKey(1f, Color.Blue) },
            new[] { new GradientAlphaKey(0.5f, 0.25f), new GradientAlphaKey(1f, 1f) });

        Close(Color.Red, gradient.EvaluateRgb(0f));
        Close(Color.Red, gradient.EvaluateRgb(-10f));
        Close(0.25f, gradient.EvaluateAlpha(0f));
    }

    [Fact]
    public void Evaluate_HandlesTimesOutsideZeroToOne()
    {
        var gradient = Gradient.Between(Color.Black, Color.White, -2f, 2f);

        Close(Color.Black, gradient.Evaluate(-2f));
        Close(new Color(0.5f, 0.5f, 0.5f, 1f), gradient.Evaluate(0f));
        Close(Color.White, gradient.Evaluate(2f));
    }

    [Fact]
    public void Evaluate_ColourAndAlphaKeysAreIndependent()
    {
        var gradient = new Gradient(
            new[] { new GradientColorKey(0f, Color.Red), new GradientColorKey(1f, Color.Blue) },
            new[] { new GradientAlphaKey(0f, 1f), new GradientAlphaKey(0.25f, 0f), new GradientAlphaKey(1f, 1f) });

        Color mid = gradient.Evaluate(0.25f);

        Close(0.75f, mid.R);
        Close(0.25f, mid.B);
        Close(0f, mid.A);
    }

    [Fact]
    public void Evaluate_AlphaKeysMayCoverADifferentRangeToColourKeys()
    {
        var gradient = new Gradient(
            new[] { new GradientColorKey(0f, Color.Red), new GradientColorKey(1f, Color.Blue) },
            new[] { new GradientAlphaKey(2f, 0.5f) });

        Close(0.5f, gradient.EvaluateAlpha(0f));
        Close(0.5f, gradient.EvaluateAlpha(10f));
        Close(0f, gradient.StartTime);
        Close(2f, gradient.EndTime);
    }

    [Fact]
    public void Evaluate_EmptyGradientIsOpaqueWhite()
    {
        var gradient = new Gradient();
        gradient.Clear();

        Close(Color.White, gradient.Evaluate(0.5f));
        Assert.Empty(gradient.ColorKeys);
    }

    [Fact]
    public void Evaluate_SingleColourKeyHoldsEverywhere()
    {
        var gradient = new Gradient(
            new[] { new GradientColorKey(0.5f, Color.Green) },
            Array.Empty<GradientAlphaKey>());

        Close(Color.Green, gradient.EvaluateRgb(0f));
        Close(Color.Green, gradient.EvaluateRgb(0.5f));
        Close(Color.Green, gradient.EvaluateRgb(1f));
        Close(1f, gradient.EvaluateAlpha(0.5f));
    }

    [Fact]
    public void Evaluate_DuplicateKeyTimesDoNotProduceNaN()
    {
        var gradient = new Gradient(
            new[]
            {
                new GradientColorKey(0f, Color.Red),
                new GradientColorKey(0.5f, Color.Green),
                new GradientColorKey(0.5f, Color.Blue),
                new GradientColorKey(1f, Color.White)
            },
            new[] { new GradientAlphaKey(0.5f, 1f), new GradientAlphaKey(0.5f, 0f) });

        for (float t = 0f; t <= 1f; t += 0.05f)
        {
            Color c = gradient.Evaluate(t);
            Assert.False(float.IsNaN(c.R) || float.IsNaN(c.G) || float.IsNaN(c.B) || float.IsNaN(c.A));
        }
        Close(Color.Blue, gradient.EvaluateRgb(0.5f));
    }

    [Fact]
    public void Evaluate_ManyKeysStillLandOnTheRightSegment()
    {
        var gradient = new Gradient();
        gradient.Clear();
        for (int i = 0; i <= 20; i++)
            gradient.AddColorKey(i / 20f, new Color(i / 20f, 0f, 0f, 1f));

        for (int i = 0; i <= 20; i++)
            Close(i / 20f, gradient.EvaluateRgb(i / 20f).R);
        Close(0.525f, gradient.EvaluateRgb(0.525f).R);
    }

    [Fact]
    public void EvaluateRgb_AlwaysReturnsOpaque()
    {
        var gradient = new Gradient(
            new[] { new GradientColorKey(0f, new Color(1f, 0f, 0f, 0f)) },
            new[] { new GradientAlphaKey(0f, 0f) });

        Close(1f, gradient.EvaluateRgb(0f).A);
        Close(0f, gradient.Evaluate(0f).A);
    }

    #endregion

    #region Fixed mode

    [Fact]
    public void FixedMode_HoldsEachKeyUntilTheNext()
    {
        Gradient gradient = RedToBlue();
        gradient.AddColorKey(0.5f, Color.Green);
        gradient.Mode = GradientMode.Fixed;

        Close(Color.Red, gradient.EvaluateRgb(0f));
        Close(Color.Red, gradient.EvaluateRgb(0.49f));
        Close(Color.Green, gradient.EvaluateRgb(0.5f));
        Close(Color.Green, gradient.EvaluateRgb(0.99f));
        Close(Color.Blue, gradient.EvaluateRgb(1f));
    }

    [Fact]
    public void FixedMode_AppliesToAlphaToo()
    {
        var gradient = new Gradient(
            new[] { new GradientColorKey(0f, Color.White) },
            new[] { new GradientAlphaKey(0f, 1f), new GradientAlphaKey(0.5f, 0f) })
        {
            Mode = GradientMode.Fixed
        };

        Close(1f, gradient.EvaluateAlpha(0.25f));
        Close(0f, gradient.EvaluateAlpha(0.5f));
    }

    #endregion

    #region Editing

    [Fact]
    public void AddColorKey_KeepsKeysSortedAndReturnsIndex()
    {
        var gradient = new Gradient();
        gradient.Clear();

        Assert.Equal(0, gradient.AddColorKey(0.5f, Color.Green));
        Assert.Equal(0, gradient.AddColorKey(0.1f, Color.Red));
        Assert.Equal(2, gradient.AddColorKey(0.9f, Color.Blue));

        Assert.Equal(new[] { 0.1f, 0.5f, 0.9f }, gradient.ColorKeys.Select(k => k.Time));
    }

    [Fact]
    public void AddAlphaKey_KeepsKeysSorted()
    {
        var gradient = new Gradient();
        gradient.Clear();

        gradient.AddAlphaKey(1f, 1f);
        gradient.AddAlphaKey(0f, 0f);
        gradient.AddAlphaKey(0.5f, 0.5f);

        Assert.Equal(new[] { 0f, 0.5f, 1f }, gradient.AlphaKeys.Select(k => k.Time));
    }

    [Fact]
    public void SetColorKey_MovingAKeyReordersAndReturnsTheNewIndex()
    {
        Gradient gradient = RedToBlue();

        int index = gradient.SetColorKey(0, new GradientColorKey(2f, Color.Red));

        Assert.Equal(1, index);
        Assert.Equal(new[] { 1f, 2f }, gradient.ColorKeys.Select(k => k.Time));
    }

    [Fact]
    public void SetColorKey_KeepingTheTimeUpdatesInPlace()
    {
        Gradient gradient = RedToBlue();

        int index = gradient.SetColorKey(0, new GradientColorKey(0f, Color.Green));

        Assert.Equal(0, index);
        Close(Color.Green, gradient.EvaluateRgb(0f));
    }

    [Fact]
    public void SetAlphaKey_Reorders()
    {
        Gradient gradient = RedToBlue();

        int index = gradient.SetAlphaKey(0, new GradientAlphaKey(5f, 0.25f));

        Assert.Equal(1, index);
        Close(0.25f, gradient.EvaluateAlpha(5f));
    }

    [Fact]
    public void RemoveKeys_ShrinkTheLists()
    {
        Gradient gradient = RedToBlue();

        gradient.RemoveColorKey(0);
        gradient.RemoveAlphaKey(1);

        Assert.Single(gradient.ColorKeys);
        Assert.Single(gradient.AlphaKeys);
        Close(Color.Blue, gradient.EvaluateRgb(0f));
    }

    [Fact]
    public void OutOfRangeEdits_Throw()
    {
        Gradient gradient = RedToBlue();

        Assert.Throws<ArgumentOutOfRangeException>(() => gradient.RemoveColorKey(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => gradient.RemoveAlphaKey(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => gradient.SetColorKey(9, new GradientColorKey(0f, Color.Red)));
        Assert.Throws<ArgumentOutOfRangeException>(() => gradient.SetAlphaKey(9, new GradientAlphaKey(0f, 1f)));
    }

    [Fact]
    public void SetKeys_ReplacesAndSorts()
    {
        Gradient gradient = RedToBlue();

        gradient.SetKeys(
            new[] { new GradientColorKey(1f, Color.White), new GradientColorKey(0f, Color.Black) },
            new[] { new GradientAlphaKey(0f, 0f) });

        Assert.Equal(new[] { 0f, 1f }, gradient.ColorKeys.Select(k => k.Time));
        Assert.Single(gradient.AlphaKeys);
        Close(Color.Black, gradient.EvaluateRgb(0f));
    }

    [Fact]
    public void SetKeys_RejectsNull()
    {
        Gradient gradient = RedToBlue();

        Assert.Throws<ArgumentNullException>(() => gradient.SetKeys(null!, Array.Empty<GradientAlphaKey>()));
        Assert.Throws<ArgumentNullException>(() => gradient.SetKeys(Array.Empty<GradientColorKey>(), null!));
    }

    [Fact]
    public void FindKey_ReturnsTheNearestWithinTolerance()
    {
        Gradient gradient = RedToBlue();
        gradient.AddColorKey(0.5f, Color.Green);

        Assert.Equal(1, gradient.FindColorKey(0.52f, 0.05f));
        Assert.Equal(-1, gradient.FindColorKey(0.7f, 0.05f));
        Assert.Equal(0, gradient.FindAlphaKey(0.01f, 0.05f));
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        Gradient gradient = RedToBlue();
        gradient.Mode = GradientMode.Fixed;

        Gradient clone = gradient.Clone();
        gradient.AddColorKey(0.5f, Color.Green);
        gradient.Mode = GradientMode.Blend;

        Assert.Equal(2, clone.ColorKeys.Count);
        Assert.Equal(GradientMode.Fixed, clone.Mode);
        Close(Color.Red, clone.EvaluateRgb(0.25f));
    }

    [Fact]
    public void KeyLists_AreReadOnlyViews()
    {
        Gradient gradient = RedToBlue();

        Assert.Equal(2, gradient.ColorKeys.Count);
        Assert.Equal(0f, gradient.ColorKeys[0].Time);
        Assert.IsNotType<GradientColorKey[]>(gradient.ColorKeys);
    }

    #endregion

    #region Keys

    [Fact]
    public void ColorKey_EqualityAndOrdering()
    {
        var a = new GradientColorKey(0.5f, Color.Red);
        var b = new GradientColorKey(0.5f, Color.Red);
        var c = new GradientColorKey(0.6f, Color.Red);

        Assert.True(a == b);
        Assert.True(a != c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.CompareTo(c) < 0);
    }

    [Fact]
    public void AlphaKey_EqualityAndOrdering()
    {
        var a = new GradientAlphaKey(0.5f, 1f);
        var b = new GradientAlphaKey(0.5f, 1f);
        var c = new GradientAlphaKey(0.5f, 0f);

        Assert.True(a == b);
        Assert.True(a != c);
        Assert.Equal(0, a.CompareTo(c));
    }

    #endregion
}
