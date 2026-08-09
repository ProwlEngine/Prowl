// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Audio.Effects;

/// <summary>Biquad filter: lowpass, highpass, bandpass, shelves, peak and notch.</summary>
public sealed class FilterEffect : AudioEffect
{
    [SerializeField]
    private FilterType _type = FilterType.Lowpass;
    [SerializeField, Tooltip("Cutoff or centre frequency in hertz.")]
    private float _frequency = 1000.0f;
    [SerializeField, Tooltip("Resonance. Higher is a narrower, more emphasised peak.")]
    private float _q = 0.707f;
    [SerializeField, Tooltip("Shelf or peak gain in decibels. Only the shelf and peak types use it.")]
    private float _gainDB = 0.0f;

    private Filter _filter;

    public FilterType Type
    {
        get => _type;
        set
        {
            _type = value;
            // The coefficient function is chosen per type, so a type change is a rebuild.
            Rebuild();
        }
    }

    public float Frequency
    {
        get => _frequency;
        set
        {
            _frequency = value;
            if (_filter != null)
            {
                _filter.Frequency = value;
                _frequency = _filter.Frequency;
            }
        }
    }

    public float Q
    {
        get => _q;
        set
        {
            _q = value;
            if (_filter != null)
            {
                _filter.Q = value;
                _q = _filter.Q;
            }
        }
    }

    public float GainDB
    {
        get => _gainDB;
        set
        {
            _gainDB = value;
            if (_filter != null)
                _filter.GainDB = value;
        }
    }

    protected override void OnInitialize() => Rebuild();

    public override void OnValidate() => Rebuild();

    private void Rebuild()
    {
        // Not bound to a chain yet, so there is no format to size the filter against.
        if (SampleRate <= 0)
            return;

        _filter = new Filter(_type, _frequency, _q, _gainDB, SampleRate, Channels);

        // The filter clamps what it was given, so mirror the values it settled on back.
        _frequency = _filter.Frequency;
        _q = _filter.Q;
    }

    public override void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
    {
        if (_filter == null)
            return;

        _filter.Process(framesIn, framesOut, frameCountIn, (int)channels);
    }
}
