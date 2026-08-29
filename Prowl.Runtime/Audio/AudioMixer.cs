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

    [SerializeIgnore]
    private int _nodeGeneration;

    [SerializeIgnore]
    private bool _releasing;

    /// <summary>
    /// Bumped every time this bus builds or tears down its native node. Anything attached to the bus
    /// compares it to know whether what it attached to is still there, because a rebuilt node can
    /// land on the address the old one had and the pointer alone cannot answer that.
    /// </summary>
    internal int NodeGeneration => _nodeGeneration;

    /// <summary>
    /// Raised while the native node is still valid, immediately before it is torn down. Everything
    /// feeding this bus has to let go here: afterwards it is attached to a node that no longer
    /// exists, which is silence at best.
    /// </summary>
    internal event Action NativeReleasing;

    // Effect hosting. Mirrors AudioSource: the bus feeds a node that runs the managed chain, and that
    // node is what feeds the parent.
    [SerializeIgnore]
    private ma_effect_node_ptr _effectNode;

    [SerializeIgnore]
    private bool _effectNodeReady;

    [SerializeIgnore]
    private ma_effect_node_process_proc _onEffectNodeProcess;

    [SerializeIgnore]
    private readonly AudioEffectChain _chain = new();

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

    [SerializeIgnore]
    private bool _solo;

    /// <summary>
    /// Hears this bus on its own. Deliberately not saved: soloing is something you do while listening
    /// to a mix, not a property of it, and a project that opened with a bus soloed would be a mystery.
    /// </summary>
    /// <remarks>
    /// While anything is soloed, a bus sounds only if it is the soloed one, is inside it, or is on the
    /// path it takes to the output. That last part is what makes soloing a child audible at all: every
    /// bus between it and the master has to keep passing audio.
    /// </remarks>
    public bool Solo
    {
        get => _solo;
        set
        {
            if (_solo == value) return;

            _solo = value;

            // Soloing one bus changes what every other bus does, so the whole mixer is re-applied.
            AudioMixer owner = _mixer;

            if (owner.IsValid()) owner.ApplyVolumes();
            else ApplyVolume();
        }
    }

    /// <summary>True when a solo somewhere in this mixer is what is keeping this bus quiet.</summary>
    public bool SilencedBySolo
    {
        get
        {
            AudioMixer owner = _mixer;

            if (owner.IsNotValid() || !owner.AnySolo) return false;

            foreach (AudioMixerGroup soloed in owner.Groups)
            {
                if (soloed.IsNotValid() || !soloed._solo) continue;

                if (ReferenceEquals(soloed, this) || IsAncestorOf(soloed) || soloed.IsAncestorOf(this))
                    return false;
            }

            return true;
        }
    }

    /// <summary>True when <paramref name="other"/> feeds into this bus, however far down.</summary>
    public bool IsAncestorOf(AudioMixerGroup other)
    {
        AudioMixerGroup at = other.IsValid() ? other.Parent : null;
        int steps = 0;

        // Bounded because a hand edited asset can describe a loop, and ValidateHierarchy only
        // straightens out the ones it can see from the root.
        while (at.IsValid() && steps++ < 64)
        {
            if (ReferenceEquals(at, this)) return true;
            at = at.Parent;
        }

        return false;
    }

    // Written by the audio thread once per block, read by whatever is drawing meters. A float write is
    // atomic, and a reader that catches the previous block's value is showing a meter one block stale.
    [SerializeIgnore]
    private volatile float _peakLevel;

    /// <summary>
    /// Loudest sample this bus produced in the block it last processed, 0 when nothing is asking for
    /// levels. Ask through <see cref="RequestMetering"/> for as long as you want it kept up to date.
    /// </summary>
    public float PeakLevel => _peakLevel;

    private static long s_meteringRequestedTick;

    /// <summary>
    /// Asks every bus to measure what it is producing. Call it each frame that meters are on screen.
    /// </summary>
    /// <remarks>
    /// Off by default because it is a pass over every block of every bus, on the audio thread, for a
    /// number nobody is looking at. Asking expires on its own so a window that closes, or an editor
    /// that stops drawing, does not leave the work running.
    /// </remarks>
    public static void RequestMetering() => s_meteringRequestedTick = Environment.TickCount64;

    private static bool MeteringWanted => Environment.TickCount64 - s_meteringRequestedTick < 500;

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
                // ReleaseNative rather than clearing the pointer by hand: the effect node holds a
                // plain allocation that no device owns and that nothing else would ever free, and it
                // already knows not to uninitialize anything belonging to a device that has gone.
                ReleaseNative();
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
        _nodeGeneration++;
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
        IntPtr parentNode = parent.IsValid() ? parent.NativeNode : IntPtr.Zero;

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

        bool silent = _mute || SilencedBySolo;

        MiniAudioNative.ma_sound_group_set_volume(_nativeGroup, silent ? 0.0f : DecibelsToLinear(_volumeDB));
    }

    /// <summary>Effects applied to everything routed into this group, in order.</summary>
    public IReadOnlyList<AudioEffect> Effects => _effects;

    /// <summary>Adds an effect to this bus. The group owns it and destroys it when it is removed.</summary>
    public void AddEffect(AudioEffect effect)
    {
        if (effect == null) return;

        if (!effect.TryClaim(this))
        {
            Debug.LogError($"Audio mixer group '{_groupName}' was given an effect that is already in another " +
                           "chain. An effect carries its own filter and delay state, so it can only be in one.");
            return;
        }

        _effects.Add(effect);
        effect.Initialize(AudioContext.SampleRate, AudioContext.Channels);
        _chain.Publish(_effects);
    }

    /// <summary>Removes an effect from this bus and destroys it.</summary>
    public void RemoveEffect(AudioEffect effect)
    {
        if (effect == null || !_effects.Remove(effect)) return;

        _chain.Publish(_effects);
        Retire(effect);
    }

    private static void Retire(AudioEffect effect)
    {
        effect.Release();
        effect.OnDestroy();
    }

    /// <summary>
    /// Binds every effect to the current audio format and republishes the chain the audio thread reads.
    /// Call after editing <see cref="Effects"/> in place.
    /// </summary>
    public void RefreshEffects()
    {
        // The inspector replaces the list rather than editing it, so an effect deleted there never
        // passes through RemoveEffect. What was published last is what the bus used to hold.
        foreach (AudioEffect previous in _chain.Current)
        {
            if (previous != null && !_effects.Contains(previous))
                Retire(previous);
        }

        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            AudioEffect effect = _effects[i];

            if (effect == null) continue;

            if (!effect.TryClaim(this))
            {
                Debug.LogError($"Audio mixer group '{_groupName}' holds an effect that belongs to another chain, " +
                               "so it has been removed. An effect carries its own state and can only be in one.");
                _effects.RemoveAt(i);
                continue;
            }

            // Re-bound rather than just re-validated when the device has reopened on a different
            // format, since the DSP state is sized to one.
            if (!effect.IsInitialized || effect.SampleRate != AudioContext.SampleRate || effect.Channels != AudioContext.Channels)
                effect.Initialize(AudioContext.SampleRate, AudioContext.Channels);
            else
                effect.OnValidate();
        }

        _chain.Publish(_effects);
    }

    // Native calls this on the audio thread. An exception leaving it is undefined behaviour, so a
    // failing effect costs this bus its audio and one log line.
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
            *frameCountOut = _chain.Process(framesIn[0], *frameCountIn, framesOut[0], *frameCountOut, channels);

            if (MeteringWanted)
                _peakLevel = Peak(framesOut[0], (int)(*frameCountOut * channels));
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

    /// <summary>Loudest absolute sample in a block, which is what a meter shows.</summary>
    private static unsafe float Peak(float* samples, int count)
    {
        float peak = 0.0f;

        for (int i = 0; i < count; i++)
        {
            float magnitude = Maths.Abs(samples[i]);

            if (magnitude > peak) peak = magnitude;
        }

        return peak;
    }

    internal void ReleaseNative()
    {
        // Nothing built means nothing to tear down and nothing that could be attached to it. This is
        // also what keeps the walk below from doing the same work twice over a tree.
        if (_builtForDevice == -1 && _nativeGroup.pointer == IntPtr.Zero && _effectNode.pointer == IntPtr.Zero)
            return;

        // A group that reaches back into this one while it is releasing, which a hand edited asset
        // with a routing loop can produce, has to stop here rather than recurse.
        if (_releasing)
            return;

        _releasing = true;

        try
        {
            // Both a child bus and a source attach their output to this group's node, and neither
            // has any other way to find out it is going. Told while the node is still valid, so what
            // they do about it still means something.
            ReleaseChildren();
            NativeReleasing?.Invoke();

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
            _nodeGeneration++;
        }
        finally
        {
            _releasing = false;
        }
    }

    /// <summary>
    /// Releases every bus feeding this one, which rebuilds from whatever asks for it next.
    /// </summary>
    /// <remarks>
    /// Read from the already bound mixer rather than through <see cref="Mixer"/>, which resolves the
    /// owning asset. Teardown is the wrong moment to load one, and anything that got as far as
    /// building a node has a bound mixer already, since that is where its parent came from.
    /// </remarks>
    private void ReleaseChildren()
    {
        AudioMixer owner = _mixer;

        if (owner.IsNotValid())
            return;

        foreach (AudioMixerGroup group in owner.Groups)
        {
            if (group.IsValid() && !ReferenceEquals(group, this) && ReferenceEquals(group.Parent, this))
                group.ReleaseNative();
        }
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

    [SerializeField, HideInInspector]
    private List<AudioMixerSnapshot> _snapshots = [];

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

    /// <summary>True while any bus in this mixer is soloed, which is what silences the rest.</summary>
    public bool AnySolo
    {
        get
        {
            foreach (AudioMixerGroup group in _groups)
                if (group.IsValid() && group.Solo) return true;

            return false;
        }
    }

    /// <summary>Clears every solo in the mixer.</summary>
    public void ClearSolo()
    {
        foreach (AudioMixerGroup group in _groups)
            if (group.IsValid()) group.Solo = false;
    }

    /// <summary>
    /// Pushes every bus's level across again. Needed after anything that changes what a bus should be
    /// doing without touching its own fields, which is what a solo somewhere else is.
    /// </summary>
    public void ApplyVolumes()
    {
        EnsureBound();

        foreach (AudioMixerGroup group in _groups)
            if (group.IsValid()) group.OnValidate();
    }

    /// <summary>
    /// Re-routes <paramref name="group"/> to feed <paramref name="parent"/>, or the output when that is
    /// null. Refuses anything that would make a bus feed itself, directly or through its own children.
    /// </summary>
    public bool SetParent(AudioMixerGroup group, AudioMixerGroup parent)
    {
        EnsureBound();

        int index = _groups.IndexOf(group);

        // The root has nowhere else to go: everything else in the mixer already feeds it.
        if (index <= 0) return false;

        if (parent.IsValid() && (ReferenceEquals(parent, group) || group.IsAncestorOf(parent)))
        {
            Debug.LogWarning($"'{group.GroupName}' cannot feed '{parent.GroupName}', which already feeds it.");
            return false;
        }

        int parentIndex = parent.IsValid() ? _groups.IndexOf(parent) : 0;

        if (parentIndex < 0 || parentIndex == group.ParentIndex) return false;

        group.ParentIndex = parentIndex;

        // Its node is attached to the bus it used to feed, so it has to be built again against the new
        // one. Releasing takes the buses feeding it with it, and tells any source attached to let go.
        group.ReleaseNative();
        return true;
    }

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

    /// <summary>
    /// Removes a group, re-pointing anything that fed into it at its own parent. Sources still routed
    /// to it fall back to the master output.
    /// </summary>
    /// <remarks>
    /// The group's native objects are released, but the object itself is left alive. It is a sub-asset,
    /// so anything holding an <see cref="AudioSource.OutputGroup"/> reference to it still has one, and
    /// destroying it here would leave those sources pointing at a destroyed object rather than at
    /// nothing. Its disappearance from the asset database is the reimport's business, which is how the
    /// rest of the sub-asset machinery works.
    /// </remarks>
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

        // Detached, so it cannot claim whichever group has taken its old index in the meantime.
        group.ParentIndex = -1;
        group.ReleaseNative();

        Debug.LogWarning($"Removed audio mixer group '{group.GroupName}' from '{Name}'. " +
                         "Any AudioSource still routed to it now feeds the master output.");

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

    #region Snapshots

    /// <summary>Every recorded mix this mixer can be moved to.</summary>
    public IReadOnlyList<AudioMixerSnapshot> Snapshots => _snapshots;

    /// <summary>The snapshot this mixer was last moved to, or is on its way to.</summary>
    public AudioMixerSnapshot ActiveSnapshot { get; private set; }

    /// <summary>True while a transition between snapshots is still running.</summary>
    public bool IsTransitioning => _transition != null;

    /// <summary>Finds a snapshot by name, or null. Names are compared exactly.</summary>
    public AudioMixerSnapshot FindSnapshot(string name)
    {
        foreach (AudioMixerSnapshot snapshot in _snapshots)
            if (snapshot != null && snapshot.Name == name) return snapshot;

        return null;
    }

    /// <summary>Records the mixer as it is now under a new name, and returns the snapshot.</summary>
    public AudioMixerSnapshot CaptureSnapshot(string name)
    {
        EnsureBound();

        var snapshot = new AudioMixerSnapshot { Name = name };
        snapshot.CaptureFrom(this);
        _snapshots.Add(snapshot);
        return snapshot;
    }

    public bool RemoveSnapshot(AudioMixerSnapshot snapshot)
    {
        if (ReferenceEquals(snapshot, ActiveSnapshot)) ActiveSnapshot = null;

        return _snapshots.Remove(snapshot);
    }

    /// <summary>Puts the mixer into a recorded mix at once, with nothing to ease.</summary>
    public void ApplySnapshot(AudioMixerSnapshot snapshot)
    {
        if (snapshot == null) return;

        EnsureBound();

        MixerParameters.Walk(this, (path, _, apply) =>
        {
            float? recorded = snapshot.Find(path);

            // A parameter the snapshot never recorded is one that did not exist when it was taken, so
            // it is left where it is rather than being reset to a value nobody chose.
            if (recorded.HasValue) apply(recorded.Value);
        });

        _transition = null;
        ActiveSnapshot = snapshot;
    }

    /// <summary>
    /// Moves the mixer to a recorded mix over <paramref name="seconds"/>, which is what a change of
    /// scene, a pause menu or going underwater actually is.
    /// </summary>
    /// <remarks>
    /// Starts from wherever the mixer is now rather than from whatever it was last told to be, so a
    /// transition interrupted by another one carries on from what you can currently hear instead of
    /// jumping back. Flags cross at the half way mark, since a mute has no midpoint to ease through.
    /// </remarks>
    public void TransitionTo(AudioMixerSnapshot snapshot, float seconds)
    {
        if (snapshot == null) return;

        if (!(seconds > 0.0f))
        {
            ApplySnapshot(snapshot);
            return;
        }

        EnsureBound();

        var transition = new Transition { Duration = seconds };

        MixerParameters.Walk(this, (path, current, apply) =>
        {
            float? recorded = snapshot.Find(path);

            if (recorded.HasValue)
                transition.Steps.Add(new Transition.Step { Apply = apply, From = current, To = recorded.Value });
        });

        ActiveSnapshot = snapshot;

        if (transition.Steps.Count == 0)
        {
            _transition = null;
            return;
        }

        _transition = transition;

        if (!s_transitioning.Contains(this))
            s_transitioning.Add(this);
    }

    /// <summary>Moves to the snapshot with this name, if there is one.</summary>
    public void TransitionTo(string name, float seconds) => TransitionTo(FindSnapshot(name), seconds);

    [SerializeIgnore]
    private Transition _transition;

    private sealed class Transition
    {
        public struct Step
        {
            public Action<float> Apply;
            public float From;
            public float To;
        }

        public float Duration;
        public float Elapsed;
        public readonly List<Step> Steps = [];
    }

    private static readonly List<AudioMixer> s_transitioning = [];
    private static long s_lastTransitionTick;

    /// <summary>
    /// Advances every running snapshot transition. Driven by the audio update, and cheap to call when
    /// nothing is transitioning, which is almost always.
    /// </summary>
    /// <remarks>
    /// Measured against the wall clock rather than the game's, because a mix change belongs to what the
    /// player is hearing: a pause menu that fades the music over a third of a second has to take a
    /// third of a second even though the thing it is fading for has stopped time.
    /// </remarks>
    public static void TickTransitions()
    {
        if (s_transitioning.Count == 0)
        {
            s_lastTransitionTick = 0;
            return;
        }

        long now = System.Diagnostics.Stopwatch.GetTimestamp();

        // The first tick of a run has nothing to measure against, so it advances nothing.
        float delta = s_lastTransitionTick == 0
            ? 0.0f
            : (float)((now - s_lastTransitionTick) / (double)System.Diagnostics.Stopwatch.Frequency);

        s_lastTransitionTick = now;

        for (int i = s_transitioning.Count - 1; i >= 0; i--)
        {
            AudioMixer mixer = s_transitioning[i];

            if (mixer.IsNotValid() || !mixer.Advance(delta))
                s_transitioning.RemoveAt(i);
        }
    }

    /// <summary>Moves one transition along. False once it has arrived and has nothing left to do.</summary>
    private bool Advance(float delta)
    {
        Transition transition = _transition;

        if (transition == null) return false;

        transition.Elapsed += delta;

        float t = Maths.Clamp(transition.Elapsed / transition.Duration, 0.0f, 1.0f);

        foreach (Transition.Step step in transition.Steps)
            step.Apply(step.From + (step.To - step.From) * t);

        if (t < 1.0f) return true;

        _transition = null;
        return false;
    }

    #endregion

    public void OnBeforeSerialize() { }

    /// <summary>Groups arrive knowing their parent's index but not which mixer they belong to.</summary>
    public void OnAfterDeserialize()
    {
        EnsureBound();
        ValidateHierarchy();
    }

    /// <summary>
    /// Straightens out routing that does not lead anywhere, which only a hand edited or corrupted
    /// asset can contain: the public API cannot build one, since a new group is always appended below
    /// the parent it is given.
    /// </summary>
    /// <remarks>
    /// A loop would be attached as a loop in the native node graph, where each bus feeds the next and
    /// the last feeds the first. What the audio backend does with that is not something to find out at
    /// runtime. Groups in a loop also never appear in the mixer inspector, because nothing reaches
    /// them walking down from the root.
    /// </remarks>
    private void ValidateHierarchy()
    {
        for (int i = 0; i < _groups.Count; i++)
        {
            AudioMixerGroup group = _groups[i];

            if (group.IsNotValid()) continue;

            if (group.ParentIndex >= _groups.Count || group.ParentIndex < -1)
            {
                Debug.LogWarning($"Audio mixer '{Name}' has group '{group.GroupName}' routed to a bus that does not exist. It now feeds the master output.");
                group.ParentIndex = -1;
                continue;
            }

            // Walk up to the root. Taking more steps than there are groups means it never gets there.
            int at = group.ParentIndex;
            int steps = 0;

            while (at >= 0 && at < _groups.Count)
            {
                if (++steps > _groups.Count)
                {
                    Debug.LogWarning($"Audio mixer '{Name}' has a routing loop through group '{group.GroupName}'. It now feeds the master output.");
                    group.ParentIndex = -1;
                    break;
                }

                at = _groups[at].IsValid() ? _groups[at].ParentIndex : -1;
            }
        }
    }

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
