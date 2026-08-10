// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Audio.Effects;
using Prowl.Runtime.Audio.Native;
using Prowl.Vector;

namespace Prowl.Runtime.Audio;

/// <summary>
/// A named bus that audio is routed through on its way to the device, carrying a volume and a mute
/// for everything feeding it.
/// </summary>
/// <remarks>
/// Groups live inside an <see cref="AudioMixer"/> and are referenced by an
/// <see cref="AudioSource.OutputGroup"/>. Each one owns a native sound group whose output is attached
/// to its parent's, so a volume set on a parent scales every descendant. The native side is built on
/// demand and thrown away when the device closes, so the asset stays pure data.
/// </remarks>
public sealed class AudioMixerGroup : EngineObject
{
    /// <summary>Quietest volume, treated as silence rather than a very small gain.</summary>
    public const float MinVolumeDB = -80.0f;
    public const float MaxVolumeDB = 20.0f;

    [SerializeField, Tooltip("Name shown when picking this group as a source's output.")]
    private string _groupName = "Group";

    [SerializeField, Range(MinVolumeDB, MaxVolumeDB), Tooltip("Level in decibels. 0 passes audio through unchanged, -80 is silence.")]
    private float _volumeDB = 0.0f;

    [SerializeField, Tooltip("Silences this group and everything routed into it.")]
    private bool _mute = false;

    // Index into the owning mixer's group list, -1 for the root. An index rather than a reference so
    // the asset stays a flat list with no object graph to resolve.
    [SerializeField, HideInInspector]
    private int _parentIndex = -1;

    // Belongs to the group rather than to its position, because every saved reference to a group is
    // keyed on it. A positional key would shift every later group onto its neighbour's identity the
    // first time one is inserted or removed, silently repointing sources at the wrong bus.
    [SerializeField, HideInInspector]
    private string _identity = Guid.NewGuid().ToString("N");

    // Sub-assets ship with their own cache file, so a group referenced by an AudioSource can be loaded
    // on its own, with no mixer around it to say what it feeds into. This is what lets it find its way
    // back to its siblings, and it is also the dependency edge that makes the mixer ship at all.
    [SerializeField, HideInInspector]
    private AssetRef<AudioMixer> _owningMixer;

    /// <summary>Stable key this group is referenced by, independent of its name and position.</summary>
    public string Identity => _identity;

    internal void SetOwningMixer(Guid mixerId) => _owningMixer = new AssetRef<AudioMixer>(mixerId);

    internal void EnsureIdentity(int index)
    {
        if (string.IsNullOrEmpty(_identity))
            _identity = $"group{index}";
    }

    [SerializeField, Tooltip("Applied to everything routed into this group, in order.")]
    private List<AudioEffect> _effects = [];

    [SerializeIgnore]
    private AudioMixer _mixer;

    [SerializeIgnore]
    private ma_sound_group_ptr _nativeGroup;

    [SerializeIgnore]
    private int _builtForDevice = -1;

    // Effect hosting. Mirrors AudioSource: the bus feeds a node that runs the managed chain, and that
    // node is what feeds the parent.
    [SerializeIgnore]
    private ma_effect_node_ptr _effectNode;

    [SerializeIgnore]
    private bool _effectNodeReady;

    [SerializeIgnore]
    private ma_effect_node_process_proc _onEffectNodeProcess;

    [SerializeIgnore]
    private volatile AudioEffect[] _effectChain = [];

    [SerializeIgnore]
    private bool _effectProcessFailed;

    public string GroupName
    {
        get => _groupName;
        set => _groupName = value;
    }

    /// <summary>Level in decibels. 0 leaves audio unchanged, <see cref="MinVolumeDB"/> is silence.</summary>
    public float VolumeDB
    {
        get => _volumeDB;
        set
        {
            _volumeDB = Maths.Clamp(value, MinVolumeDB, MaxVolumeDB);
            ApplyVolume();
        }
    }

    /// <summary>The same level as a 0 to 1 linear gain, for volume sliders.</summary>
    public float Volume
    {
        get => DecibelsToLinear(_volumeDB);
        set => VolumeDB = LinearToDecibels(value);
    }

    public bool Mute
    {
        get => _mute;
        set
        {
            _mute = value;
            ApplyVolume();
        }
    }

