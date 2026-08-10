// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Audio.Effects;

/// <summary>
/// Base class for effects in an <see cref="AudioSource"/>'s processing chain.
/// </summary>
/// <remarks>
/// Parameters belong in serialized fields so the effect can be authored in the inspector and saved
/// with the scene. Anything derived from the audio format belongs in <see cref="OnInitialize"/>,
/// which runs once the sample rate and channel count are known, rather than in a constructor that
/// makes the caller pass in values the engine already has.
///
/// <see cref="OnProcess"/> runs on the audio thread. It must not allocate, block, or take a lock: a
/// garbage collection or a contended lock there is an audible dropout, not a slow frame.
/// </remarks>
public abstract class AudioEffect
{
    [SerializeField, Tooltip("Pass audio through untouched, without taking the effect out of the chain.")]
    private bool _bypass;

    /// <summary>
    /// Passes audio through untouched while leaving the effect in the chain. Takes effect on the next
    /// block, from any thread.
    /// </summary>
    /// <remarks>
    /// Tested per block rather than filtered out when the chain is built, so toggling this from
    /// gameplay works without anything having to republish the chain afterwards. The audio thread may
    /// read a value one block stale, which is inaudible, and the alternative was a toggle that only
    /// worked from the inspector.
    /// </remarks>
    public bool Bypass
    {
        get => _bypass;
        set => _bypass = value;
    }

    /// <summary>Sample rate of the chain this effect is in. Valid from <see cref="OnInitialize"/> onward.</summary>
    protected int SampleRate { get; private set; }

    /// <summary>Channel count of the chain this effect is in. Valid from <see cref="OnInitialize"/> onward.</summary>
    protected int Channels { get; private set; }

    /// <summary>True once <see cref="OnInitialize"/> has run and the DSP state exists.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Binds the effect to an audio format and builds its DSP state. Called when the effect joins a
    /// source and again if the format changes, so it has to be safe to call more than once.
    /// </summary>
    internal void Initialize(int sampleRate, int channels)
    {
        SampleRate = Math.Max(1, sampleRate);
        Channels = Math.Max(1, channels);
        OnInitialize();
        IsInitialized = true;
    }

    /// <summary>Build DSP state sized to <see cref="SampleRate"/> and <see cref="Channels"/> here.</summary>
    protected virtual void OnInitialize() { }

    /// <summary>
    /// Pushes the current parameter values into the DSP state. Called after the inspector or a script
    /// edits the serialized fields directly, since those never go through the property setters.
    /// </summary>
    public virtual void OnValidate() { }

    /// <summary>
    /// Runs the effect over one block unless it is bypassed, and reports whether it did. Chains call
    /// this rather than <see cref="OnProcess"/>, so bypassing is honoured the same way everywhere.
    /// </summary>
    public bool Process(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
    {
        if (_bypass)
            return false;

        OnProcess(framesIn, frameCountIn, framesOut, ref frameCountOut, channels);
        return true;
    }

    /// <summary>
    /// Processes one block on the audio thread. Leaving <paramref name="framesOut"/> untouched passes
    /// the previous stage's output through unchanged.
    /// </summary>
    protected abstract void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels);

    /// <summary>Called when the effect is removed from its source, or the source is destroyed.</summary>
    public virtual void OnDestroy() { }
}
