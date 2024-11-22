// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime.GUI;

public enum EaseType
{
    Linear = 0,
    SineIn = 10,
    SineOut = 11,
    SineInOut = 12,
    QuadIn = 20,
    QuadOut = 21,
    QuadInOut = 22,
    CubicIn = 30,
    CubicOut = 31,
    CubicInOut = 32,
    QuartIn = 40,
    QuartOut = 41,
    QuartInOut = 42,
    QuintIn = 50,
    QuintOut = 51,
    QuintInOut = 52,
    ExpoIn = 60,
    ExpoOut = 61,
    ExpoInOut = 62,
    CircIn = 70,
    CircOut = 71,
    CircInOut = 72,
    BackIn = 80,
    BackOut = 81,
    BackInOut = 82,
    ElasticIn = 90,
    ElasticOut = 91,
    ElasticInOut = 92,
    BounceIn = 100,
    BounceOut = 101,
    BounceInOut = 102,
}

public partial class Gui
{
    private readonly Dictionary<ulong, BoolAnimation> _boolAnimations = [];

    /// <inheritdoc cref="AnimateBool(ulong, bool, float, EaseType)"/>
    public float AnimateBool(bool state, float durationIn, float durationOut, EaseType easeIn, EaseType easeOut)
        => AnimateBool(GetNextID(), state, state ? durationOut : durationIn, state ? easeOut : easeIn);
    public float AnimateBool(bool state, float durationIn, float durationOut, EaseType type)
        => AnimateBool(GetNextID(), state, state ? durationOut : durationIn, type);
    /// <inheritdoc cref="AnimateBool(ulong, bool, float, EaseType)"/>
    public float AnimateBool(bool state, float duration, EaseType easeIn, EaseType easeOut)
        => AnimateBool(GetNextID(), state, duration, state ? easeOut : easeIn);
    /// <inheritdoc cref="AnimateBool(ulong, bool, float, EaseType)"/>
    public float AnimateBool(bool state, float duration, EaseType ease)
        => AnimateBool(GetNextID(), state, duration, ease);

    /// <summary>
    /// Create and animate a bool value over time
    /// This is useful for creating animations based on bool values
    ///
    /// An ID will be assigned based on the current Node and the next available ID
    /// You can manually assign an ID if you want it to persist across nodes
    /// </summary>
    /// <returns>Returns a Float value between 0 and 1 which is animated over time based on the EaseType
    /// A state of true will animate to 1, a state of false will animate to 0</returns>
    public float AnimateBool(ulong animId, bool state, float duration, EaseType type)
    {
        BoolAnimation anim;
        if (_boolAnimations.TryGetValue(animId, out anim))
        {
            anim.CurrentValue = state;
            anim.Duration = duration;
            anim.EaseType = type;
        }
        else
        {
            anim = new BoolAnimation
            {
                CurrentValue = state,
                Duration = duration,
                EaseType = type,
                ElapsedTime = state ? 1 : 0
            };
        }
        _boolAnimations[animId] = anim;

        return (float)GetEase(anim.ElapsedTime, anim.EaseType);
    }

    private ulong GetNextID() => ProwlHash.Combine(CurrentNode.ID, CurrentNode.GetNextAnimation());

    private void UpdateAnimations(double dt)
    {
        foreach (var storageID in _boolAnimations.Keys)
        {
            BoolAnimation anim = _boolAnimations[storageID];
            double speed = 1.0 / anim.Duration;
            anim.ElapsedTime = MathD.MoveTowards(anim.ElapsedTime, anim.CurrentValue ? 1 : 0, dt * speed);
            _boolAnimations[storageID] = anim;
        }
    }

    private struct BoolAnimation
    {
        public bool CurrentValue;
        public EaseType EaseType;
        public double Duration;
        public double ElapsedTime;
    }

    #region Easing

    static double GetEase(double time, EaseType type)
    {
        return type switch
        {
            EaseType.Linear       => Linear(time),
            EaseType.SineIn       => SineIn(time),
            EaseType.SineOut      => SineOut(time),
            EaseType.SineInOut    => SineInOut(time),
            EaseType.QuadIn       => QuadIn(time),
            EaseType.QuadOut      => QuadOut(time),
            EaseType.QuadInOut    => QuadInOut(time),
            EaseType.CubicIn      => CubicIn(time),
            EaseType.CubicOut     => CubicOut(time),
            EaseType.CubicInOut   => CubicInOut(time),
            EaseType.QuartIn      => QuartIn(time),
            EaseType.QuartOut     => QuartOut(time),
            EaseType.QuartInOut   => QuartInOut(time),
            EaseType.QuintIn      => QuintIn(time),
            EaseType.QuintOut     => QuintOut(time),
            EaseType.QuintInOut   => QuintInOut(time),
            EaseType.ExpoIn       => ExpoIn(time),
            EaseType.ExpoOut      => ExpoOut(time),
            EaseType.ExpoInOut    => ExpoInOut(time),
            EaseType.CircIn       => CircIn(time),
            EaseType.CircOut      => CircOut(time),
            EaseType.CircInOut    => CircInOut(time),
            EaseType.BackIn       => BackIn(time),
            EaseType.BackOut      => BackOut(time),
            EaseType.BackInOut    => BackInOut(time),
            EaseType.ElasticIn    => ElasticIn(time),
            EaseType.ElasticOut   => ElasticOut(time),
            EaseType.ElasticInOut => ElasticInOut(time),
            EaseType.BounceIn     => BounceIn(time),
            EaseType.BounceOut    => BounceOut(time),
            EaseType.BounceInOut  => BounceInOut(time),
            _                     => Linear(time),
        };
    }

