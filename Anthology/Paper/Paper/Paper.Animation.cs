using System;
using System.Runtime.CompilerServices;
using Prowl.Vector;

namespace Prowl.PaperUI
{
    public partial class Paper
    {
        #region Animation Primitives

        // Storage key prefixes are short on purpose > one hashtable lookup per call adds up.
        private const string AnimateBoolKey = "_pp_ab_";
        private const string AnimateFloatKey = "_pp_af_";
        private const string AnimateSpringKey = "_pp_as_";
        private const string OneShotKey = "_pp_os_";
        private const string AnimateColorKey = "_pp_ac_";
        private const string AnimateVec2Key = "_pp_av2_";
        private const string AnimateAngleKey = "_pp_an_";
        private const string StableForKey = "_pp_sf_";
        private const string ShakeKey = "_pp_sh_";

        /// <summary>
        /// Smoothly animates a 0..1 progress value toward 1 when <paramref name="target"/> is true and
        /// toward 0 when it is false, returning the eased value. State is stored on
        /// <see cref="CurrentParent"/>; if that element isn't recreated next frame, the storage is wiped
        /// automatically (so the animation resets when the element reappears).
        /// <para>
        /// Each call site gets its own slot via <see cref="CallerLineNumberAttribute"/>, so you can stack
        /// independent animations on a single element:
        /// <code>
        /// float hoverT   = gui.AnimateBool(IsHovered, 0.12f, Easing.SineInOut);
        /// float pressedT = gui.AnimateBool(IsActive,  0.06f);
        /// </code>
        /// If you call <c>AnimateBool</c> from inside a loop or a wrapper method that hides the call site,
        /// pass an explicit <paramref name="id"/> to keep slots distinct.
        /// </para>
        /// </summary>
        /// <param name="target">Direction to animate toward (true -> 1, false -> 0).</param>
        /// <param name="duration">Seconds to traverse 0 -> 1 (or 1 -> 0). 0 snaps instantly.</param>
        /// <param name="easing">Optional easing applied on read; null = linear. The underlying progress is
        /// stored linear, so reversing target produces a smooth reversal regardless of easing shape.</param>
        /// <param name="id">Optional explicit slot id for use inside loops or wrapper helpers.</param>
        /// <returns>Eased 0..1 progress.</returns>
        public float AnimateBool(
            bool target,
            float duration = 0.2f,
            Func<float, float>? easing = null,
            string? id = null,
            [CallerLineNumber] int callerLine = 0)
        {
            string key = AnimateBoolKey + (id ?? callerLine.ToString());
            float dir = target ? 1f : 0f;
            float progress = GetElementStorage<float>(key, dir);

            if (duration <= 0f)
            {
                progress = dir;
            }
            else if (DeltaTime > 0f)
            {
                float step = DeltaTime / duration;
                if (progress < dir) progress = Maths.Min(dir, progress + step);
                else if (progress > dir) progress = Maths.Max(dir, progress - step);
            }

            SetElementStorage(key, progress);
            return easing?.Invoke(progress) ?? progress;
        }

        /// <summary>
        /// Smoothly chases a continuously-changing numeric <paramref name="target"/> using frame-rate-
        /// independent exponential smoothing. Returns the current interpolated value. Higher
        /// <paramref name="speed"/> = faster catch-up (8 = ~99% within 0.5s).
        /// State is stored on <see cref="CurrentParent"/>.
        /// </summary>
        /// <remarks>
        /// Use for values that change every frame (cursor position, scroll offset, animated number readouts).
        /// For binary on/off transitions prefer <see cref="AnimateBool"/>.
        /// </remarks>
        public float AnimateFloat(
            float target,
            float speed = 8f,
            string? id = null,
            [CallerLineNumber] int callerLine = 0)
        {
            string key = AnimateFloatKey + (id ?? callerLine.ToString());
            float current = GetElementStorage<float>(key, target);
            if (DeltaTime > 0f && speed > 0f)
            {
                float t = 1f - Maths.Exp(-speed * DeltaTime);
                current += (target - current) * t;
            }
            SetElementStorage(key, current);
            return current;
        }

