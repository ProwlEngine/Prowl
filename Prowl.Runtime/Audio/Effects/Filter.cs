// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using Prowl.Runtime.Audio.Native;
using Prowl.Vector;

namespace Prowl.Runtime.Audio.Effects;

public enum FilterType
{
    Lowpass,
    Highpass,
    Bandpass,
    Lowshelf,
    Highshelf,
    Peak,
    Notch
}

public sealed class Filter
{
    private FilterType type;
    private Int32 sampleRate;
    private float frequency;
    private float q;
    private float gainDB;
    private float a0;
    private float a1;
    private float a2;
    private float b1;
    private float b2;
    // One delay pair per channel. A biquad carries state, so running an interleaved stereo stream
    // through a single pair is not a stereo filter, it is one filter fed two unrelated signals.
    private float[] z1;
    private float[] z2;

    /// <summary>
    /// Which response this filter has. Changing it re-picks the coefficient set and leaves the delay
    /// state alone, so a live filter can change shape without restarting.
    /// </summary>
    public FilterType Type
    {
        get
        {
            return type;
        }
        set
        {
            if (type == value)
                return;

            type = value;
            Recalculate();
        }
    }
    
    /// <summary>Cutoff or centre frequency, clamped to the range the coefficients stay stable over.</summary>
    public float Frequency
    {
        get
        {
            return frequency;
        }
        set
        {
            frequency = ClampFrequency(value);
            Recalculate();
        }
    }

    /// <summary>Resonance. Clamped away from zero, which the coefficients divide by.</summary>
    public float Q
    {
        get
        {
            return q;
        }
        set
        {
            q = ClampQ(value);
            Recalculate();
        }
    }

    /// <summary>Shelf or peak gain in decibels. Negative cuts, positive boosts. Only the Lowshelf,
    /// Highshelf and Peak types use it.</summary>
    public float GainDB
    {
        get
        {
            return gainDB;
        }
        set
        {
            gainDB = value;
            Recalculate();
        }
    }

    /// <summary>
    /// Out of range values are clamped rather than rejected. These are live parameters that gameplay
    /// drives from sliders and curves, so an exception out of a setter or a constructor is a crash
    /// where a sane limit is what the caller wanted.
    /// </summary>
    public Filter(FilterType type, float frequency, float q, float gainDB, int sampleRate = 0, int channels = 0)
    {
        this.type = type;
        this.sampleRate = sampleRate > 0 ? sampleRate : AudioContext.SampleRate;
        this.frequency = ClampFrequency(frequency);
        this.q = ClampQ(q);
        this.gainDB = gainDB;
        AllocateState(channels > 0 ? channels : AudioContext.Channels);
        Recalculate();
    }

    // The coefficients run the frequency through tan(pi * f / sampleRate), which runs away as it
    // approaches Nyquist, so the top of the range keeps a margin below it.
    private float ClampFrequency(float value) => Maths.Clamp(value, 1.0f, sampleRate * 0.49f);

    private float ClampQ(float value) => Maths.Max(value, 0.01f);

    /// <summary>Filters one sample of a single stream, using the first channel's state.</summary>
    public float Process(float input)
    {
        float output = input * a0 + z1[0];
        z1[0] = input * a1 + z2[0] - b1 * output;
        z2[0] = input * a2 - b2 * output;
        return output;
    }

    /// <summary>Filters <paramref name="frameCount"/> interleaved frames, each channel against its own state.</summary>
    public void Process(NativeArray<float> framesIn, NativeArray<float> framesOut, ulong frameCount, int channels)
    {
        if (channels <= 0)
            return;

        if (channels > z1.Length)
            AllocateState(channels);

        int frames = (int)frameCount;
        int available = Math.Min(framesIn.Length, framesOut.Length) / channels;

        if (frames > available)
            frames = available;

        for(int frame = 0; frame < frames; frame++)
        {
            int start = frame * channels;

            for(int channel = 0; channel < channels; channel++)
            {
                int i = start + channel;
                float input = framesIn[i];
                float output = input * a0 + z1[channel];
                z1[channel] = input * a1 + z2[channel] - b1 * output;
                z2[channel] = input * a2 - b2 * output;
                framesOut[i] = output;
            }
        }
    }

