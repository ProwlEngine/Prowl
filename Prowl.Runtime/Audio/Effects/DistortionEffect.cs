// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

using Prowl.Echo;
using Prowl.Runtime.Audio.Native;
using Prowl.Vector;

namespace Prowl.Runtime.Audio.Effects;

/// <summary>Soft clipping waveshaper, blended against the untouched signal.</summary>
public sealed class DistortionEffect : AudioEffect
{
    [SerializeField, Tooltip("How hard the signal is pushed into the shaper.")]
    private float _drive = 1.0f;
    [SerializeField, Tooltip("Scales the drive. The two multiply.")]
    private float _range = 1.0f;
    [SerializeField, Range(0f, 1f), Tooltip("Crossfade between the untouched signal at 0 and the shaped one at 1.")]
    private float _blend = 1.0f;
    [SerializeField, Tooltip("Output level applied after the blend.")]
    private float _volume = 1.0f;

    public float Drive
    {
        get => _drive;
        set => _drive = value;
    }

    public float Range
    {
        get => _range;
        set => _range = value;
    }

    public float Blend
    {
        get => _blend;
        set => _blend = value;
    }

    public float Volume
    {
        get => _volume;
        set => _volume = value;
    }

    protected override void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
    {
        int count = (int)(frameCountIn * channels);

        if (count > framesIn.Length) count = framesIn.Length;
        if (count > framesOut.Length) count = framesOut.Length;

        for (int i = 0; i < count; i++)
        {
            framesOut[i] = Distort(framesIn[i], _drive, _range, _blend, _volume);
        }
    }

    // The blend crossfades the shaped signal against the untouched one, so the dry end of it has to
    // come out at the level it went in.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float Distort(float x, float drive, float range, float blend, float volume)
    {
        float xClean = x;
        x *= drive * range;
        return ((((2.0f / Maths.PI) * Maths.Atan(x)) * blend) + (xClean * (1.0f - blend))) * volume;
    }
}
