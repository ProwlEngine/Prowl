using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.PaperUI.RichText;

/// <summary>
/// Turns the effect spans covering a glyph into the transform it should be drawn with.
///
/// Every effect is a pure function of (glyph index, time, parameters), so the same time always
/// produces the same frame. A paused game genuinely pauses and screenshots reproduce.
///
/// Effects fall into three rhythms, which is what makes them read differently on screen:
/// some march along the text as a travelling wave, some move every glyph in step, and some give
/// each glyph its own phase so the run looks alive rather than mechanical.
/// </summary>
public static class RichTextEffects
{
    /// <summary>Offset amplitude for strength 1, as a fraction of the font size.</summary>
    private const float OffsetUnit = 0.06f;
    /// <summary>Rotation amplitude in degrees for strength 1.</summary>
    private const float RotationUnit = 8f;
    /// <summary>Scale amplitude for strength 1.</summary>
    private const float ScaleUnit = 0.12f;
    /// <summary>Shear amplitude for strength 1, as a fraction of the font size.</summary>
    private const float ShearUnit = 0.12f;
    /// <summary>Phase shift per glyph for a travelling wave.</summary>
    private const float TravelPhase = 0.35f;
    /// <summary>How far each glyph's phase may wander, in radians. Small enough to stay a group.</summary>
    private const float Scatter = 0.9f;
    private const float DegreesToRadians = MathF.PI / 180f;
    private const float Tau = MathF.PI * 2f;

    private static float Or(float value, float fallback) => float.IsNaN(value) ? fallback : value;