    private void AllocateState(int channels)
    {
        z1 = new float[Math.Max(1, channels)];
        z2 = new float[Math.Max(1, channels)];
    }

    /// <summary>
    /// Works the coefficients out for the filter's current shape and parameters.
    /// </summary>
    /// <remarks>
    /// Dispatched here rather than through a delegate picked when the shape changes. A delegate is a
    /// field like any other, and cloning an object graph does not carry one across: a filter that
    /// arrived by being copied, which is what instantiating a prefab does to every effect on it, then
    /// threw on the first parameter it was given.
    /// </remarks>
    private void Recalculate()
    {
        switch (type)
        {
            case FilterType.Lowpass: CalculateLowpassCoefficients(this); break;
            case FilterType.Highpass: CalculateHighpassCoefficients(this); break;
            case FilterType.Bandpass: CalculateBandpassCoefficients(this); break;
            case FilterType.Lowshelf: CalculateLowshelfCoefficients(this); break;
            case FilterType.Highshelf: CalculateHighshelfCoefficients(this); break;
            case FilterType.Peak: CalculatePeakCoefficients(this); break;
            case FilterType.Notch: CalculateNotchCoefficients(this); break;
        }
    }

    private static void CalculateLowpassCoefficients(Filter filter) 
    {
        float k = (float)Maths.Tan(Maths.PI * filter.frequency / filter.sampleRate);
        float norm = 1.0f / (1.0f + k / filter.q + k * k);
        filter.a0 = k * k * norm;
        filter.a1 = 2.0f * filter.a0;
        filter.a2 = filter.a0;
        filter.b1 = 2.0f * (k * k - 1.0f) * norm;
        filter.b2 = (1.0f - k / filter.q + k * k) * norm;
    }

    private static void CalculateHighpassCoefficients(Filter filter) 
    {
        float k = (float)Maths.Tan(Maths.PI * filter.frequency / filter.sampleRate);
        float norm = 1.0f / (1.0f + k / filter.q + k * k);
        filter.a0 = 1.0f * norm;
        filter.a1 = -2.0f * filter.a0;
        filter.a2 = filter.a0;
        filter.b1 = 2.0f * (k * k - 1.0f) * norm;
        filter.b2 = (1.0f - k / filter.q + k * k) * norm;
    }

    private static void CalculateBandpassCoefficients(Filter filter) 
    {
        float k = (float)Maths.Tan(Maths.PI * filter.frequency / filter.sampleRate);
        float norm = 1.0f / (1.0f + k / filter.q + k * k);
        filter.a0 = k / filter.q * norm;
        filter.a1 = 0.0f;
        filter.a2 = -filter.a0;
        filter.b1 = 2.0f * (k * k - 1.0f) * norm;
        filter.b2 = (1.0f - k / filter.q + k * k) * norm;
    }

    private static void CalculateLowshelfCoefficients(Filter filter) 
    {
        const float sqrt2 = 1.4142135623730951f;
        float k = (float)Maths.Tan(Maths.PI * filter.frequency / filter.sampleRate);
        float v = (float)Maths.Pow(10.0f, Maths.Abs(filter.gainDB) / 20.0f);
        float norm;
        if (filter.gainDB >= 0.0f) {
            // boost
            norm = 1.0f / (1.0f + sqrt2 * k + k * k);
            filter.a0 = (1.0f + (float)Maths.Sqrt(2.0f * v) * k + v * k * k) * norm;
            filter.a1 = 2.0f * (v * k * k - 1.0f) * norm;
            filter.a2 = (1.0f - (float)Maths.Sqrt(2.0f * v) * k + v * k * k) * norm;
            filter.b1 = 2.0f * (k * k - 1.0f) * norm;
            filter.b2 = (1.0f - sqrt2 * k + k * k) * norm;
        } else {
            // cut
            norm = 1.0f / (1.0f + (float)Maths.Sqrt(2.0f * v) * k + v * k * k);
            filter.a0 = (1.0f + sqrt2 * k + k * k) * norm;
            filter.a1 = 2.0f * (k * k - 1.0f) * norm;
            filter.a2 = (1.0f - sqrt2 * k + k * k) * norm;
            filter.b1 = 2.0f * (v * k * k - 1.0f) * norm;
            filter.b2 = (1.0f - (float)Maths.Sqrt(2.0f * v) * k + v * k * k) * norm;
        }
    }

