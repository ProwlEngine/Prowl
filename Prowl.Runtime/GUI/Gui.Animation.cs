using System.Collections.Generic;

namespace Prowl.Runtime.GUI
{
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
        private Dictionary<ulong, BoolAnimation> _boolAnimations = [];

        public float AnimateBool(bool state, float durationIn, float durationOut, EaseType easeIn, EaseType easeOut) => AnimateBool(GetNextID(), state, state ? durationOut : durationIn, state ? easeOut : easeIn);
        public float AnimateBool(bool state, float durationIn, float durationOut, EaseType type) => AnimateBool(GetNextID(), state, state ? durationOut : durationIn, type);
        public float AnimateBool(bool state, float duration, EaseType easeIn, EaseType easeOut) => AnimateBool(GetNextID(), state, duration, state ? easeOut : easeIn);
        public float AnimateBool(bool state, float duration, EaseType ease) => AnimateBool(GetNextID(), state, duration, ease);
        public float AnimateBool(ulong animId, bool state, float duration, EaseType type)
        {
            BoolAnimation anim;
            if(_boolAnimations.TryGetValue(animId, out anim))
            {
                anim.CurrentValue = state;
                anim.Duration = duration;
                anim.EaseType = type;
            }
            else
            {
                anim = new BoolAnimation {
                    CurrentValue = state,
                    Duration = duration,
                    EaseType = type,
                    ElapsedTime = state ? 1 : 0
                };
            }
            _boolAnimations[animId] = anim;

            return (float)GetEase(anim.ElapsedTime, anim.EaseType);
        }

        private ulong GetNextID()
        {
            ulong animId = 17;
            animId = animId * 23 + (ulong)CurrentNode.GetNextAnimation();
            return animId * 23 + CurrentNode.ID;
        }

