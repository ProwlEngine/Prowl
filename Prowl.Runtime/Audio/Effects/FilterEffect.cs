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
            if (_filter != null)
                _filter.Type = value;
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

    /// <summary>Binding to a format is the one thing that needs a filter built from scratch.</summary>
    protected override void OnInitialize() => Rebuild();

    /// <summary>
    /// Pushes the serialized values into the live filter rather than replacing it. Replacing it threw
    /// away the delay state mid stream, so an unrelated inspector edit on the source, dragging its
    /// volume say, clicked once per frame of the drag.
    /// </summary>
    public override void OnValidate()
    {
        if (_filter == null)
        {
            Rebuild();
            return;
        }

        _filter.Type = _type;
        _filter.Frequency = _frequency;
        _filter.Q = _q;
        _filter.GainDB = _gainDB;

        MirrorClamps();
    }

    private void Rebuild()
    {
        // Not bound to a chain yet, so there is no format to size the filter against.
        if (SampleRate <= 0)
            return;

        _filter = new Filter(_type, _frequency, _q, _gainDB, SampleRate, Channels);
        MirrorClamps();
    }

    /// <summary>The filter clamps what it is given, so the serialized values follow what it settled on.</summary>
    private void MirrorClamps()
    {
        _frequency = _filter.Frequency;
        _q = _filter.Q;
    }

    protected override void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
    {
        if (_filter == null)
            return;

        _filter.Process(framesIn, framesOut, frameCountIn, (int)channels);
    }
}
