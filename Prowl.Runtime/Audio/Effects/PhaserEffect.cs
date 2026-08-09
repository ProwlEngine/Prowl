// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Audio.Effects;

public sealed class PhaserEffect : IAudioEffect
{
    // A phaser carries allpass state and an LFO. One instance shared by every channel smears them
    // together and advances the sweep once per sample per channel, so stereo swept at double rate.
    private Phaser[] _phasers;
    private readonly float _sampleRate;

    private float _depth = 1.0f;
    private float _feedback = 0.7f;
    private float _minimum = 440.0f;
    private float _maximum = 1600.0f;
    private float _rate = 5.0f;

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

    public PhaserEffect(UInt32 sampleRate)
    {
        _sampleRate = sampleRate;
        AllocatePhasers(AudioContext.Channels);
    }

    public void OnProcess(NativeArray<float> framesIn, uint frameCountIn, NativeArray<float> framesOut, ref uint frameCountOut, uint channels)
    {
        if (channels == 0)
            return;

        if (channels > _phasers.Length)
            AllocatePhasers((int)channels);

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

    private void AllocatePhasers(int channels)
    {
        _phasers = new Phaser[Math.Max(1, channels)];

        for (int i = 0; i < _phasers.Length; i++)
        {
            _phasers[i] = new Phaser
            {
                Depth = _depth,
                Feedback = _feedback,
                Minimum = _minimum,
                Maximum = _maximum,
                Rate = _rate,
                SampleRate = _sampleRate,
            };
        }
    }

    public void OnDestroy() { }
}
