using Prowl.Vector;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Provides a collection of easing functions for animations and transitions.
    /// Each function takes a normalized time value (0 to 1) and returns a transformed value (0 to 1).
    /// </summary>
    public static class Easing
    {
        #region Linear

        /// <summary>
        /// Linear interpolation with no easing.
        /// </summary>
        /// <param name="t">Normalized time (0 to 1)</param>
        /// <returns>Linear interpolated value</returns>
        public static float Linear(float t) => t;

        #endregion

        #region Quadratic (Power of 2)

        /// <summary>
        /// Quadratic ease-in: Accelerates from zero velocity.
        /// </summary>
        public static float EaseIn(float t) => t * t;

        /// <summary>
        /// Quadratic ease-out: Decelerates to zero velocity.
        /// </summary>
        public static float EaseOut(float t) => 1 - Maths.Pow(1 - t, 2f);

        /// <summary>
        /// Quadratic ease-in-out: Accelerates until halfway, then decelerates.
        /// </summary>
        public static float EaseInOut(float t) => t < 0.5f ? 2 * t * t : 1 - Maths.Pow(-2 * t + 2, 2) / 2;

        #endregion

        #region Cubic (Power of 3)

        /// <summary>
        /// Cubic ease-in: More pronounced acceleration from zero velocity.
        /// </summary>
        public static float CubicIn(float t) => t * t * t;

        /// <summary>
        /// Cubic ease-out: More pronounced deceleration to zero velocity.
        /// </summary>
        public static float CubicOut(float t) => 1 - Maths.Pow(1 - t, 3);

        /// <summary>
        /// Cubic ease-in-out: Stronger acceleration until halfway, then stronger deceleration.
        /// </summary>
        public static float CubicInOut(float t) => t < 0.5f ? 4 * t * t * t : 1 - Maths.Pow(-2 * t + 2, 3) / 2;

        #endregion

        #region Quartic (Power of 4)

        /// <summary>
        /// Quartic ease-in: Very pronounced acceleration from zero velocity.
        /// </summary>
        public static float QuartIn(float t) => t * t * t * t;

        /// <summary>
        /// Quartic ease-out: Very pronounced deceleration to zero velocity.
        /// </summary>
        public static float QuartOut(float t) => 1 - Maths.Pow(1 - t, 4);

        /// <summary>
        /// Quartic ease-in-out: Dramatic acceleration until halfway, then dramatic deceleration.
        /// </summary>
        public static float QuartInOut(float t) => t < 0.5f ? 8 * t * t * t * t : 1 - Maths.Pow(-2 * t + 2, 4) / 2;

        #endregion

        #region Quintic (Power of 5)

        /// <summary>
        /// Quintic ease-in: Extremely pronounced acceleration from zero velocity.
        /// </summary>
        public static float QuintIn(float t) => t * t * t * t * t;

        /// <summary>
        /// Quintic ease-out: Extremely pronounced deceleration to zero velocity.
        /// </summary>
        public static float QuintOut(float t) => 1 - Maths.Pow(1 - t, 5);

        /// <summary>
        /// Quintic ease-in-out: Extreme acceleration until halfway, then extreme deceleration.
        /// </summary>
        public static float QuintInOut(float t) => t < 0.5f ? 16 * t * t * t * t * t : 1 - Maths.Pow(-2 * t + 2, 5) / 2;

        #endregion

        #region Sinusoidal

        /// <summary>
        /// Sinusoidal ease-in: Gradual acceleration using a sine curve.
        /// </summary>
        public static float SineIn(float t) => 1 - Maths.Cos((t * Maths.PI) / 2);

        /// <summary>
        /// Sinusoidal ease-out: Gradual deceleration using a sine curve.
        /// </summary>
        public static float SineOut(float t) => Maths.Sin((t * Maths.PI) / 2);

        /// <summary>
        /// Sinusoidal ease-in-out: Gentle acceleration and deceleration based on a sine curve.
        /// </summary>
        public static float SineInOut(float t) => -(Maths.Cos(Maths.PI * t) - 1) / 2;

        #endregion

        #region Exponential

        /// <summary>
        /// Exponential ease-in: Acceleration with an exponential growth curve.
        /// </summary>
        public static float ExpoIn(float t) => t == 0f ? 0f : Maths.Pow(2, 10 * t - 10);

        /// <summary>
        /// Exponential ease-out: Deceleration with an exponential decay curve.
        /// </summary>
        public static float ExpoOut(float t) => t == 1f ? 1f : 1 - Maths.Pow(2, -10 * t);

        /// <summary>
        /// Exponential ease-in-out: Exponential acceleration until halfway, then exponential deceleration.
        /// </summary>
        public static float ExpoInOut(float t) => t == 0f ? 0f : t == 1f ? 1f : t < 0.5f ?
                                                 Maths.Pow(2, 20 * t - 10) / 2 : (2 - Maths.Pow(2, -20 * t + 10)) / 2;

        #endregion

        #region Circular

        /// <summary>
        /// Circular ease-in: Acceleration following a quarter-circle curve.
        /// </summary>
        public static float CircIn(float t) => 1 - Maths.Sqrt(1 - Maths.Pow(t, 2));

        /// <summary>
        /// Circular ease-out: Deceleration following a quarter-circle curve.
        /// </summary>
        public static float CircOut(float t) => Maths.Sqrt(1 - Maths.Pow(t - 1, 2));

        /// <summary>
        /// Circular ease-in-out: Acceleration and deceleration following a semi-circle curve.
        /// </summary>
        public static float CircInOut(float t) => t < 0.5f ?
                                                 (1 - Maths.Sqrt(1 - Maths.Pow(2 * t, 2))) / 2 :
                                                 (Maths.Sqrt(1 - Maths.Pow(-2 * t + 2, 2)) + 1) / 2;

        #endregion

        #region Back

        /// <summary>
        /// Back ease-in: Slight overshoot backward before accelerating forward.
        /// </summary>
        public static float BackIn(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1;
            return c3 * t * t * t - c1 * t * t;
        }

        /// <summary>
        /// Back ease-out: Acceleration followed by a slight overshoot beyond the final position.
        /// </summary>
        public static float BackOut(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1;
            return 1 + c3 * Maths.Pow(t - 1, 3) + c1 * Maths.Pow(t - 1, 2);
        }

        /// <summary>
        /// Back ease-in-out: Slight overshoot in both directions.
        /// </summary>
        public static float BackInOut(float t)
        {
            const float c1 = 1.70158f;
            const float c2 = c1 * 1.525f;
            return t < 0.5f ?
                   (Maths.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2)) / 2 :
                   (Maths.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
        }

        #endregion

        #region Elastic

        /// <summary>
        /// Elastic ease-in: Begins slowly and then accelerates with a spring-like effect.
        /// </summary>
        public static float ElasticIn(float t)
        {
            const float c4 = (2 * Maths.PI) / 3;
            return t == 0f ? 0f : t == 1f ? 1f :
                   -Maths.Pow(2, 10 * t - 10) * Maths.Sin((t * 10 - 10.75f) * c4);
        }

        /// <summary>
        /// Elastic ease-out: Overshoots the destination and then oscillates to the final position.
        /// </summary>
        public static float ElasticOut(float t)
        {
            const float c4 = (2 * Maths.PI) / 3;
            return t == 0f ? 0f : t == 1f ? 1f :
                   Maths.Pow(2, -10 * t) * Maths.Sin((t * 10 - 0.75f) * c4) + 1;
        }

        /// <summary>
        /// Elastic ease-in-out: Oscillating effect at both the beginning and the end.
        /// </summary>
        public static float ElasticInOut(float t)
        {
            const float c5 = (2 * Maths.PI) / 4.5f;
            return t == 0f ? 0f : t == 1f ? 1f : t < 0.5f ?
                   -(Maths.Pow(2, 20 * t - 10) * Maths.Sin((20 * t - 11.125f) * c5)) / 2 :
                   (Maths.Pow(2, -20 * t + 10) * Maths.Sin((20 * t - 11.125f) * c5)) / 2 + 1;
        }

        #endregion

        #region Bounce

        /// <summary>
        /// Bounce ease-out: Bounces multiple times near the destination before settling.
        /// </summary>
        public static float BounceOut(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1 / d1)
                return n1 * t * t;
            else if (t < 2 / d1)
                return n1 * (t -= 1.5f / d1) * t + 0.75f;
            else if (t < 2.5 / d1)
                return n1 * (t -= 2.25f / d1) * t + 0.9375f;
            else
                return n1 * (t -= 2.625f / d1) * t + 0.984375f;
        }

        /// <summary>
        /// Bounce ease-in: Bounces multiple times at the start before accelerating.
        /// </summary>
        public static float BounceIn(float t) => 1 - BounceOut(1 - t);

        /// <summary>
        /// Bounce ease-in-out: Bounces at both the beginning and end of the animation.
        /// </summary>
        public static float BounceInOut(float t) => t < 0.5f ?
                                                   (1 - BounceOut(1 - 2 * t)) / 2 :
                                                   (1 + BounceOut(2 * t - 1)) / 2;

        #endregion

        #region Additional Useful Functions

        /// <summary>
        /// Steps instantly from 0 to 1 at the midpoint.
        /// Useful for binary state transitions.
        /// </summary>
        public static float Step(float t) => t < 0.5f ? 0f : 1f;

        /// <summary>
        /// Smoothstep: Smooth Hermite interpolation.
        /// Provides smoother transition than EaseInOut with minimal computation.
        /// </summary>
        public static float SmoothStep(float t)
        {
            // Clamp between 0 and 1
            t = Maths.Max(0f, Maths.Min(1f, t));
            // Evaluate polynomial
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Smootherstep: Even smoother Hermite interpolation.
        /// Higher degree polynomial for more continuous derivatives.
        /// </summary>
        public static float SmootherStep(float t)
        {
            // Clamp between 0 and 1
            t = Maths.Max(0f, Maths.Min(1f, t));
            // Evaluate higher degree polynomial
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>
        /// Spring function with configurable elasticity.
        /// Returns values that can exceed the 0-1 range during oscillation.
        /// </summary>
        /// <param name="t">Normalized time (0 to 1)</param>
        /// <param name="dampingRatio">Controls oscillation damping (0.1 = lots of oscillation, 1.0 = no oscillation)</param>
        /// <param name="angularFrequency">Controls speed of oscillation (default = 20)</param>
        public static float Spring(float t, float dampingRatio = 0.5f, float angularFrequency = 20.0f)
        {
            // Clamp to avoid issues
            dampingRatio = Maths.Max(0.0001f, dampingRatio);

            // Don't calculate for extremes
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            if (dampingRatio < 1.0f) // Under-damped
            {
                // Calculate for oscillation
                float envelope = Maths.Exp(-dampingRatio * angularFrequency * t);
                float exponent = angularFrequency * Maths.Sqrt(1.0f - dampingRatio * dampingRatio) * t;
                return 1.0f - envelope * Maths.Cos(exponent);
            }
            else // Critically damped (no oscillation)
            {
                float envelope = Maths.Exp(-angularFrequency * t);
                return 1.0f - envelope * (1.0f + angularFrequency * t);
            }
        }

        #endregion
    }
}