        /// <summary>
        /// Spring-based chase toward <paramref name="target"/>. Maintains both position and velocity, so
        /// rapid target changes produce natural overshoot and settling. Returns the current position.
        /// </summary>
        /// <param name="target">Value to converge toward.</param>
        /// <param name="frequency">Oscillation frequency in Hz (higher = stiffer / faster response).</param>
        /// <param name="damping">0 = undamped (rings forever), 1 = critically damped (no overshoot),
        /// values in between produce decaying oscillation.</param>
        public float AnimateSpring(
            float target,
            float frequency = 6f,
            float damping = 0.7f,
            string? id = null,
            [CallerLineNumber] int callerLine = 0)
        {
            string key = AnimateSpringKey + (id ?? callerLine.ToString());
            var state = GetElementStorage<(float pos, float vel)>(key, (target, 0f));

            if (DeltaTime > 0f && frequency > 0f)
            {
                float omega = frequency * Maths.PI * 2f;
                float a = -omega * omega * (state.pos - target) - 2f * damping * omega * state.vel;
                state.vel += a * DeltaTime;
                state.pos += state.vel * DeltaTime;
            }

            SetElementStorage(key, state);
            return state.pos;
        }

        /// <summary>
        /// One-shot 0..1 ramp: when <paramref name="trigger"/> goes from false to true, ramps from 0 to 1
        /// over <paramref name="duration"/> seconds and stays at 1 until released. Going false resets to 0.
        /// Useful for fire-once flash / pulse / shake effects.
        /// </summary>
        public float OneShot(
            bool trigger,
            float duration = 0.4f,
            Func<float, float>? easing = null,
            string? id = null,
            [CallerLineNumber] int callerLine = 0)
        {
            string key = OneShotKey + (id ?? callerLine.ToString());
            var state = GetElementStorage<(bool prev, float t)>(key, (false, 0f));

            if (trigger && !state.prev) state.t = 0f;          // (re)start on rising edge
            if (!trigger) state.t = 0f;                        // reset on release
            else if (duration > 0f && state.t < 1f)
                state.t = Maths.Min(1f, state.t + DeltaTime / duration);

            state.prev = trigger;
            SetElementStorage(key, state);
            return easing?.Invoke(state.t) ?? state.t;
        }

        /// <summary>
        /// A continuously-cycling 0..1 oscillator driven by <see cref="Time"/>. Returns a smooth cosine
        /// pulse with the given <paramref name="period"/> in seconds; useful for breathing highlights,
        /// blinking cursors, and similar idle effects. Stateless.
        /// </summary>
        public float Pulse(float period = 1.5f)
        {
            if (period <= 0f) return 0f;
            float phase = (Time % period) / period;
            return 0.5f - 0.5f * Maths.Cos(phase * Maths.PI * 2f);
        }

        /// <summary>
        /// Color-flavoured <see cref="AnimateFloat"/> > exponential per-channel chase toward
        /// <paramref name="target"/>. Each call site stored independently on <see cref="CurrentParent"/>.
        /// </summary>
        public Color AnimateColor(
            Color target,
            float speed = 8f,
            string? id = null,
            [CallerLineNumber] int callerLine = 0)
        {
            string key = AnimateColorKey + (id ?? callerLine.ToString());
            Color current = GetElementStorage<Color>(key, target);
            if (DeltaTime > 0f && speed > 0f)
            {
                float t = 1f - Maths.Exp(-speed * DeltaTime);
                current = new Color(
                    current.R + (target.R - current.R) * t,
                    current.G + (target.G - current.G) * t,
                    current.B + (target.B - current.B) * t,
                    current.A + (target.A - current.A) * t);
            }
            SetElementStorage(key, current);
            return current;
        }

        /// <summary>
        /// 2-D variant of <see cref="AnimateFloat"/>. Useful for chasing positions, sizes, and other
        /// vector quantities.
        /// </summary>
        public Float2 AnimateVec2(
            Float2 target,
            float speed = 8f,
            string? id = null,
            [CallerLineNumber] int callerLine = 0)
        {
            string key = AnimateVec2Key + (id ?? callerLine.ToString());
            Float2 current = GetElementStorage<Float2>(key, target);
            if (DeltaTime > 0f && speed > 0f)
            {
                float t = 1f - Maths.Exp(-speed * DeltaTime);
                current += (target - current) * t;
            }
            SetElementStorage(key, current);
            return current;
        }

