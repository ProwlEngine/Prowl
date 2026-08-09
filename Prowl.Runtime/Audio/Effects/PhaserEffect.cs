// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Audio.Effects;

/// <summary>Six stage phase shifter with a swept notch.</summary>
public sealed class PhaserEffect : AudioEffect
{
    [SerializeField, Tooltip("How much of the shifted signal is mixed back in.")]
    private float _depth = 1.0f;
    [SerializeField, Range(0f, 1f), Tooltip("How much output is fed back into the input.")]
    private float _feedback = 0.7f;
    [SerializeField, Tooltip("Lowest frequency the sweep reaches, in hertz.")]
    private float _minimum = 440.0f;
    [SerializeField, Tooltip("Highest frequency the sweep reaches, in hertz.")]
    private float _maximum = 1600.0f;
    [SerializeField, Tooltip("Sweep speed in hertz.")]
    private float _rate = 5.0f;

    // A phaser carries allpass state and an LFO. One instance shared by every channel smears them
    // together and advances the sweep once per sample per channel.
    private Phaser[] _phasers = [];

    public float Depth
    {
        get => _depth;
        set { _depth = value; foreach (Phaser phaser in _phasers) phaser.Depth = value; }
    }

    public float Feedback
    {
        get => _feedback;
        set { _feedback = value; foreach (Phaser phaser in _phasers) phaser.Feedback = value; }
    }

    public float Minimum
    {
        get => _minimum;
        set { _minimum = value; foreach (Phaser phaser in _phasers) phaser.Minimum = value; }
    }

    public float Maximum
    {
        get => _maximum;
        set { _maximum = value; foreach (Phaser phaser in _phasers) phaser.Maximum = value; }
    }

    public float Rate
    {
        get => _rate;
        set { _rate = value; foreach (Phaser phaser in _phasers) phaser.Rate = value; }
    }

    protected override void OnInitialize() => Allocate(Channels);

    public override void OnValidate()
    {
        foreach (Phaser phaser in _phasers)
            Configure(phaser);
    }

    public override void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
    {
        if (channels == 0)
            return;

        if (channels > _phasers.Length)
            Allocate((int)channels);

        int frames = (int)frameCountIn;
        int available = Math.Min(framesIn.Length, framesOut.Length) / (int)channels;

        if (frames > available)
            frames = available;

        for (int frame = 0; frame < frames; frame++)
        {
            int start = frame * (int)channels;

            for (int channel = 0; channel < channels; channel++)
            {
                int i = start + channel;
                framesOut[i] = _phasers[channel].Process(framesIn[i]);
            }
        }
    }

    private void Allocate(int channels)
    {
        _phasers = new Phaser[Math.Max(1, channels)];

        for (int i = 0; i < _phasers.Length; i++)
        {
            _phasers[i] = new Phaser();
            Configure(_phasers[i]);
        }
    }

    private void Configure(Phaser phaser)
    {
        phaser.Depth = _depth;
        phaser.Feedback = _feedback;
        phaser.Minimum = _minimum;
        phaser.Maximum = _maximum;
        phaser.Rate = _rate;
        // Last: its setter recomputes the sweep increment and the normalised sweep range.
        phaser.SampleRate = SampleRate > 0 ? SampleRate : AudioContext.SampleRate;
    }
}