    /// <summary>The group this one feeds into, or null if it is the mixer's root.</summary>
    public AudioMixerGroup Parent
    {
        get
        {
            AudioMixer owner = Mixer;
            return owner.IsValid() && _parentIndex >= 0 ? owner.GetGroupAt(_parentIndex) : null;
        }
    }

    /// <summary>
    /// The mixer this group belongs to. Resolved from the owning asset when this group was loaded on
    /// its own rather than as part of its mixer.
    /// </summary>
    public AudioMixer Mixer
    {
        get
        {
            if (_mixer.IsValid())
                return _mixer;

            _mixer = _owningMixer.Res;
            return _mixer;
        }
    }

    internal int ParentIndex
    {
        get => _parentIndex;
        set => _parentIndex = value;
    }

    internal void Bind(AudioMixer mixer) => _mixer = mixer;

    /// <summary>
    /// The native node audio should be attached to, built on first use. IntPtr.Zero when there is no
    /// device, in which case callers route to the engine endpoint as though there were no mixer.
    /// </summary>
    internal IntPtr NativeNode
    {
        get
        {
            if (!AudioContext.IsInitialized)
            {
                _nativeGroup.pointer = IntPtr.Zero;
                _builtForDevice = -1;
                return IntPtr.Zero;
            }

            // A device restart invalidates every native object, so anything built against an older
            // one has to be rebuilt rather than reused as a dangling pointer.
            if (_builtForDevice != AudioContext.DeviceGeneration)
                Build();

            return _nativeGroup.pointer;
        }
    }

    private void Build()
    {
        _builtForDevice = AudioContext.DeviceGeneration;
        _nativeGroup.pointer = MiniAudioExNative.ma_ex_sound_group_init(AudioContext.NativeContext);

        if (_nativeGroup.pointer == IntPtr.Zero)
        {
            Debug.LogError($"Audio mixer group '{_groupName}' could not be created, audio routed to it will not be heard.");
            return;
        }

        // Groups are spatialized by default, which would attenuate a bus by its distance from the
        // listener. A bus is a mix stage, not a thing in the world.
        MiniAudioNative.ma_sound_group_set_spatialization_enabled(_nativeGroup, 0);

        var engine = new ma_engine_ptr(MiniAudioExNative.ma_ex_context_get_engine(AudioContext.NativeContext));

        BuildEffectNode(engine);
        RefreshEffects();

        // Whatever runs last in this bus is what feeds the parent: the effect node if it came up, the
        // bus itself otherwise.
        IntPtr tail = _effectNodeReady ? _effectNode.pointer : _nativeGroup.pointer;

        AudioMixerGroup parent = Parent;
        IntPtr parentNode = parent != null ? parent.NativeNode : IntPtr.Zero;

        if (parentNode != IntPtr.Zero)
            MiniAudioNative.ma_node_attach_output_bus(new ma_node_ptr(tail), 0, new ma_node_ptr(parentNode), 0);
        else
            MiniAudioNative.ma_node_attach_output_bus(new ma_node_ptr(tail), 0, MiniAudioNative.ma_engine_get_endpoint(engine), 0);

        ApplyVolume();
    }

    private void BuildEffectNode(ma_engine_ptr engine)
    {
        _onEffectNodeProcess = OnEffectProcess;

        ma_effect_node_config config = MiniAudioNative.ma_effect_node_config_init(
            (UInt32)AudioContext.Channels,
            (UInt32)AudioContext.SampleRate,
            _onEffectNodeProcess,
            IntPtr.Zero);

        _effectNode = new ma_effect_node_ptr(true);
        _effectNodeReady = MiniAudioNative.ma_effect_node_init(MiniAudioNative.ma_engine_get_node_graph(engine), ref config, _effectNode) == ma_result.success;

        if (!_effectNodeReady)
        {
            _effectNode.Free();
            return;
        }

        MiniAudioNative.ma_node_attach_output_bus(new ma_node_ptr(_nativeGroup.pointer), 0, new ma_node_ptr(_effectNode.pointer), 0);
    }

    private void ApplyVolume()
    {
        if (_nativeGroup.pointer == IntPtr.Zero || _builtForDevice != AudioContext.DeviceGeneration)
            return;

        MiniAudioNative.ma_sound_group_set_volume(_nativeGroup, _mute ? 0.0f : DecibelsToLinear(_volumeDB));
    }