    const double ConstantA = 1.70158;
    const double ConstantB = ConstantA * 1.525;
    const double ConstantC = ConstantA + 1.0;
    const double ConstantD = 2.0 * MathD.PI / 3.0;
    const double ConstantE = 2.0 * MathD.PI / 4.5;
    const double ConstantF = 7.5625;
    const double ConstantG = 2.75;

    static double Linear(double time) => time;

    static double SineIn(double time) => 1.0 - MathD.Cos((time * MathD.PI) / 2.0);
    static double SineOut(double time) => MathD.Sin((time * MathD.PI) / 2.0);
    static double SineInOut(double time) => -(MathD.Cos(MathD.PI * time) - 1.0) / 2.0;

    static double QuadIn(double time) => time * time;
    static double QuadOut(double time) => 1 - (1 - time) * (1 - time);
    static double QuadInOut(double time) => time < 0.5 ? 2 * time * time : 1 - MathD.Pow(-2 * time + 2, 2) / 2;

    static double CubicIn(double time) => time * time * time;
    static double CubicOut(double time) => 1 - MathD.Pow(1 - time, 3);
    static double CubicInOut(double time) => time < 0.5 ? 4 * time * time * time : 1 - MathD.Pow(-2 * time + 2, 3) / 2;

    static double QuartIn(double time) => time * time * time * time;
    static double QuartOut(double time) => 1 - MathD.Pow(1 - time, 4);
    static double QuartInOut(double time) => time < 0.5 ? 8 * time * time * time * time : 1 - MathD.Pow(-2 * time + 2, 4) / 2;

    static double QuintIn(double time) => time * time * time * time * time;
    static double QuintOut(double time) => 1 - MathD.Pow(1 - time, 5);
    static double QuintInOut(double time) => time < 0.5 ? 16 * time * time * time * time * time : 1 - MathD.Pow(-2 * time + 2, 5) / 2;

    static double ExpoIn(double time) => time == 0 ? 0 : MathD.Pow(2, 10 * time - 10);
    static double ExpoOut(double time) => MathD.ApproximatelyEquals(time, 1) ? 1 : 1 - MathD.Pow(2, -10 * time);
    static double ExpoInOut(double time) => time == 0 ? 0 : MathD.ApproximatelyEquals(time, 1) ? 1 : time < 0.5 ? MathD.Pow(2, 20 * time - 10) / 2 : (2 - MathD.Pow(2, -20 * time + 10)) / 2;

    static double CircIn(double time) => 1 - MathD.Sqrt(1 - MathD.Pow(time, 2));
    static double CircOut(double time) => MathD.Sqrt(1 - MathD.Pow(time - 1, 2));
    static double CircInOut(double time) => time < 0.5 ? (1 - MathD.Sqrt(1 - MathD.Pow(2 * time, 2))) / 2 : (MathD.Sqrt(1 - MathD.Pow(-2 * time + 2, 2)) + 1) / 2;

    static double BackIn(double time) => ConstantC * time * time * time - ConstantA * time * time;
    static double BackOut(double time) => 1.0 + ConstantC * MathD.Pow(time - 1, 3) + ConstantA * MathD.Pow(time - 1, 2);
    static double BackInOut(double time) => time < 0.5 ?
        MathD.Pow(2 * time, 2) * ((ConstantB + 1) * 2 * time - ConstantB) / 2 :
        (MathD.Pow(2 * time - 2, 2) * ((ConstantB + 1) * (time * 2 - 2) + ConstantB) + 2) / 2;

    static double ElasticIn(double time) => time == 0 ? 0 : MathD.ApproximatelyEquals(time, 1) ? 1 : -MathD.Pow(2, 10 * time - 10) * MathD.Sin((time * 10.0 - 10.75) * ConstantD);
    static double ElasticOut(double time) => time == 0 ? 0 : MathD.ApproximatelyEquals(time, 1) ? 1 : MathD.Pow(2, -10 * time) * MathD.Sin((time * 10 - 0.75) * ConstantD) + 1;
    static double ElasticInOut(double time) => time == 0 ? 0 : MathD.ApproximatelyEquals(time, 1) ? 1 : time < 0.5 ? -(MathD.Pow(2, 20 * time - 10) * MathD.Sin((20 * time - 11.125) * ConstantE)) / 2 : MathD.Pow(2, -20 * time + 10) * MathD.Sin((20 * time - 11.125) * ConstantE) / 2 + 1;

    static double BounceIn(double t) => 1 - BounceOut(1 - t);
    static double BounceOut(double t)
    {
        double div = 2.75f;
        double mult = 7.5625f;

        if (t < 1 / div)
        {
            return mult * t * t;
        }
        else if (t < 2 / div)
        {
            t -= 1.5f / div;
            return mult * t * t + 0.75f;
        }
        else if (t < 2.5 / div)
        {
            t -= 2.25f / div;
            return mult * t * t + 0.9375f;
        }
        else
        {
            t -= 2.625f / div;
            return mult * t * t + 0.984375f;
        }
    }
    static double BounceInOut(double t)
    {
        if (t < 0.5) return BounceIn(t * 2) / 2;
        return 1 - BounceIn((1 - t) * 2) / 2;
    }

    #endregion
}
