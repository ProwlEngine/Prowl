// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

using Prowl.Echo;
using Prowl.Runtime.Audio.Native;
using Prowl.Vector;

namespace Prowl.Runtime.Audio.Effects;

/// <summary>Fixed delay line with optional feedback, for slapback and echo.</summary>
public sealed class DelayEffect : AudioEffect
{
    [SerializeField, Tooltip("Delay length in seconds.")]
    private float _delaySeconds = 0.25f;
    [SerializeField, Range(0f, 1f), Tooltip("How much of each repeat feeds the next one. 0 is a single repeat.")]
    private float _decay = 0.0f;
    [SerializeField, Range(0f, 1f), Tooltip("Level of the delayed signal.")]
    private float _wet = 1.0f;
    [SerializeField, Range(0f, 1f), Tooltip("Level of the signal written into the delay line.")]
    private float _dry = 1.0f;

    private Int32 channels = 1;
    private bool delayStart;       /* Set to true to delay the start of the output; false otherwise. */
    private Int32 cursor;               /* Feedback is written to this cursor. Always equal or in front of the read cursor. */
    private Int32 bufferSizeInFrames = 1;
    private float[] buffer = [];

    public float Wet
    {
        get => _wet;
        set => _wet = value;
    }

    public float Dry
    {
        get => _dry;
        set => _dry = value;
    }

    public float Decay
    {
        get => _decay;
        set
        {
            _decay = value;
            delayStart = _decay == 0;
        }
    }

    /// <summary>Delay length in seconds. Rounds up to whole frames, with one frame as the floor.</summary>
    public float DelayInSeconds
    {
        get => _delaySeconds;
        set
        {
            _delaySeconds = value;
            Resize();
        }
    }

    /// <summary>The delay length the buffer was actually sized to.</summary>
    public UInt32 DelayInFrames => (UInt32)bufferSizeInFrames;

    protected override void OnInitialize()
    {
        channels = Math.Max(1, Channels);
        delayStart = _decay == 0;
        Resize();
    }

    public override void OnValidate()
    {
        delayStart = _decay == 0;
        Resize();
    }

    /// <summary>
    /// Sizes the delay line. One frame is the floor: OnProcess takes the cursor modulo this, and a
    /// zero length is a divide by zero on the audio thread over a zero length buffer.
    /// </summary>
    private void Resize()
    {
        int frames = (Int32)Maths.Ceiling(_delaySeconds * Math.Max(1, SampleRate));
        bufferSizeInFrames = Maths.Max(1, frames);

        int required = (Int32)GetNextPowerOfTwo((UInt32)(bufferSizeInFrames * channels));

        if (buffer.Length < required)
            buffer = new float[required];

        cursor %= bufferSizeInFrames;
    }

    protected override unsafe void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
    {
        // The buffer is laid out for the channel count this effect was initialized with, so a chain
        // that disagrees would step the write cursor off the end of it.
        if (buffer.Length == 0 || channels != this.channels)
            return;

        int frames = (int)frameCountIn;
        int available = Math.Min(framesIn.Length, framesOut.Length) / this.channels;

        if (frames > available)
            frames = available;

        float* pFramesOutF32 = (float*)framesOut.Pointer;
        float* pFramesInF32 = (float*)framesIn.Pointer;

        for (int iFrame = 0; iFrame < frames; iFrame += 1)
        {
            for (int iChannel = 0; iChannel < this.channels; iChannel += 1)
            {
                Int32 iBuffer = (cursor * this.channels) + iChannel;

                if (delayStart)
                {
                    /* Delayed start. */

                    /* Read */
                    pFramesOutF32[iChannel] = buffer[iBuffer] * _wet;

                    /* Feedback */
                    buffer[iBuffer] = (buffer[iBuffer] * _decay) + (pFramesInF32[iChannel] * _dry);
                }
                else
                {
                    /* Immediate start */

                    /* Feedback */
                    buffer[iBuffer] = (buffer[iBuffer] * _decay) + (pFramesInF32[iChannel] * _dry);

                    /* Read */
                    pFramesOutF32[iChannel] = buffer[iBuffer] * _wet;
                }
            }

            cursor = (cursor + 1) % bufferSizeInFrames;

            pFramesOutF32 += this.channels;
            pFramesInF32 += this.channels;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private UInt32 GetNextPowerOfTwo(UInt32 value)
    {
        if (value <= 1)
            return 1;

        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        value++;
        return value;
    }
}