        /// <summary>
        /// Angle (in degrees) variant of <see cref="AnimateFloat"/>, taking the shortest path around the
        /// circle so animating from 350° to 10° goes the short way (through 360°), not the long way.
        /// The returned value is the running angle and may drift outside [0, 360); apply <c>% 360f</c> if
        /// you need a normalized output.
        /// </summary>
        public float AnimateAngle(
            float targetDegrees,
            float speed = 8f,
            string? id = null,
            [CallerLineNumber] int callerLine = 0)
        {
            string key = AnimateAngleKey + (id ?? callerLine.ToString());
            float current = GetElementStorage<float>(key, targetDegrees);
            if (DeltaTime > 0f && speed > 0f)
            {
                // Shortest angular distance: wrap diff into [-180, 180].
                float diff = ((targetDegrees - current) % 360f + 540f) % 360f - 180f;
                float t = 1f - Maths.Exp(-speed * DeltaTime);
                current += diff * t;
            }
            SetElementStorage(key, current);
            return current;
        }

        /// <summary>
        /// Returns the number of seconds <paramref name="current"/> has held its present value. Resets to
        /// 0 the frame the value flips. Tiny primitive that unlocks long-press detection, hover-delayed
        /// tooltips, staggered entrances, idle detection > anywhere "X has been true (or false) for N
        /// seconds" matters.
        /// <example>
        /// <code>
        /// // Long-press: trigger after 0.5s of held activity
        /// if (IsActive &amp;&amp; gui.StableFor(IsActive) &gt; 0.5f) DoLongPress();
        ///
        /// // Hover-delayed tooltip
        /// float tip = gui.AnimateBool(IsHovered &amp;&amp; gui.StableFor(IsHovered) &gt; 0.4f);
        ///
        /// // Stagger a list entrance > each row delays its mount by index*40ms
        /// float t = gui.AnimateBool(visible &amp;&amp; gui.StableFor(visible) &gt; index * 0.04f);
        /// </code>
        /// </example>
        /// </summary>
        public float StableFor(
            bool current,
            string? id = null,
            [CallerLineNumber] int callerLine = 0)
        {
            string key = StableForKey + (id ?? callerLine.ToString());
            var state = GetElementStorage<(bool last, float t)>(key, (current, 0f));
            if (state.last == current)
                state.t += DeltaTime;
            else
                state.t = 0f;
            state.last = current;
            SetElementStorage(key, state);
            return state.t;
        }

        /// <summary>
        /// Rising-edge shake: when <paramref name="trigger"/> goes from false to true, kicks an internal
        /// amplitude to 1 which then decays exponentially. Returns a 2-D offset suitable for adding to a
        /// translate / position. Apply repeatedly each frame on the same element to drive the shake.
        /// </summary>
        /// <param name="trigger">Rising edge fires the shake.</param>
        /// <param name="intensity">Peak displacement in pixels.</param>
        /// <param name="decay">Higher = shake fades faster (units of 1/sec; 6 ≈ ~0.5s tail).</param>
        /// <param name="frequency">How fast the shake oscillates (Hz-ish; 30 = quick chatter).</param>
        public Float2 Shake(
            bool trigger,
            float intensity = 4f,
            float decay = 6f,
            float frequency = 30f,
            string? id = null,
            [CallerLineNumber] int callerLine = 0)
        {
            string key = ShakeKey + (id ?? callerLine.ToString());
            var state = GetElementStorage<(bool prev, float amp)>(key, (false, 0f));

            if (trigger && !state.prev) state.amp = 1f;
            if (DeltaTime > 0f) state.amp *= Maths.Exp(-decay * DeltaTime);
            state.prev = trigger;
            SetElementStorage(key, state);

            if (state.amp < 0.001f) return Float2.Zero;
            float phase = Time * frequency;
            return new Float2(
                Maths.Sin(phase * 1.13f) * state.amp * intensity,
                Maths.Cos(phase * 0.97f) * state.amp * intensity);
        }

        #endregion
    }
}
