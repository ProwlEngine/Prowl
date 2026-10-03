// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>A group of particles emitted at once, at a time in seconds into each loop.</summary>
[Serializable]
public struct ParticleBurst
{
    [Tooltip("Seconds into the loop.")]
    public float Time;
    public int MinCount;
    public int MaxCount;
    [Tooltip("How many times it fires each loop. 0 keeps repeating until the loop ends.")]
    public int Cycles;
    [Tooltip("Seconds between cycles.")]
    public float Interval;
    [Range(0f, 1f), Tooltip("Chance each cycle actually fires.")]
    public float Probability;

    public ParticleBurst()
    {
        MinCount = 30;
        MaxCount = 30;
        Cycles = 1;
        Interval = 0.01f;
        Probability = 1f;
    }

    public ParticleBurst(float time, int count) : this()
    {
        Time = time;
        MinCount = count;
        MaxCount = count;
    }

    public ParticleBurst(float time, int minCount, int maxCount, int cycles, float interval) : this()
    {
        Time = time;
        MinCount = minCount;
        MaxCount = maxCount;
        Cycles = cycles;
        Interval = interval;
    }
}

/// <summary>
/// When particles are born: a steady rate over time, a rate per unit the emitter travels, and bursts.
/// Rate curves are sampled at the system's normalized time.
/// </summary>
[Serializable]
public class EmissionModule : ParticleSystemModule
{
    [Tooltip("Particles per second.")]
    public MinMaxCurve RateOverTime = new(10f);

    [Tooltip("Particles per world unit the emitter moves.")]
    public MinMaxCurve RateOverDistance = new(0f);

    public List<ParticleBurst> Bursts = new();

    private float _timeAccumulator;
    private float _distanceAccumulator;
    private int[] _cyclesDone = Array.Empty<int>();
    private float[] _nextBurstTime = Array.Empty<float>();

    public EmissionModule() => Enabled = true;

    internal void Reset()
    {
        _timeAccumulator = 0f;
        _distanceAccumulator = 0f;
        ResetBursts();
    }

    /// <summary>Rearms every burst for a new loop.</summary>
    internal void ResetBursts()
    {
        if (_cyclesDone.Length != Bursts.Count)
        {
            _cyclesDone = new int[Bursts.Count];
            _nextBurstTime = new float[Bursts.Count];
        }
        for (int i = 0; i < Bursts.Count; i++)
        {
            _cyclesDone[i] = 0;
            _nextBurstTime[i] = Bursts[i].Time;
        }
    }

    /// <summary>
    /// Emits for the loop time span [<paramref name="t0"/>, <paramref name="t1"/>), which starts
    /// <paramref name="stepOffset"/> seconds into the current step. Each particle is handed to the system with
    /// the moment it was born, so a slow frame spreads particles out instead of clumping them.
    /// </summary>
    internal void Emit(ParticleSystemComponent system, float t0, float t1, float duration, float distance, float stepOffset, Random random)
    {
        float span = t1 - t0;
        if (span <= 0f) return;
        float time01 = duration > 0f ? t0 / duration : 0f;

        float rate = MathF.Max(0f, RateOverTime.Evaluate(time01, random));
        EmitContinuous(system, ref _timeAccumulator, rate * span, span, stepOffset);

        if (distance > 0f)
        {
            float perUnit = MathF.Max(0f, RateOverDistance.Evaluate(time01, random));
            EmitContinuous(system, ref _distanceAccumulator, perUnit * distance, span, stepOffset);
        }

        if (_cyclesDone.Length != Bursts.Count)
            ResetBursts();

        for (int i = 0; i < Bursts.Count; i++)
        {
            ParticleBurst burst = Bursts[i];
            float interval = MathF.Max(burst.Interval, 0.01f);
            while ((burst.Cycles <= 0 || _cyclesDone[i] < burst.Cycles) && _nextBurstTime[i] < t1 && _nextBurstTime[i] < duration)
            {
                int count = RollBurst(burst, system.MaxParticles, random);
                if (count > 0)
                {
                    float at = stepOffset + MathF.Max(0f, _nextBurstTime[i] - t0);
                    system.SpawnBatch(count, at, at);
                }
                _cyclesDone[i]++;
                _nextBurstTime[i] += interval;
            }
        }
    }

    /// <summary>
    /// One cycle of a burst: 0 when its probability roll fails, otherwise a count from its range, clamped
    /// to what the system can hold so huge counts cannot overflow.
    /// </summary>
    internal static int RollBurst(in ParticleBurst burst, int maxParticles, Random random)
    {
        if (burst.Probability < 1f && random.NextSingle() >= burst.Probability) return 0;

        int cap = Math.Clamp(maxParticles, 0, int.MaxValue - 1);
        int min = Math.Clamp(burst.MinCount, 0, cap);
        int max = Math.Clamp(burst.MaxCount, min, cap);
        return min == max ? min : random.Next(min, max + 1);
    }

    // Particles are born at the exact moments the accumulator crosses a whole number.
    private static void EmitContinuous(ParticleSystemComponent system, ref float accumulator, float amount, float span, float stepOffset)
    {
        if (!(amount > 0f)) return;
        if (!float.IsFinite(accumulator)) accumulator = 0f;

        // More than a system can ever hold in one step is pointless, and would overflow the count.
        amount = MathF.Min(amount, Math.Max(1, system.MaxParticles));

        float before = accumulator;
        accumulator += amount;
        int count = (int)accumulator;
        accumulator -= count;
        if (count <= 0) return;

        float first = stepOffset + span * (1f - before) / amount;
        float last = stepOffset + span * (count - before) / amount;
        system.SpawnBatch(count, first, last);
    }
}
