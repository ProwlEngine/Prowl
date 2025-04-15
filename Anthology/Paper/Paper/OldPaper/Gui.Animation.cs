// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

/*

namespace Prowl.PaperUI.GUI;

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

public partial class Paper
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

    private ulong GetNextID() => (ulong)HashCode.Combine(CurrentNode.ID, CurrentNode.GetNextAnimation());

    private void UpdateAnimations(float dt)
    {
        foreach (var storageID in _boolAnimations.Keys)
        {
            BoolAnimation anim = _boolAnimations[storageID];
            float speed = 1.0f / anim.Duration;
            anim.ElapsedTime = MathUtilities.MoveTowards(anim.ElapsedTime, anim.CurrentValue ? 1 : 0, dt * speed);
            _boolAnimations[storageID] = anim;
        }
    }

    private struct BoolAnimation
    {
        public bool CurrentValue;
        public EaseType EaseType;
        public float Duration;
        public float ElapsedTime;
    }

    #region Easing

    static float GetEase(float time, EaseType type)
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

    const float ConstantA = 1.70158F;
    const float ConstantB = ConstantA * 1.525F;
    const float ConstantC = ConstantA + 1.0F;
    const float ConstantD = 2.0F * MathF.PI / 3.0F;
    const float ConstantE = 2.0F * MathF.PI / 4.5F;
    const float ConstantF = 7.5625F;
    const float ConstantG = 2.75F;

    static float Linear(float time) => time;

    static float SineIn(float time) => 1.0f - MathF.Cos((time * MathF.PI) / 2.0f);
    static float SineOut(float time) => MathF.Sin((time * MathF.PI) / 2.0f);
    static float SineInOut(float time) => -(MathF.Cos(MathF.PI * time) - 1.0f) / 2.0f;

    static float QuadIn(float time) => time * time;
    static float QuadOut(float time) => 1 - (1 - time) * (1 - time);
    static float QuadInOut(float time) => time < 0.5 ? 2 * time * time : 1 - MathF.Pow(-2 * time + 2, 2) / 2;

    static float CubicIn(float time) => time * time * time;
    static float CubicOut(float time) => 1 - MathF.Pow(1 - time, 3);
    static float CubicInOut(float time) => time < 0.5 ? 4 * time * time * time : 1 - MathF.Pow(-2 * time + 2, 3) / 2;

    static float QuartIn(float time) => time * time * time * time;
    static float QuartOut(float time) => 1 - MathF.Pow(1 - time, 4);
    static float QuartInOut(float time) => time < 0.5 ? 8 * time * time * time * time : 1 - MathF.Pow(-2 * time + 2, 4) / 2;

    static float QuintIn(float time) => time * time * time * time * time;
    static float QuintOut(float time) => 1 - MathF.Pow(1 - time, 5);
    static float QuintInOut(float time) => time < 0.5 ? 16 * time * time * time * time * time : 1 - MathF.Pow(-2 * time + 2, 5) / 2;

    static float ExpoIn(float time) => time == 0 ? 0 : MathF.Pow(2, 10 * time - 10);
    static float ExpoOut(float time) => MathUtilities.ApproximatelyEquals(time, 1) ? 1 : 1 - MathF.Pow(2, -10 * time);
    static float ExpoInOut(float time) => time == 0 ? 0 : MathUtilities.ApproximatelyEquals(time, 1) ? 1 : time < 0.5 ? MathF.Pow(2, 20 * time - 10) / 2 : (2 - MathF.Pow(2, -20 * time + 10)) / 2;

    static float CircIn(float time) => 1 - MathF.Sqrt(1 - MathF.Pow(time, 2));
    static float CircOut(float time) => MathF.Sqrt(1 - MathF.Pow(time - 1, 2));
    static float CircInOut(float time) => time < 0.5 ? (1 - MathF.Sqrt(1 - MathF.Pow(2 * time, 2))) / 2 : (MathF.Sqrt(1 - MathF.Pow(-2 * time + 2, 2)) + 1) / 2;

    static float BackIn(float time) => ConstantC * time * time * time - ConstantA * time * time;
    static float BackOut(float time) => 1.0f + ConstantC * MathF.Pow(time - 1, 3) + ConstantA * MathF.Pow(time - 1, 2);
    static float BackInOut(float time) => time < 0.5 ?
        MathF.Pow(2 * time, 2) * ((ConstantB + 1) * 2 * time - ConstantB) / 2 :
        (MathF.Pow(2 * time - 2, 2) * ((ConstantB + 1) * (time * 2 - 2) + ConstantB) + 2) / 2;

    static float ElasticIn(float time) => time == 0 ? 0 : MathUtilities.ApproximatelyEquals(time, 1) ? 1 : -MathF.Pow(2, 10 * time - 10) * MathF.Sin((time * 10.0f - 10.75f) * ConstantD);
    static float ElasticOut(float time) => time == 0 ? 0 : MathUtilities.ApproximatelyEquals(time, 1) ? 1 : MathF.Pow(2, -10 * time) * MathF.Sin((time * 10f - 0.75f) * ConstantD) + 1;
    static float ElasticInOut(float time) => time == 0 ? 0 : MathUtilities.ApproximatelyEquals(time, 1) ? 1 : time < 0.5 ? -(MathF.Pow(2, 20 * time - 10) * MathF.Sin((20f * time - 11.125f) * ConstantE)) / 2 : MathF.Pow(2, -20 * time + 10) * MathF.Sin((20f * time - 11.125f) * ConstantE) / 2 + 1;

    static float BounceIn(float t) => 1 - BounceOut(1 - t);
    static float BounceOut(float t)
    {
        float div = 2.75f;
        float mult = 7.5625f;

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
    static float BounceInOut(float t)
    {
        if (t < 0.5) return BounceIn(t * 2) / 2;
        return 1 - BounceIn((1 - t) * 2) / 2;
    }

    #endregion
}


*/