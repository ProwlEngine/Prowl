// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Audio.Native;
using Prowl.Vector;

namespace Prowl.Runtime.Audio.Effects;

/// <summary>Fixed delay line with optional feedback, for slapback and echo.</summary>
/// <remarks>
/// An insert, so the signal that goes in comes back out with the repeats added to it. It used to
/// replace the input with the contents of the delay line instead, which meant dropping a delay on a
/// source removed the sound and left only a copy of it arriving a quarter of a second later.
/// </remarks>
public sealed class DelayEffect : AudioEffect
{
    [SerializeField, Tooltip("Delay length in seconds.")]
    private float _delaySeconds = 0.25f;
    [SerializeField, Range(0f, 1f), Tooltip("How much of each repeat feeds the next one. 0 is a single repeat.")]
    private float _decay = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Balance between the untouched signal at 0 and the repeats alone at 1.")]
    private float _mix = 0.5f;

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
            Buffer = new float[FrameCount * Channels];
        }
    }

    private volatile DelayLine _line = new(1, 1);

    /// <summary>
    /// Balance between the untouched signal and the repeats. 0 passes audio through, 1 is the
    /// repeats on their own.
    /// </summary>
    public float Mix
    {
        get => _mix;
        set => _mix = Maths.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>
    /// How much of each repeat feeds the next one. 0 gives a single repeat, and it stops short of 1,
    /// where the line would return everything it was given forever and build without limit.
    /// </summary>
    public float Decay
    {
        get => _decay;
        set => _decay = Maths.Clamp(value, 0.0f, 0.99f);
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

    protected override void OnInitialize() => Resize();

    public override void OnValidate()
    {
        // The inspector writes the fields directly, so this is where a value it wrote is checked.
        _mix = Maths.Clamp(_mix, 0.0f, 1.0f);
        _decay = Maths.Clamp(_decay, 0.0f, 0.99f);

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
        float wet = _mix;
        float dry = 1.0f - _mix;
        float decay = _decay;

        for (int iFrame = 0; iFrame < frames; iFrame += 1)
        {
            for (int iChannel = 0; iChannel < line.Channels; iChannel += 1)
            {
                Int32 iBuffer = (cursor * line.Channels) + iChannel;

                // Read before write, always. The cursor is a whole delay length behind what is about
                // to be written to it, so this is the sample from that long ago. Writing first turned
                // the effect into something else, and it was the feedback amount that decided which,
                // because a delay with no feedback took one branch and a delay with feedback the other.
                float delayed = buffer[iBuffer];
                float input = pFramesInF32[iChannel];

                buffer[iBuffer] = input + (delayed * decay);
                pFramesOutF32[iChannel] = (input * dry) + (delayed * wet);
            }

            cursor = (cursor + 1) % line.FrameCount;

            pFramesOutF32 += line.Channels;
            pFramesInF32 += line.Channels;
        }

        line.Cursor = cursor;
    }
}
