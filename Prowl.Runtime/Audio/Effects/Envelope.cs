// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

// Originally Created by Nigel Redmon on 12/18/12.
// EarLevel Engineering: earlevel.com
// C# Port 2024 W.M.R Jap-A-Joe

using System;
using Prowl.Runtime.Audio;
using Prowl.Vector;

namespace Prowl.Runtime.Audio.Effects;

public class ADSR
{
    public float a;
    public float d;
    public float s;
    public float r;

    public ADSR(float a, float d, float s, float r)
    {
        this.a = a * AudioContext.SampleRate;
        this.d = d * AudioContext.SampleRate;
        this.s = s * AudioContext.SampleRate;
        this.r = r * AudioContext.SampleRate;
    }
}

public sealed class Envelope
{
    public enum EnvelopeState
    {
        Idle = 0,
        Attack,
        Decay,
        Sustain,
        Release
    }

    private EnvelopeState state;
    private float output;
    private float attackRate;
    private float decayRate;
    private float releaseRate;
    private float attackCoef;
    private float decayCoef;
    private float releaseCoef;
    private float sustainLevel;
    private float targetRatioA;
    private float targetRatioDR;
    private float attackBase;
    private float decayBase;
    private float releaseBase;

    public float Attack
    {
        get
        {
            return attackRate;
        }
        set
        {
            SetAttackRate(value);
        }
    }

    public float Decay
    {
        get
        {
            return decayRate;
        }
        set
        {
            SetDecayRate(value);
        }
    }

    public float Sustain
    {
        get
        {
            return sustainLevel;
        }
        set
        {
            SetSustainLevel(value);
        }
    }

    public float Release
    {
        get
        {
            return releaseRate;
        }
        set
        {
            SetReleaseRate(value);
        }
    }

    public EnvelopeState State
    {
        get
        {
            return state;
        }
    }

    public float Output
    {
        get
        {
            return output;
        }
    }

    public Envelope()
    {
        Reset();

        SetAttackRate(0);
        SetDecayRate(0);
        SetReleaseRate(0);
        SetSustainLevel(1.0f);
        SetTargetRatioA(0.3f);
        SetTargetRatioDR(0.0001f);

        state = EnvelopeState.Attack;
    }

    public Envelope(ADSR config)
    {
        Reset();

        SetAttackRate(config.a);
        SetDecayRate(config.d);
        SetReleaseRate(config.r);
        SetSustainLevel(config.s);
        SetTargetRatioA(0.3f);
        SetTargetRatioDR(0.0001f);

        state = EnvelopeState.Attack;
    }

    public Envelope(float attackRate, float decayRate, float sustainLevel, float releaseRate)
    {
        Reset();

        SetAttackRate(attackRate);
        SetDecayRate(decayRate);
        SetReleaseRate(releaseRate);
        SetSustainLevel(sustainLevel);
        SetTargetRatioA(0.3f);
        SetTargetRatioDR(0.0001f);

        state = EnvelopeState.Attack;
    }

    public float Process()
    {
        switch (state) 
        {
            case EnvelopeState.Idle:
            {
                break;
            }
            case EnvelopeState.Attack:
            {
                output = attackBase + output * attackCoef;
                if (output >= 1.0f) {
                    output = 1.0f;
                    state = EnvelopeState.Decay;
                }
                break;
            }
            case EnvelopeState.Decay:
            {
                output = decayBase + output * decayCoef;
                if (output <= sustainLevel) {
                    output = sustainLevel;
                    state = EnvelopeState.Sustain;
                }
                break;
            }
            case EnvelopeState.Sustain:
            {
                break;
            }
            case EnvelopeState.Release:
            {
                output = releaseBase + output * releaseCoef;
                if (output <= 0.0f) {
                    output = 0.0f;
                    state = EnvelopeState.Idle;
                }
                break;
            }
        }
        return (float)output;
    }

    public void SetGate(bool enabled)
    {
        if (enabled)
            state = EnvelopeState.Attack;
        else if (state != EnvelopeState.Idle)
            state = EnvelopeState.Release;
    }

    public void Reset()
    {
        state = EnvelopeState.Idle;
        output = 0.0f;
    }

    private void SetAttackRate(float rate)
    {
        attackRate = rate;
        attackCoef = CalcCoef(rate, targetRatioA);
        attackBase = (1.0f + targetRatioA) * (1.0f - attackCoef);
    }

    private void SetDecayRate(float rate)
    {
        decayRate = rate;
        decayCoef = CalcCoef(rate, targetRatioDR);
        decayBase = (sustainLevel - targetRatioDR) * (1.0f - decayCoef);
    }

    private void SetReleaseRate(float rate)
    {
        releaseRate = rate;
        releaseCoef = CalcCoef(rate, targetRatioDR);
        releaseBase = -targetRatioDR * (1.0f - releaseCoef);
    }

    private float CalcCoef(float rate, float targetRatio)
    {
        return (rate <= 0) ? 0.0f : Maths.Exp(-Maths.Log((1.0f + targetRatio) / targetRatio) / rate);
    }

    private void SetSustainLevel(float level)
    {
        sustainLevel = level;
        decayBase = (sustainLevel - targetRatioDR) * (1.0f - decayCoef);
    }

    private void SetTargetRatioA(float targetRatio)
    {
        if (targetRatio < 0.000000001f)
            targetRatio = 0.000000001f;  // -180 dB
        targetRatioA = targetRatio;
        attackCoef = CalcCoef(attackRate, targetRatioA);
        attackBase = (1.0f + targetRatioA) * (1.0f - attackCoef);
    }

    private void SetTargetRatioDR(float targetRatio)
    {
        if (targetRatio < 0.000000001f)
            targetRatio = 0.000000001f;  // -180 dB
        targetRatioDR = targetRatio;
        decayCoef = CalcCoef(decayRate, targetRatioDR);
        releaseCoef = CalcCoef(releaseRate, targetRatioDR);
        decayBase = (sustainLevel - targetRatioDR) * (1.0f - decayCoef);
        releaseBase = -targetRatioDR * (1.0f - releaseCoef);
    }
}
