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
        public static double Linear(double t) => t;

        #endregion

        #region Quadratic (Power of 2)

        /// <summary>
        /// Quadratic ease-in: Accelerates from zero velocity.
        /// </summary>
        public static double EaseIn(double t) => t * t;

        /// <summary>
        /// Quadratic ease-out: Decelerates to zero velocity.
        /// </summary>
        public static double EaseOut(double t) => 1 - Math.Pow(1 - t, 2);

        /// <summary>
        /// Quadratic ease-in-out: Accelerates until halfway, then decelerates.
        /// </summary>
        public static double EaseInOut(double t) => t < 0.5f ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;

        #endregion

        #region Cubic (Power of 3)

        /// <summary>
        /// Cubic ease-in: More pronounced acceleration from zero velocity.
        /// </summary>
        public static double CubicIn(double t) => t * t * t;

        /// <summary>
        /// Cubic ease-out: More pronounced deceleration to zero velocity.
        /// </summary>
        public static double CubicOut(double t) => 1 - Math.Pow(1 - t, 3);

        /// <summary>
        /// Cubic ease-in-out: Stronger acceleration until halfway, then stronger deceleration.
        /// </summary>
        public static double CubicInOut(double t) => t < 0.5f ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

        #endregion

        #region Quartic (Power of 4)

        /// <summary>
        /// Quartic ease-in: Very pronounced acceleration from zero velocity.
        /// </summary>
        public static double QuartIn(double t) => t * t * t * t;

        /// <summary>
        /// Quartic ease-out: Very pronounced deceleration to zero velocity.
        /// </summary>
        public static double QuartOut(double t) => 1 - Math.Pow(1 - t, 4);

        /// <summary>
        /// Quartic ease-in-out: Dramatic acceleration until halfway, then dramatic deceleration.
        /// </summary>
        public static double QuartInOut(double t) => t < 0.5f ? 8 * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 4) / 2;

        #endregion

        #region Quintic (Power of 5)

        /// <summary>
        /// Quintic ease-in: Extremely pronounced acceleration from zero velocity.
        /// </summary>
        public static double QuintIn(double t) => t * t * t * t * t;

        /// <summary>
        /// Quintic ease-out: Extremely pronounced deceleration to zero velocity.
        /// </summary>
        public static double QuintOut(double t) => 1 - Math.Pow(1 - t, 5);

        /// <summary>
        /// Quintic ease-in-out: Extreme acceleration until halfway, then extreme deceleration.
        /// </summary>
        public static double QuintInOut(double t) => t < 0.5f ? 16 * t * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 5) / 2;

        #endregion

        #region Sinusoidal

        /// <summary>
        /// Sinusoidal ease-in: Gradual acceleration using a sine curve.
        /// </summary>
        public static double SineIn(double t) => 1 - Math.Cos((t * Math.PI) / 2);

        /// <summary>
        /// Sinusoidal ease-out: Gradual deceleration using a sine curve.
        /// </summary>
        public static double SineOut(double t) => Math.Sin((t * Math.PI) / 2);

        /// <summary>
        /// Sinusoidal ease-in-out: Gentle acceleration and deceleration based on a sine curve.
        /// </summary>
        public static double SineInOut(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;

        #endregion

        #region Exponential

        /// <summary>
        /// Exponential ease-in: Acceleration with an exponential growth curve.
        /// </summary>
        public static double ExpoIn(double t) => t == 0f ? 0f : Math.Pow(2, 10 * t - 10);

        /// <summary>
        /// Exponential ease-out: Deceleration with an exponential decay curve.
        /// </summary>
        public static double ExpoOut(double t) => t == 1f ? 1f : 1 - Math.Pow(2, -10 * t);

        /// <summary>
        /// Exponential ease-in-out: Exponential acceleration until halfway, then exponential deceleration.
        /// </summary>
        public static double ExpoInOut(double t) => t == 0f ? 0f : t == 1f ? 1f : t < 0.5f ?
                                                 Math.Pow(2, 20 * t - 10) / 2 : (2 - Math.Pow(2, -20 * t + 10)) / 2;

        #endregion

        #region Circular

        /// <summary>
        /// Circular ease-in: Acceleration following a quarter-circle curve.
        /// </summary>
        public static double CircIn(double t) => 1 - Math.Sqrt(1 - Math.Pow(t, 2));

        /// <summary>
        /// Circular ease-out: Deceleration following a quarter-circle curve.
        /// </summary>
        public static double CircOut(double t) => Math.Sqrt(1 - Math.Pow(t - 1, 2));

        /// <summary>
        /// Circular ease-in-out: Acceleration and deceleration following a semi-circle curve.
        /// </summary>
        public static double CircInOut(double t) => t < 0.5f ?
                                                 (1 - Math.Sqrt(1 - Math.Pow(2 * t, 2))) / 2 :
                                                 (Math.Sqrt(1 - Math.Pow(-2 * t + 2, 2)) + 1) / 2;

        #endregion

        #region Back

        /// <summary>
        /// Back ease-in: Slight overshoot backward before accelerating forward.
        /// </summary>
        public static double BackIn(double t)
        {
            const double c1 = 1.70158f;
            const double c3 = c1 + 1;
            return c3 * t * t * t - c1 * t * t;
        }

        /// <summary>
        /// Back ease-out: Acceleration followed by a slight overshoot beyond the final position.
        /// </summary>
        public static double BackOut(double t)
        {
            const double c1 = 1.70158f;
            const double c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        }

        /// <summary>
        /// Back ease-in-out: Slight overshoot in both directions.
        /// </summary>
        public static double BackInOut(double t)
        {
            const double c1 = 1.70158f;
            const double c2 = c1 * 1.525f;
            return t < 0.5f ?
                   (Math.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2)) / 2 :
                   (Math.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
        }

        #endregion

        #region Elastic

        /// <summary>
        /// Elastic ease-in: Begins slowly and then accelerates with a spring-like effect.
        /// </summary>
        public static double ElasticIn(double t)
        {
            const double c4 = (2 * Math.PI) / 3;
            return t == 0f ? 0f : t == 1f ? 1f :
                   -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75f) * c4);
        }

        /// <summary>
        /// Elastic ease-out: Overshoots the destination and then oscillates to the final position.
        /// </summary>
        public static double ElasticOut(double t)
        {
            const double c4 = (2 * Math.PI) / 3;
            return t == 0f ? 0f : t == 1f ? 1f :
                   Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75f) * c4) + 1;
        }

        /// <summary>
        /// Elastic ease-in-out: Oscillating effect at both the beginning and the end.
        /// </summary>
        public static double ElasticInOut(double t)
        {
            const double c5 = (2 * Math.PI) / 4.5f;
            return t == 0f ? 0f : t == 1f ? 1f : t < 0.5f ?
                   -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125f) * c5)) / 2 :
                   (Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125f) * c5)) / 2 + 1;
        }

        #endregion

        #region Bounce

        /// <summary>
        /// Bounce ease-out: Bounces multiple times near the destination before settling.
        /// </summary>
        public static double BounceOut(double t)
        {
            const double n1 = 7.5625f;
            const double d1 = 2.75f;

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
        public static double BounceIn(double t) => 1 - BounceOut(1 - t);

        /// <summary>
        /// Bounce ease-in-out: Bounces at both the beginning and end of the animation.
        /// </summary>
        public static double BounceInOut(double t) => t < 0.5f ?
                                                   (1 - BounceOut(1 - 2 * t)) / 2 :
                                                   (1 + BounceOut(2 * t - 1)) / 2;

        #endregion

        #region Additional Useful Functions

        /// <summary>
        /// Steps instantly from 0 to 1 at the midpoint.
        /// Useful for binary state transitions.
        /// </summary>
        public static double Step(double t) => t < 0.5f ? 0f : 1f;

        /// <summary>
        /// Smoothstep: Smooth Hermite interpolation.
        /// Provides smoother transition than EaseInOut with minimal computation.
        /// </summary>
        public static double SmoothStep(double t)
        {
            // Clamp between 0 and 1
            t = Math.Max(0f, Math.Min(1f, t));
            // Evaluate polynomial
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Smootherstep: Even smoother Hermite interpolation.
        /// Higher degree polynomial for more continuous derivatives.
        /// </summary>
        public static double SmootherStep(double t)
        {
            // Clamp between 0 and 1
            t = Math.Max(0f, Math.Min(1f, t));
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
        public static double Spring(double t, double dampingRatio = 0.5f, double angularFrequency = 20.0f)
        {
            // Clamp to avoid issues
            dampingRatio = Math.Max(0.0001f, dampingRatio);

            // Don't calculate for extremes
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            if (dampingRatio < 1.0f) // Under-damped
            {
                // Calculate for oscillation
                double envelope = Math.Exp(-dampingRatio * angularFrequency * t);
                double exponent = angularFrequency * Math.Sqrt(1.0f - dampingRatio * dampingRatio) * t;
                return 1.0f - envelope * Math.Cos(exponent);
            }
            else // Critically damped (no oscillation)
            {
                double envelope = Math.Exp(-angularFrequency * t);
                return 1.0f - envelope * (1.0f + angularFrequency * t);
            }
        }

        #endregion
    }
}
