using System;
using System.Collections.Generic;

namespace Prowl.Scribe
{
    /// <summary>
    /// Per-glyph render transform produced by evaluating all active effect spans for a single
    /// glyph at a given time.
    /// </summary>
    public struct RichEffectResult
    {
        public float OffsetX;
        public float OffsetY;
        public float Scale;
        public FontColor Color;
        public bool Visible;
    }

    /// <summary>
    /// Evaluates a glyph's <see cref="RichEffectResult"/> by walking active effect spans and
    /// composing their contributions.
    ///
    /// Effects are deterministic functions of (glyph index, char index, elapsed time, params),
    /// so animations look the same on every frame at a given <c>t</c>.
    /// </summary>
    public static class RichTextEffects
    {
        public static RichEffectResult Evaluate(in RichGlyph g, IReadOnlyList<RichEffectSpan> effects,
            float t, RichTextLayoutSettings settings)
        {
            var r = new RichEffectResult {
                OffsetX = 0f, OffsetY = 0f,
                Scale = 1f,
                Color = g.Color,
                Visible = true,
            };

            int ci = g.CharIndex;
            // Glyph-relative index within its own effect span — used for per-glyph phase shifts so
            // the wave/rainbow still march along even when the same effect wraps multiple lines.
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (ci < e.Start || ci >= e.End) continue;
                int local = ci - e.Start;

                switch (e.Kind)
                {
                    case RichEffectKind.Shake:
                    {
                        float amp = float.IsNaN(e.P0) ? settings.DefaultShakeAmp : e.P0;
                        float freq = float.IsNaN(e.P1) ? settings.DefaultShakeFreq : e.P1;
                        // Per-glyph deterministic noise — different seeds for X and Y axes.
                        float ox = Noise2(local * 13 + 1, t * freq) * amp;
                        float oy = Noise2(local * 17 + 7, t * freq + 3.7f) * amp;
                        r.OffsetX += ox;
                        r.OffsetY += oy;
                        break;
                    }
                    case RichEffectKind.Wave:
                    {
                        float amp = float.IsNaN(e.P0) ? settings.DefaultWaveAmp : e.P0;
                        float freq = float.IsNaN(e.P1) ? settings.DefaultWaveFreq : e.P1;
                        float phase = float.IsNaN(e.P2) ? settings.DefaultWavePhase : e.P2;
                        r.OffsetY += MathF.Sin(t * freq + local * phase) * amp;
                        break;
                    }
                    case RichEffectKind.Rainbow:
                    {
                        float speed = float.IsNaN(e.P0) ? settings.DefaultRainbowSpeed : e.P0;
                        float spread = float.IsNaN(e.P1) ? settings.DefaultRainbowSpread : e.P1;
                        float sat = float.IsNaN(e.P2) ? settings.DefaultRainbowSat : e.P2;
                        float val = float.IsNaN(e.P3) ? settings.DefaultRainbowValue : e.P3;
                        float hue = ((t * speed + local * spread) % 1f) * 360f;
                        if (hue < 0) hue += 360f;
                        var rb = FontColor.FromHSV(hue, sat, val, r.Color.A / 255f);
                        r.Color = rb;
                        break;
                    }
                    case RichEffectKind.Pulse:
                    {
                        float speed = float.IsNaN(e.P0) ? settings.DefaultPulseSpeed : e.P0;
                        float amp = float.IsNaN(e.P1) ? settings.DefaultPulseAmp : e.P1;
                        float s = 1f + MathF.Sin(t * speed + local * 0.4f) * amp;
                        r.Scale *= s;
                        break;
                    }
                    case RichEffectKind.Fade:
                    {
                        float speed = float.IsNaN(e.P0) ? settings.DefaultFadeSpeed : e.P0;
                        float a = (MathF.Sin(t * speed + local * 0.3f) * 0.5f + 0.5f);
                        var c = r.Color;
                        c.A = (byte)Math.Clamp((int)(r.Color.A * a + 0.5f), 0, 255);
                        r.Color = c;
                        break;
                    }
                    case RichEffectKind.Jitter:
                    {
                        float amp = float.IsNaN(e.P0) ? settings.DefaultJitterAmp : e.P0;
                        float freq = float.IsNaN(e.P1) ? settings.DefaultJitterFreq : e.P1;
                        // High-frequency uncorrelated micro-shake; uses different prime offsets.
                        float ox = Noise2(local * 31 + 5, t * freq) * amp;
                        float oy = Noise2(local * 37 + 11, t * freq + 1.3f) * amp;
                        r.OffsetX += ox;
                        r.OffsetY += oy;
                        break;
                    }
                    case RichEffectKind.Typewriter:
                    {
                        float speed = float.IsNaN(e.P0) ? settings.DefaultTypewriterSpeed : e.P0;
                        float fadeIn = float.IsNaN(e.P1) ? settings.DefaultTypewriterFadeIn : e.P1;
                        // Glyph `local` starts revealing at local/speed and is fully shown by
                        // (local + max(fadeIn, 1/speed)). At t=0 nothing is revealed.
                        float startT = local / MathF.Max(speed, 0.0001f);
                        float fadeDur = MathF.Max(fadeIn, 1f / MathF.Max(speed, 0.0001f));
                        if (t <= startT)
                        {
                            r.Visible = false;
                        }
                        else if (t < startT + fadeDur)
                        {
                            float a = (t - startT) / fadeDur;
                            var c = r.Color;
                            c.A = (byte)Math.Clamp((int)(r.Color.A * a + 0.5f), 0, 255);
                            r.Color = c;
                        }
                        // else: fully visible, leave color alone
                        break;
                    }
                }
            }
            return r;
        }

        // Cheap deterministic 2D pseudo-noise in [-1, 1]. Not a real PRNG — good enough for
        // shake/jitter visual effects and stable across frames at the same (i, t).
        private static float Noise2(int i, float t)
        {
            // Hash glyph index into the seed so different glyphs animate independently.
            unchecked
            {
                uint h = (uint)i * 374761393u + 0x68E31DA4u;
                h = (h ^ (h >> 13)) * 1274126177u;
                float seed = ((h & 0xFFFFFu) / (float)0xFFFFF) * 6.2831853f;
                return MathF.Sin(t * 6.2831853f + seed);
            }
        }
    }
}
