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
    private delegate void CalcCoefficientsFunc(Filter filter);
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
    private float z1;
    private float z2;
    private CalcCoefficientsFunc calcCoefficients;

    public FilterType Type
    {
        get
        {
            return type;
        }
    }
    
    public float Frequency
    {
        get
        {
            return frequency;
        }
        set
        {
            if(sampleRate <= (value * 2))
                throw new ArgumentException("Frequency must be less than 0.5 * sample rate");
            frequency = value;
            calcCoefficients(this);
        }
    }

    public float Q
    {
        get
        {
            return q;
        }
        set
        {
            if(value <= 0.0f)
                throw new ArgumentException("Q can not be zero");
            q = value;
            calcCoefficients(this);
        }
    }

    public float GainDB
    {
        get
        {
            return gainDB;
        }
        set
        {
            gainDB = value;
            calcCoefficients(this);
        }
    }

    public Filter(FilterType type, float frequency, float q, float gainDB, int sampleRate = 0)
    {
        if(sampleRate <= 0)
            this.sampleRate = AudioContext.SampleRate;
        if(frequency <= 0.0f)
            throw new ArgumentException("Frequency must be greater than 0");
        if(sampleRate <= (frequency * 2))
            throw new ArgumentException("Frequency must be less than 0.5 * sample rate");
        if(q <= 0.0f)
            throw new ArgumentException("Q can not be zero");

        this.type = type;
        this.sampleRate = sampleRate == 0 ? AudioContext.SampleRate : sampleRate;
        this.frequency = frequency;
        this.q = q;
        this.gainDB = gainDB > 0.0f ? gainDB : 6.0f;
        SetCoefficientsFunc();
        calcCoefficients(this);
    }

    public float Process(float input)
    {
        float output = input * a0 + z1;
        z1 = input * a1 + z2 - b1 * output;
        z2 = input * a2 - b2 * output;
        return output;
    }

    public void Process(NativeArray<float> framesOut, NativeArray<float> framesIn, ulong frameCount, int channels)
    {
        float output = 0.0f;
        float currentSample = 0.0f;
        
        for(int i = 0; i < framesIn.Length; i+=channels)
        {
            for(int j = 0; j < channels; j++)
            {
                currentSample = framesIn[i+j]; 
                output = currentSample * a0 + z1;
                z1 = currentSample * a1 + z2 - b1 * output;
                z2 = currentSample * a2 - b2 * output;
                framesOut[i+j] = output;
            }
        }
    }

    private void SetCoefficientsFunc()
    {
        switch(type) 
        {
            case FilterType.Lowpass:
                calcCoefficients = CalculateLowpassCoefficients;
                break;
            case FilterType.Highpass:
                calcCoefficients = CalculateHighpassCoefficients;
                break;
            case FilterType.Bandpass:
                calcCoefficients = CalculateBandpassCoefficients;
                break;
            case FilterType.Lowshelf:
                calcCoefficients = CalculateLowshelfCoefficients;
                break;
            case FilterType.Highshelf:
                calcCoefficients = CalculateHighshelfCoefficients;
                break;
            case FilterType.Peak:
                calcCoefficients = CalculatePeakCoefficients;
                break;
            case FilterType.Notch:
                calcCoefficients = CalculateNotchCoefficients;
                break;
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