        private void UpdateAnimations(double dt)
        {
            foreach (var storageID in _boolAnimations.Keys)
            {
                BoolAnimation anim = _boolAnimations[storageID];
                double speed = 1.0 / anim.Duration;
                anim.ElapsedTime = Mathf.MoveTowards(anim.ElapsedTime, anim.CurrentValue ? 1 : 0, dt * speed);
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
            return type switch {
                EaseType.Linear => Linear(time),
                EaseType.SineIn => SineIn(time),
                EaseType.SineOut => SineOut(time),
                EaseType.SineInOut => SineInOut(time),
                EaseType.QuadIn => QuadIn(time),
                EaseType.QuadOut => QuadOut(time),
                EaseType.QuadInOut => QuadInOut(time),
                EaseType.CubicIn => CubicIn(time),
                EaseType.CubicOut => CubicOut(time),
                EaseType.CubicInOut => CubicInOut(time),
                EaseType.QuartIn => QuartIn(time),
                EaseType.QuartOut => QuartOut(time),
                EaseType.QuartInOut => QuartInOut(time),
                EaseType.QuintIn => QuintIn(time),
                EaseType.QuintOut => QuintOut(time),
                EaseType.QuintInOut => QuintInOut(time),
                EaseType.ExpoIn => ExpoIn(time),
                EaseType.ExpoOut => ExpoOut(time),
                EaseType.ExpoInOut => ExpoInOut(time),
                EaseType.CircIn => CircIn(time),
                EaseType.CircOut => CircOut(time),
                EaseType.CircInOut => CircInOut(time),
                EaseType.BackIn => BackIn(time),
                EaseType.BackOut => BackOut(time),
                EaseType.BackInOut => BackInOut(time),
                EaseType.ElasticIn => ElasticIn(time),
                EaseType.ElasticOut => ElasticOut(time),
                EaseType.ElasticInOut => ElasticInOut(time),
                EaseType.BounceIn => BounceIn(time),
                EaseType.BounceOut => BounceOut(time),
                EaseType.BounceInOut => BounceInOut(time),
                _ => Linear(time),
            };
        }

        const double ConstantA = 1.70158;
        const double ConstantB = ConstantA * 1.525;
        const double ConstantC = ConstantA + 1.0;
        const double ConstantD = 2.0 * Mathf.PI / 3.0;
        const double ConstantE = 2.0 * Mathf.PI / 4.5;
        const double ConstantF = 7.5625;
        const double ConstantG = 2.75;

        static double Linear(double time) => time;

        static double SineIn(double time) => 1.0 - Mathf.Cos((time * Mathf.PI) / 2.0);
        static double SineOut(double time) => Mathf.Sin((time * Mathf.PI) / 2.0);
        static double SineInOut(double time) => -(Mathf.Cos(Mathf.PI * time) - 1.0) / 2.0;

        static double QuadIn(double time) => time * time;
        static double QuadOut(double time) => 1 - (1 - time) * (1 - time);
        static double QuadInOut(double time) => time < 0.5 ? 2 * time * time : 1 - Mathf.Pow(-2 * time + 2, 2) / 2;

        static double CubicIn(double time) => time * time * time;
        static double CubicOut(double time) => 1 - Mathf.Pow(1 - time, 3);
        static double CubicInOut(double time) => time < 0.5 ? 4 * time * time * time : 1 - Mathf.Pow(-2 * time + 2, 3) / 2;

        static double QuartIn(double time) => time * time * time * time;
        static double QuartOut(double time) => 1 - Mathf.Pow(1 - time, 4);
        static double QuartInOut(double time) => time < 0.5 ? 8 * time * time * time * time : 1 - Mathf.Pow(-2 * time + 2, 4) / 2;

        static double QuintIn(double time) => time * time * time * time * time;
        static double QuintOut(double time) => 1 - Mathf.Pow(1 - time, 5);
        static double QuintInOut(double time) => time < 0.5 ? 16 * time * time * time * time * time : 1 - Mathf.Pow(-2 * time + 2, 5) / 2;

        static double ExpoIn(double time) => time == 0 ? 0 : Mathf.Pow(2, 10 * time - 10);
        static double ExpoOut(double time) => time == 1 ? 1 : 1 - Mathf.Pow(2, -10 * time);
        static double ExpoInOut(double time) => time == 0 ? 0 : time == 1 ? 1 : time < 0.5 ? Mathf.Pow(2, 20 * time - 10) / 2 : (2 - Mathf.Pow(2, -20 * time + 10)) / 2;

        static double CircIn(double time) => 1 - Mathf.Sqrt(1 - Mathf.Pow(time, 2));
        static double CircOut(double time) => Mathf.Sqrt(1 - Mathf.Pow(time - 1, 2));
        static double CircInOut(double time) => time < 0.5 ? (1 - Mathf.Sqrt(1 - Mathf.Pow(2 * time, 2))) / 2 : (Mathf.Sqrt(1 - Mathf.Pow(-2 * time + 2, 2)) + 1) / 2;

        static double BackIn(double time) => ConstantC * time * time * time - ConstantA * time * time;
        static double BackOut(double time) => 1.0 + ConstantC * Mathf.Pow(time - 1, 3) + ConstantA * Mathf.Pow(time - 1, 2);
        static double BackInOut(double time) => time < 0.5 ?
              Mathf.Pow(2 * time, 2) * ((ConstantB + 1) * 2 * time - ConstantB) / 2 :
              (Mathf.Pow(2 * time - 2, 2) * ((ConstantB + 1) * (time * 2 - 2) + ConstantB) + 2) / 2;

        static double ElasticIn(double time) => time == 0 ? 0 : time == 1 ? 1 : -Mathf.Pow(2, 10 * time - 10) * Mathf.Sin((time * 10.0 - 10.75) * ConstantD);
        static double ElasticOut(double time) => time == 0 ? 0 : time == 1 ? 1 : Mathf.Pow(2, -10 * time) * Mathf.Sin((time * 10 - 0.75) * ConstantD) + 1;
        static double ElasticInOut(double time) => time == 0 ? 0 : time == 1 ? 1 : time < 0.5 ? -(Mathf.Pow(2, 20 * time - 10) * Mathf.Sin((20 * time - 11.125) * ConstantE)) / 2 : Mathf.Pow(2, -20 * time + 10) * Mathf.Sin((20 * time - 11.125) * ConstantE) / 2 + 1;

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
}