    /// <summary>
    /// Compose every effect covering <paramref name="index"/> into one transform.
    /// </summary>
    /// <param name="index">Character index into the visible text.</param>
    /// <param name="effects">Spans from the parser. Only those covering the index contribute.</param>
    /// <param name="time">Seconds on any steady clock. Every effect repeats over time, so where it starts does not matter.</param>
    /// <param name="pixelSize">Font pixel size, so movement scales with the text.</param>
    /// <param name="baseColor">The glyph's colour before any colour effect.</param>
    public static RichGlyphTransform Evaluate(
        int index,
        IReadOnlyList<RichEffectSpan> effects,
        float time,
        float pixelSize,
        Color baseColor)
    {
        var result = RichGlyphTransform.Identity(baseColor);
        if (effects == null || effects.Count == 0)
        {
            return result;
        }

        float offsetScale = pixelSize * OffsetUnit;
        float shearScale = pixelSize * ShearUnit;

        for (int i = 0; i < effects.Count; i++)
        {
            var span = effects[i];
            if (!span.Covers(index))
            {
                continue;
            }

            // Phase runs from the start of the span, so a wave restarts per span rather than
            // inheriting where the glyph happens to sit in the paragraph.
            int local = index - span.Start;
            float strength = span.Strength;
            float speed = span.Speed;

            switch (span.Kind)
            {
                case RichEffect.Shake:
                {
                    float amp = Or(strength, 1f) * offsetScale;
                    float rate = Or(speed, 18f);
                    // Quantised time makes it jump rather than glide, which is what a shake is.
                    float step = MathF.Floor(time * rate);
                    result.OffsetX += Hash(local * 2 + 1, step) * amp;
                    result.OffsetY += Hash(local * 2 + 2, step) * amp;
                    break;
                }

                case RichEffect.Wiggle:
                {
                    float amp = Or(strength, 1f) * offsetScale;
                    float rate = Or(speed, 3f);
                    // Each glyph gets its own axis and its own phase, so it slides back and forth
                    // between two points of its own rather than orbiting with everyone else.
                    float axis = Scramble(local * 3 + 1) * Tau;
                    float phase = Scramble(local * 3 + 2) * Tau;
                    float travel = MathF.Sin(time * rate + phase) * amp;
                    result.OffsetX += MathF.Cos(axis) * travel;
                    result.OffsetY += MathF.Sin(axis) * travel;
                    break;
                }

                case RichEffect.Wave:
                {
                    float amp = Or(strength, 1f) * offsetScale;
                    float rate = Or(speed, 6f);
                    result.OffsetY += MathF.Sin(time * rate - local * TravelPhase) * amp;
                    break;
                }

                case RichEffect.Swell:
                {
                    // The same travelling wave as Wave, driving size instead of height.
                    float amp = Or(strength, 1f) * ScaleUnit;
                    float rate = Or(speed, 6f);
                    float s = 1f + MathF.Sin(time * rate - local * TravelPhase) * amp;
                    result.ScaleX *= s;
                    result.ScaleY *= s;
                    break;
                }

                case RichEffect.Bounce:
                {
                    float amp = Or(strength, 1f) * offsetScale * 3f;
                    // Speed is waves per second, and the glyphs rest between passes.
                    float rate = Or(speed, 0.5f);
                    float cycle = 1f / MathF.Max(rate, 0.0001f);
                    // Each glyph is kicked a little after the one before it.
                    float trigger = local * 0.05f;
                    float phase = Repeat(time - trigger, cycle);
                    const float HopDuration = 0.6f;
                    if (phase < HopDuration)
                    {
                        float p = phase / HopDuration;
                        // Two arcs, the second much smaller: jump, land, a small bounce, settle.
                        float decay = (1f - p) * (1f - p);
                        result.OffsetY -= MathF.Abs(MathF.Sin(p * MathF.PI * 2f)) * decay * amp;
                    }

                    break;
                }

                case RichEffect.Slide:
                {
                    // Skew about the centre: the top leans one way and the bottom the other,
                    // every glyph in step.
                    float amp = Or(strength, 1f) * shearScale;
                    float rate = Or(speed, 3f);
                    result.ShearX += MathF.Sin(time * rate) * amp;
                    result.ShearPivotY = 0.5f;
                    break;
                }

                case RichEffect.Dangle:
                {
                    // A flag: the top edge is pinned and the bottom swings, each glyph slightly
                    // out of step with its neighbours.
                    float amp = Or(strength, 1f) * shearScale * 1.5f;
                    float rate = Or(speed, 2.5f);
                    float phase = Scramble(local * 5 + 1) * Scatter;
                    result.ShearX += MathF.Sin(time * rate + phase) * amp;
                    result.ShearPivotY = 0f;
                    break;
                }

                case RichEffect.Pendulum:
                {
                    // Hanging from a pivot at the top centre, each glyph on its own slightly
                    // different swing.
                    float amp = Or(strength, 1f) * RotationUnit * 1.5f;
                    float rate = Or(speed, 3f);
                    float phase = Scramble(local * 7 + 3) * Scatter;
                    result.Rotation += MathF.Sin(time * rate + phase) * amp * DegreesToRadians;
                    result.PivotX = 0.5f;
                    result.PivotY = 0f;
                    break;
                }

                case RichEffect.Swing:
                {
                    // Pendulum's motion, but about the centre and with every glyph in step.
                    float amp = Or(strength, 1f) * RotationUnit;
                    float rate = Or(speed, 4f);
                    result.Rotation += MathF.Sin(time * rate) * amp * DegreesToRadians;
                    result.PivotX = 0.5f;
                    result.PivotY = 0.5f;
                    break;
                }

                case RichEffect.Rotate:
                {
                    float turns = Or(strength, 1f);
                    float rate = Or(speed, 1f);
                    result.Rotation += time * rate * turns * Tau;
                    result.PivotX = 0.5f;
                    result.PivotY = 0.5f;
                    break;
                }

                case RichEffect.Pulse:
                {
                    float amp = Or(strength, 1f) * ScaleUnit;
                    float rate = Or(speed, 4f);
                    float s = 1f + MathF.Sin(time * rate) * amp;
                    result.ScaleX *= s;
                    result.ScaleY *= s;
                    break;
                }

                case RichEffect.Rainbow:
                {
                    float saturation = Math.Clamp(Or(strength, 1f), 0f, 1f);
                    float rate = Or(speed, 0.6f);
                    float hue = Frac(time * rate + local * 0.06f);
                    var rainbow = FromHue(hue, saturation);
                    result.Color = new Color(rainbow.R, rainbow.G, rainbow.B, result.Color.A);
                    break;
                }
            }
        }

        return result;
    }

    private static float Frac(float value) => value - MathF.Floor(value);

    private static float Repeat(float value, float length) => value - MathF.Floor(value / length) * length;

    /// <summary>A stable value in 0..1 for a glyph, used to give each one its own phase.</summary>
    private static float Scramble(int lane)
    {
        uint h = (uint)(lane * 374761393);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return h / (float)uint.MaxValue;
    }

    /// <summary>Deterministic noise in -1..1 from an integer lane and a quantised time step.</summary>
    private static float Hash(int lane, float step)
    {
        uint h = (uint)(lane * 374761393) + (uint)((int)step * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h / (float)uint.MaxValue) * 2f - 1f;
    }

    /// <summary>Fully bright colour at a hue in 0..1.</summary>
    private static Color FromHue(float hue, float saturation)
    {
        float h = Frac(hue) * 6f;
        int sector = (int)h;
        float f = h - sector;
        float q = 1f - f;

        (float r, float g, float b) = sector switch
        {
            0 => (1f, f, 0f),
            1 => (q, 1f, 0f),
            2 => (0f, 1f, f),
            3 => (0f, q, 1f),
            4 => (f, 0f, 1f),
            _ => (1f, 0f, q)
        };

        // Desaturate toward white so strength can dial the effect down.
        r = 1f + (r - 1f) * saturation;
        g = 1f + (g - 1f) * saturation;
        b = 1f + (b - 1f) * saturation;
        return new Color(r, g, b, 1f);
    }
}
