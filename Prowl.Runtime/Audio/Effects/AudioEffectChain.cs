// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Audio.Effects;

/// <summary>
/// Runs an ordered set of effects over one block of audio, on behalf of an <see cref="AudioSource"/> or
/// an <see cref="AudioMixerGroup"/>.
/// </summary>
/// <remarks>
/// The chain works in buffers it owns rather than passing the node graph's own input and output back
/// and forth. Doing that only worked while the two frame counts happened to be equal: the copy in one
/// direction needs the input to fit the output and the copy back needs the opposite, so any block where
/// the graph handed over different counts threw. It also meant writing into the upstream node's output
/// buffer, which is not ours to modify.
///
/// Everything here runs on the audio thread. It allocates only when the block size grows past what it
/// has already seen, and never blocks.
/// </remarks>
internal sealed class AudioEffectChain
{
    // Swapped as a whole array so the audio thread always walks a consistent set, with no lock.
    private volatile AudioEffect[] _chain = [];

    private float[] _scratchA = [];
    private float[] _scratchB = [];

    /// <summary>The effects that will run, in order.</summary>
    public AudioEffect[] Current => _chain;

    /// <summary>
    /// Republishes the chain from <paramref name="effects"/>, dropping empty entries. Bypassed effects
    /// stay in, because bypassing is tested per block so that toggling it needs no republish.
    /// </summary>
    public void Publish(List<AudioEffect> effects)
    {
        var chain = new List<AudioEffect>(effects.Count);

        foreach (AudioEffect effect in effects)
        {
            if (effect != null)
                chain.Add(effect);
        }

        _chain = chain.ToArray();
    }

    /// <summary>
    /// Runs every effect over one block and writes the result to <paramref name="framesOut"/>.
    /// Returns how many frames were written, which is what the caller should report to the graph.
    /// </summary>
    public unsafe uint Process(float* framesIn, uint frameCountIn, float* framesOut, uint frameCountOut, uint channels)
    {
        int channelCount = (int)Math.Max(1u, channels);
        int inSamples = (int)frameCountIn * channelCount;
        int outSamples = (int)frameCountOut * channelCount;

        AudioEffect[] chain = _chain;

        // Nothing to run, so the block passes through untouched.
        if (chain.Length == 0)
            return (uint)(Copy(framesIn, framesOut, inSamples, outSamples) / channelCount);

        int capacity = Math.Max(inSamples, outSamples);
        EnsureScratch(capacity);

        fixed (float* pA = _scratchA, pB = _scratchB)
        {
            Copy(framesIn, pA, inSamples, capacity);

            float* source = pA;
            float* destination = pB;

            uint countIn = frameCountIn;
            uint countOut = frameCountOut;

            for (int i = 0; i < chain.Length; i++)
            {
                var input = new NativeArray<float>(source, (int)countIn * channelCount);
                var output = new NativeArray<float>(destination, (int)countOut * channelCount);

                // A bypassed effect leaves both buffers as the previous stage left them, so there is
                // nothing to carry forward either.
                if (!chain[i].Process(input, countIn, output, ref countOut, channels))
                    continue;

                // An effect is allowed to change the count, but not past what the scratch can hold.
                if ((int)countOut * channelCount > capacity)
                    countOut = (uint)(capacity / channelCount);

                // Plain swap rather than a tuple: a pointer cannot be a type argument.
                float* previous = source;
                source = destination;
                destination = previous;

                countIn = countOut;
            }

            return (uint)(Copy(source, framesOut, (int)countIn * channelCount, outSamples) / channelCount);
        }
    }

    /// <summary>Copies as much as fits and returns how many samples that was.</summary>
    private static unsafe int Copy(float* source, float* destination, int sourceSamples, int destinationSamples)
    {
        int samples = Math.Min(sourceSamples, destinationSamples);

        if (samples > 0)
            Buffer.MemoryCopy(source, destination, (long)destinationSamples * sizeof(float), (long)samples * sizeof(float));

        return samples;
    }

    /// <summary>
    /// Grows the working buffers to hold a block. Only ever grows, so the allocation happens on the
    /// first block of a given size and never again.
    /// </summary>
    private void EnsureScratch(int capacity)
    {
        if (_scratchA.Length >= capacity)
            return;

        _scratchA = new float[capacity];
        _scratchB = new float[capacity];
    }
}
