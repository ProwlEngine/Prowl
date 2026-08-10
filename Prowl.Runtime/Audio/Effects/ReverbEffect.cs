// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Audio.Effects;

/// <summary>Freeverb style reverb. Supports mono and stereo chains only.</summary>
public sealed class ReverbEffect : AudioEffect
{
    [SerializeField, Range(0f, 1f), Tooltip("Apparent size of the space.")]
    private float _roomSize = 0.5f;
    [SerializeField, Range(0f, 1f), Tooltip("How quickly high frequencies are absorbed.")]
    private float _damping = 0.25f;
    [SerializeField, Range(0f, 1f), Tooltip("Level of the reverberated signal.")]
    private float _wet = 1.0f / 3.0f;
    [SerializeField, Range(0f, 1f), Tooltip("Level of the untouched signal.")]
    private float _dry = 0.0f;
    [SerializeField, Range(0f, 1f), Tooltip("Stereo spread of the reverb tail.")]
    private float _width = 1.0f;
    [SerializeField, Tooltip("Widens or narrows the stereo image going in. 0 sums it to mono.")]
    private float _inputWidth = 0.0f;
    [SerializeField, Tooltip("Holds the tail forever instead of letting it decay.")]
    private bool _freeze = false;

    private Reverb _reverb;

    public float RoomSize
    {
        get => _roomSize;
        set { _roomSize = value; if (_reverb != null) _reverb.RoomSize = value; }
    }

    public float Damping
    {
        get => _damping;
        set { _damping = value; if (_reverb != null) _reverb.Damping = value; }
    }

    public float Wet
    {
        get => _wet;
        set { _wet = value; if (_reverb != null) _reverb.Wet = value; }
    }

    public float Dry
    {
        get => _dry;
        set { _dry = value; if (_reverb != null) _reverb.Dry = value; }
    }

    public float Width
    {
        get => _width;
        set { _width = value; if (_reverb != null) _reverb.Width = value; }
    }

    public float InputWidth
    {
        get => _inputWidth;
        set { _inputWidth = value; if (_reverb != null) _reverb.InputWidth = value; }
    }

    /// <summary>Holds the tail forever instead of letting it decay.</summary>
    public bool Freeze
    {
        get => _freeze;
        set { _freeze = value; if (_reverb != null) _reverb.Mode = value ? 1.0f : 0.0f; }
    }

    /// <summary>How long the tail takes to fall below audibility. Zero while frozen.</summary>
    public UInt64 DecayTimeInFrames => _reverb?.DecayTimeInFrames ?? 0;

    protected override void OnInitialize()
    {
        _reverb = null;

        // The comb and allpass tunings are only defined for one or two channels, and the buffer
        // scaling only holds over a bounded range of rates.
        if (Channels is not (1 or 2) || SampleRate < 22050 || SampleRate > 176400)
        {
            Debug.LogWarningOnce("Audio.ReverbFormat",
                $"ReverbEffect supports 1 or 2 channels at 22050 to 176400 Hz. This chain is {Channels} channels at {SampleRate} Hz, so the effect passes audio through untouched.");
            return;
        }

        _reverb = new Reverb((UInt32)SampleRate, (UInt32)Channels);
        OnValidate();
    }

    public override void OnValidate()
    {
        if (_reverb == null)
            return;

        _reverb.RoomSize = _roomSize;
        _reverb.Damping = _damping;
        _reverb.Wet = _wet;
        _reverb.Dry = _dry;
        _reverb.Width = _width;
        _reverb.InputWidth = _inputWidth;
        _reverb.Mode = _freeze ? 1.0f : 0.0f;
    }

    protected override void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
    {
        if (_reverb == null)
            return;

        UInt32 available = (UInt32)(Math.Min(framesIn.Length, framesOut.Length) / (int)Math.Max(1, channels));
        _reverb.Process(framesIn, framesOut, Math.Min(frameCountIn, available));
    }
}
