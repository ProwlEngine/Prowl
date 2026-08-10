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

    /// <summary>
    /// The buffer and the geometry that describes it, as one thing.
    /// </summary>
    /// <remarks>
    /// These three have to agree. Held as separate fields, resizing wrote the new frame count and then
    /// swapped the array, so a block landing between the two indexed the old, shorter buffer with the
    /// new, larger count and ran off the end of it. Swapping one reference instead means the audio
    /// thread reads a set that was never half updated.
    /// </remarks>
    private sealed class DelayLine
    {
        public readonly float[] Buffer;
        public readonly int FrameCount;
        public readonly int Channels;

        /// <summary>Where feedback is written. Only the audio thread touches it.</summary>
        public int Cursor;

        public DelayLine(int frameCount, int channels)
        {
            FrameCount = Math.Max(1, frameCount);
            Channels = Math.Max(1, channels);
            Buffer = new float[GetNextPowerOfTwo((UInt32)(FrameCount * Channels))];
        }
    }

    private volatile DelayLine _line = new(1, 1);
    private bool delayStart;       /* Set to true to delay the start of the output; false otherwise. */

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
    public UInt32 DelayInFrames => (UInt32)_line.FrameCount;

    protected override void OnInitialize()
    {
        delayStart = _decay == 0;
        Resize();
    }

    public override void OnValidate()
    {
        delayStart = _decay == 0;
        Resize();
    }

    /// <summary>
    /// Sizes the delay line, replacing it only when the geometry actually changes. One frame is the
    /// floor: OnProcess takes the cursor modulo this, and a zero length is a divide by zero over a
    /// zero length buffer.
    /// </summary>
    /// <remarks>
    /// The early return matters as much as the resize. OnValidate runs for every field on the owning
    /// source, so replacing the line unconditionally would empty the delay each time someone nudged
    /// an unrelated slider.
    /// </remarks>
    private void Resize()
    {
        int frames = Maths.Max(1, (Int32)Maths.Ceiling(_delaySeconds * Math.Max(1, SampleRate)));
        int channelCount = Math.Max(1, Channels);

        DelayLine current = _line;

        if (current.FrameCount == frames && current.Channels == channelCount)
            return;

        _line = new DelayLine(frames, channelCount);
    }

    protected override unsafe void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
    {
        // Read once. Everything below works against this one set, so a resize landing mid block takes
        // effect on the next one rather than halfway through this one.
        DelayLine line = _line;
        float[] buffer = line.Buffer;

        // The buffer is laid out for the channel count this effect was initialized with, so a chain
        // that disagrees would step the write cursor off the end of it.
        if (buffer.Length == 0 || channels != line.Channels)
            return;

        int frames = (int)frameCountIn;
        int available = Math.Min(framesIn.Length, framesOut.Length) / line.Channels;

        if (frames > available)
            frames = available;

        float* pFramesOutF32 = (float*)framesOut.Pointer;
        float* pFramesInF32 = (float*)framesIn.Pointer;

        int cursor = line.Cursor % line.FrameCount;

        for (int iFrame = 0; iFrame < frames; iFrame += 1)
        {
            for (int iChannel = 0; iChannel < line.Channels; iChannel += 1)
            {
                Int32 iBuffer = (cursor * line.Channels) + iChannel;

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

            cursor = (cursor + 1) % line.FrameCount;

            pFramesOutF32 += line.Channels;
            pFramesInF32 += line.Channels;
        }

        line.Cursor = cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UInt32 GetNextPowerOfTwo(UInt32 value)
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