    /// <summary>Effects applied to everything routed into this group, in order.</summary>
    public IReadOnlyList<AudioEffect> Effects => _effects;

    /// <summary>Adds an effect to this bus. The group owns it and destroys it when it is removed.</summary>
    public void AddEffect(AudioEffect effect)
    {
        if (effect == null) return;

        _effects.Add(effect);
        effect.Initialize(AudioContext.SampleRate, AudioContext.Channels);
        PublishEffectChain();
    }

    /// <summary>Removes an effect from this bus and destroys it.</summary>
    public void RemoveEffect(AudioEffect effect)
    {
        if (effect == null || !_effects.Remove(effect)) return;

        PublishEffectChain();
        effect.OnDestroy();
    }

    /// <summary>
    /// Binds every effect to the current audio format and republishes the chain the audio thread reads.
    /// Call after editing <see cref="Effects"/> in place.
    /// </summary>
    public void RefreshEffects()
    {
        foreach (AudioEffect effect in _effects)
        {
            if (effect == null) continue;

            if (!effect.IsInitialized)
                effect.Initialize(AudioContext.SampleRate, AudioContext.Channels);
            else
                effect.OnValidate();
        }

        PublishEffectChain();
    }

    private void PublishEffectChain()
    {
        var chain = new List<AudioEffect>(_effects.Count);

        foreach (AudioEffect effect in _effects)
        {
            if (effect != null && !effect.Bypass)
                chain.Add(effect);
        }

        _effectChain = chain.ToArray();
    }

    // Native calls this on the audio thread. An exception leaving it is undefined behaviour, so a
    // failing effect costs this bus its audio and one log line. Mirrors AudioSource.OnEffectProcess,
    // without the per-source output buffer and Process event.
    private unsafe void OnEffectProcess(ma_node_ptr pNode, IntPtr ppFramesIn, IntPtr pFrameCountIn, IntPtr ppFramesOut, IntPtr pFrameCountOut)
    {
        if (pNode.pointer == IntPtr.Zero)
            return;

        ma_effect_node* pEffectNode = (ma_effect_node*)pNode.pointer;
        UInt32* frameCountIn = (UInt32*)pFrameCountIn;
        UInt32* frameCountOut = (UInt32*)pFrameCountOut;
        UInt32 channels = pEffectNode->config.channels;

        float** framesIn = (float**)ppFramesIn;
        float** framesOut = (float**)ppFramesOut;

        try
        {
            var bufferIn = new NativeArray<float>(framesIn[0], (int)(*frameCountIn * channels));
            var bufferOut = new NativeArray<float>(framesOut[0], (int)(*frameCountOut * channels));

            // Pass through when the bus has no effects, otherwise it would emit silence.
            bufferIn.CopyTo(bufferOut);

            UInt32 countIn = *frameCountIn;
            UInt32 countOut = *frameCountOut;

            AudioEffect[] chain = _effectChain;

            for (int i = 0; i < chain.Length; i++)
            {
                chain[i].OnProcess(bufferIn, countIn, bufferOut, ref countOut, channels);
                bufferOut.CopyTo(bufferIn);
                countIn = countOut;
            }

            *frameCountOut = countOut;
        }
        catch (Exception ex)
        {
            if (!_effectProcessFailed)
            {
                _effectProcessFailed = true;
                Debug.LogError($"Audio mixer group '{_groupName}' effect processing threw and is now producing silence: {ex}");
            }

            if (framesOut != null && framesOut[0] != null)
                new Span<float>(framesOut[0], (int)(*frameCountOut * channels)).Clear();
        }
    }

    internal void ReleaseNative()
    {
        if (_builtForDevice == AudioContext.DeviceGeneration)
        {
            if (_effectNodeReady)
                MiniAudioNative.ma_effect_node_uninit(_effectNode);

            if (_nativeGroup.pointer != IntPtr.Zero)
                MiniAudioExNative.ma_ex_sound_group_uninit(_nativeGroup.pointer);
        }

        _effectNode.Free();
        _effectNodeReady = false;
        _nativeGroup.pointer = IntPtr.Zero;
        _builtForDevice = -1;
    }

