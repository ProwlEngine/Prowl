// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Audio.Effects;

public sealed class FilterEffect: IAudioEffect
{
    private Filter filter;

    public FilterType Type
    {
        get
        {
            return filter.Type;
        }
    }
    
    public float Frequency
    {
        get
        {
            return filter.Frequency;
        }
        set
        {
            filter.Frequency = value;
        }
    }

    public float Q
    {
        get
        {
            return filter.Q;
        }
        set
        {
            filter.Q = value;
        }
    }

    public float GainDB
    {
        get
        {
            return filter.GainDB;
        }
        set
        {
            filter.GainDB = value;
        }
    }

    public FilterEffect(FilterType type, float frequency, float q, float gainDB)
    {
        filter = new Filter(type, frequency, q, gainDB, AudioContext.SampleRate);
    }

    public void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
    {
        filter.Process(framesIn, framesOut, frameCountIn, (int)channels);
		}

    public void OnDestroy() { }
	}