    private static void CalculateHighshelfCoefficients(Filter filter) 
    {
        const float sqrt2 = 1.4142135623730951f;
        float k = (float)Maths.Tan(Maths.PI * filter.frequency / filter.sampleRate);
        float v = (float)Maths.Pow(10.0f, Maths.Abs(filter.gainDB) / 20.0f);
        float norm = 0.0f;
        if (filter.gainDB >= 0) {
            // boost
            norm = 1.0f / (1.0f + sqrt2 * k + k * k);
            filter.a0 = (v + (float)Maths.Sqrt(2.0f * v) * k + k * k) * norm;
            filter.a1 = 2.0f * (k * k - v) * norm;
            filter.a2 = (v - (float)Maths.Sqrt(2.0f * v) * k + k * k) * norm;
            filter.b1 = 2.0f * (k * k - 1.0f) * norm;
            filter.b2 = (1.0f - sqrt2 * k + k * k) * norm;
        } else {
            // cut
            norm = 1.0f / (v + (float)Maths.Sqrt(2.0f * v) * k + k * k);
            filter.a0 = (1.0f + sqrt2 * k + k * k) * norm;
            filter.a1 = 2.0f * (k * k - 1.0f) * norm;
            filter.a2 = (1.0f - sqrt2 * k + k * k) * norm;
            filter.b1 = 2.0f * (k * k - v) * norm;
            filter.b2 = (v - (float)Maths.Sqrt(2.0f * v) * k + k * k) * norm;
        }
    }

    private static void CalculatePeakCoefficients(Filter filter) 
    {
        float v = (float)Maths.Pow(10, Maths.Abs(filter.gainDB) / 20.0f);
        float k = (float)Maths.Tan(Maths.PI * filter.frequency / filter.sampleRate);
        float q = filter.q;
        float norm = 0;

        if (filter.gainDB >= 0.0f) {
            //boost 
            norm = 1.0f / (1.0f + 1.0f / q * k + k * k);
            filter.a0 = (1.0f + v / q * k + k * k) * norm;
            filter.a1 = 2.0f * (k * k - 1.0f) * norm;
            filter.a2 = (1.0f - v / q * k + k * k) * norm;
            filter.b1 = filter.a1;
            filter.b2 = (1.0f - 1.0f / q * k + k * k) * norm;
        }  else {
            //cut
            norm = 1.0f / (1.0f + v / q * k + k * k);
            filter.a0 = (1.0f + 1.0f / q * k + k * k) * norm;
            filter.a1 = 2.0f * (k * k - 1.0f) * norm;
            filter.a2 = (1.0f - 1.0f / q * k + k * k) * norm;
            filter.b1 = filter.a1;
            filter.b2 = (1.0f - v / q * k + k * k) * norm;
        }
    }

    private static void CalculateNotchCoefficients(Filter filter) 
    {
        float k = (float)Maths.Tan(Maths.PI * filter.frequency / filter.sampleRate);
        float norm = 1.0f / (1.0f + k / filter.q + k * k);
        filter.a0 = (1.0f + k * k) * norm;
        filter.a1 = 2.0f * (k * k - 1.0f) * norm;
        filter.a2 = filter.a0;
        filter.b1 = filter.a1;
        filter.b2 = (1.0f - k / filter.q + k * k) * norm;
    }
}