    public override void OnValidate()
    {
        ApplyVolume();
        RefreshEffects();
    }

    protected override void OnDispose() => ReleaseNative();

    /// <summary>Converts a decibel level to a linear gain, with <see cref="MinVolumeDB"/> as silence.</summary>
    public static float DecibelsToLinear(float decibels)
        => decibels <= MinVolumeDB ? 0.0f : Maths.Pow(10.0f, decibels / 20.0f);

    /// <summary>Converts a linear gain to a decibel level, with 0 and below as <see cref="MinVolumeDB"/>.</summary>
    public static float LinearToDecibels(float linear)
        => linear <= 0.0f ? MinVolumeDB : Maths.Clamp(20.0f * MathF.Log10(linear), MinVolumeDB, MaxVolumeDB);
}

/// <summary>
/// An asset describing how audio is routed and mixed: a tree of <see cref="AudioMixerGroup"/> buses
/// that sources feed into, so volumes can be set per category rather than per source.
/// </summary>
[CreateAssetMenu("Audio Mixer", Extension = ".audiomixer", Order = 1100)]
public sealed class AudioMixer : EngineObject, ISerializationCallbackReceiver
{
    [SerializeField, HideInInspector]
    private List<AudioMixerGroup> _groups = [];

    /// <summary>Every group in the mixer. The first is the root that all others eventually feed into.</summary>
    public IReadOnlyList<AudioMixerGroup> Groups
    {
        get
        {
            EnsureBound();
            return _groups;
        }
    }

    /// <summary>The root group everything in this mixer feeds into.</summary>
    public AudioMixerGroup Master => Groups.Count > 0 ? _groups[0] : null;

    public AudioMixer()
    {
        // A mixer with no root has nothing for a source to point at, so a new asset starts usable.
        _groups.Add(new AudioMixerGroup { GroupName = "Master", Name = "Master" });
        EnsureBound();
    }

    /// <summary>Finds a group by name, or null. Names are compared exactly.</summary>
    public AudioMixerGroup FindGroup(string groupName)
    {
        EnsureBound();

        foreach (AudioMixerGroup group in _groups)
        {
            if (group.IsValid() && group.GroupName == groupName)
                return group;
        }

        return null;
    }

    /// <summary>
    /// Adds a group feeding into <paramref name="parent"/>, or into the root when that is null.
    /// </summary>
    public AudioMixerGroup AddGroup(string groupName, AudioMixerGroup parent = null)
    {
        EnsureBound();

        int parentIndex = parent != null ? _groups.IndexOf(parent) : (_groups.Count > 0 ? 0 : -1);

        var group = new AudioMixerGroup { GroupName = groupName, Name = groupName };
        group.ParentIndex = parentIndex;
        group.Bind(this);
        _groups.Add(group);
        return group;
    }

    /// <summary>Removes a group, re-pointing anything that fed into it at its own parent.</summary>
    public bool RemoveGroup(AudioMixerGroup group)
    {
        EnsureBound();

        int index = _groups.IndexOf(group);

        // The root has nowhere to re-point its children to, so it stays.
        if (index <= 0) return false;

        int newParent = group.ParentIndex;
        _groups.RemoveAt(index);

        foreach (AudioMixerGroup other in _groups)
        {
            if (other.ParentIndex == index)
                other.ParentIndex = newParent;
            else if (other.ParentIndex > index)
                other.ParentIndex--;
        }

        group.Dispose();
        return true;
    }

    internal AudioMixerGroup GetGroupAt(int index)
        => index >= 0 && index < _groups.Count ? _groups[index] : null;

    /// <summary>Drops every native object this mixer built. They are rebuilt on next use.</summary>
    public void ReleaseNative()
    {
        foreach (AudioMixerGroup group in _groups)
        {
            if (group.IsValid())
                group.ReleaseNative();
        }
    }

    public void OnBeforeSerialize() { }

    /// <summary>Groups arrive knowing their parent's index but not which mixer they belong to.</summary>
    public void OnAfterDeserialize() => EnsureBound();

    private void EnsureBound()
    {
        for (int i = 0; i < _groups.Count; i++)
        {
            if (_groups[i].IsNotValid()) continue;

            _groups[i].Bind(this);
            _groups[i].EnsureIdentity(i);
        }
    }
}
