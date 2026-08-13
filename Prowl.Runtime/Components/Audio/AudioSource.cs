// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Audio;
using Prowl.Runtime.Audio.Effects;
using Prowl.Runtime.Audio.Native;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

public delegate void AudioEndEvent();
public delegate void AudioProcessEvent(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels);
public delegate void AudioReadEvent(NativeArray<float> framesOut, UInt64 frameCount, Int32 channels);

/// <summary>
/// AudioSource component for playing audio in the scene.
/// Supports spatial audio, effects, procedural generation, and serialization.
/// </summary>
[AddComponentMenu("Audio/Audio Source")]
[ComponentIcon("\uf028")] // VolumeHigh
public sealed class AudioSource : MonoBehaviour
{
    private class SourceInfo
    {
        public IntPtr handle;
        public bool atEnd;
        public long startOrder;

        /// <summary>Shared clip buffer this voice holds a reference on, 0 if it holds none.</summary>
        public ulong clipHash;
    }

    // Audio clip and playback settings
    [Header("Playback")]
    [SerializeField, Tooltip("The clip this source plays.")]
    private AssetRef<AudioClip> _clip;
    [SerializeField, Tooltip("Start playing as soon as the component is enabled.")]
    private bool _playOnStart = false;
    // Set when OnEnable wanted to auto-play but the clip was still streaming in (async loading);
    // Update performs the play once the clip arrives.
    private bool _pendingAutoPlay = false;
    [SerializeField]
    private bool _loop = false;
    [SerializeField]
    private float _volume = 1.0f;
    [SerializeField, Tooltip("Playback speed multiplier. 1 is the clip's original pitch.")]
    private float _pitch = 1.0f;
    [SerializeField, Range(-1f, 1f), Tooltip("Stereo placement. -1 is fully left, 1 is fully right.")]
    private float _pan = 0.0f;
    [SerializeField, Tooltip("Balance keeps the original stereo image, Pan collapses it toward one side.")]
    private PanMode _panMode = PanMode.Balance;

    // Spatial audio settings
    [Header("Spatial")]
    [SerializeField, Tooltip("Position this source in 3D space relative to the AudioListener.")]
    private bool _spatial = true;
    [SerializeField, Tooltip("Strength of the pitch shift from relative motion. 0 disables doppler.")]
    private float _dopplerFactor = 1.0f;
    [SerializeField, Tooltip("Distance below which the source plays at full volume.")]
    private float _minDistance = 1.0f;
    [SerializeField, Tooltip("Distance at which the source reaches its quietest.")]
    private float _maxDistance = 10.0f;
    [SerializeField, Tooltip("Curve used to fall off between the min and max distance.")]
    private AttenuationModel _attenuationModel = AttenuationModel.Linear;

    [Header("Routing")]
    [SerializeField, Tooltip("Mixer group this source feeds into. Empty routes straight to the master output.")]
    private AssetRef<AudioMixerGroup> _outputGroup;

    /// <summary>
    /// The mixer group this source feeds into, or null to go straight to the master output. Setting it
    /// re-routes immediately, so a running source moves buses without restarting.
    /// </summary>
    public AudioMixerGroup OutputGroup
    {
        get => _outputGroup.Res;
        set
        {
            _outputGroup = value;
            RouteToOutputGroup();
        }
    }

    [Header("Voices")]
    [SerializeField, Tooltip("How many one shot voices this source can sound at once before the longest running one is taken over.")]
    private int _maxOneShotVoices = 8;

    /// <summary>
    /// How many one shot voices this source can sound at once before stealing the oldest. Lowering it
    /// takes voices away immediately rather than waiting for them to finish.
    /// </summary>
    public int MaxOneShotVoices
    {
        get => _maxOneShotVoices;
        set
        {
            _maxOneShotVoices = value;
            TrimOneShotVoices();
        }
    }

    // Native handles
    private SourceInfo _mainSource;
    private readonly List<SourceInfo> _oneShots = [];
    private long _oneShotCounter;
    private bool _isPaused;
    private ulong _pausedCursor;
    private int _deviceGeneration = -1;

    // Captured while the outgoing device could still answer, so a restart picks playback up where it
    // left off rather than reading a handle that has already gone.
    private bool _resumePlaying;
    private ulong _resumeCursor;
    private ma_sound_group_ptr _soundGroup;
    private ma_effect_node_ptr _effectNode;
    private bool _effectNodeReady;
    private IntPtr _routedTo;

    // The bus this source is attached to, and the node generation it was attached at. Kept so a bus
    // that rebuilds or disappears under a running source can be noticed and followed.
    private AudioMixerGroup _routedGroup;
    private int _routedGeneration;

    private Float3 _previousPosition;
    private Float3 _velocity;
    private ma_effect_node_process_proc _onEffectNodeProcess;
    private ma_procedural_data_source_proc _proceduralProcessCallback;

    // Effects and buffers
    [Header("Effects")]
    [SerializeField, Tooltip("Applied to this source's output in order, before it reaches the mix.")]
    private List<AudioEffect> _effects = [];

    // What the audio thread runs. Owns its own working buffers and the published snapshot, so the
    // audio thread never sees a half-edited chain and never needs a lock to walk one.
    private readonly AudioEffectChain _chain = new();

    private AudioBuffer _outputBuffer;

    // Latched so a failing audio callback reports itself once instead of every block.
    private bool _effectProcessFailed;
    private bool _proceduralProcessFailed;

    // Events
    public event AudioEndEvent End;
    public event AudioProcessEvent Process;
    public event AudioReadEvent Read;

    #region Properties

    /// <summary>
    /// The clip this source plays. Assigning a different one stops playback, since what was playing
    /// was the previous clip. Starting the new one is <see cref="Play"/>'s job.
    /// </summary>
    /// <remarks>
    /// Reading this resolves the reference, which loads the clip if it has not been already. Use
    /// <see cref="ClipRef"/> to read or assign without triggering that.
    /// </remarks>
    public AudioClip? Clip
    {
        get => _clip.Res;
        set => ClipRef = value;
    }

    /// <summary>
    /// The clip reference, without resolving it. Assigning through here neither loads the outgoing
    /// clip nor the incoming one.
    /// </summary>
    public AssetRef<AudioClip> ClipRef
    {
        get => _clip;
        set
        {
            // Assigning what is already there is not a reason to interrupt anything.
            if (_clip == value)
                return;

            // Whatever is sounding belongs to the clip being replaced.
            Stop();
            _clip = value;
        }
    }

    /// <summary>
    /// If true, the audio will start playing automatically when OnEnable is called.
    /// </summary>
    public bool PlayOnStart
    {
        get => _playOnStart;
        set => _playOnStart = value;
    }

    /// <summary>
    /// If true, the audio will loop continuously.
    /// </summary>
    public bool Loop
    {
        get => _loop;
        set
        {
            _loop = value;
            if (_mainSource != null && _mainSource.handle != IntPtr.Zero)
                MiniAudioExNative.ma_ex_audio_source_set_loop(_mainSource.handle, value ? (uint)1 : 0);
        }
    }

    /// <summary>
    /// The slowest playback speed a source can be set to. Zero stalls the resampler and a negative
    /// rate is not something the native layer defines, so the range stops short of both.
    /// </summary>
    public const float MinPitch = 0.01f;

    /// <summary>
    /// Keeps a live audio parameter inside the range the native layer is defined over, and replaces a
    /// value that is not a number at all.
    /// </summary>
    /// <remarks>
    /// A NaN or an infinity reaching the mix is not a bad frame, it is permanent: it propagates
    /// through the filters and the spatializer and stays in their state until something rebuilds
    /// them, so the source is silence or noise for the rest of the session. These come off sliders,
    /// curves and gameplay arithmetic, any of which can produce one by dividing by something that
    /// happened to be zero, so they are checked at the boundary rather than trusted.
    /// </remarks>
    private static float Sane(float value, float min, float max, float fallback)
        => float.IsFinite(value) ? Maths.Clamp(value, min, max) : fallback;

    /// <summary>
    /// Level of the audio source. 1 is the clip as it was authored, 0 is silence, and above 1
    /// amplifies. Negative is not a quieter sound, it is the same sound with its phase inverted, so
    /// the range stops at 0.
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Sane(value, 0.0f, float.MaxValue, 1.0f);
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_volume(_soundGroup, _volume);
        }
    }

    /// <summary>
    /// Playback speed of the audio source, which carries the pitch with it. 1 is the clip's own rate.
    /// Never below <see cref="MinPitch"/>.
    /// </summary>
    public float Pitch
    {
        get => _pitch;
        set
        {
            _pitch = Sane(value, MinPitch, float.MaxValue, 1.0f);
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_pitch(_soundGroup, _pitch);
        }
    }

    /// <summary>
    /// Stereo pan of the audio source (-1.0 left, 0.0 center, 1.0 right).
    /// </summary>
    public float Pan
    {
        get => _pan;
        set
        {
            _pan = Sane(value, -1.0f, 1.0f, 0.0f);
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_pan(_soundGroup, _pan);
        }
    }

    /// <summary>
    /// Pan mode (Balance or Pan).
    /// </summary>
    public PanMode PanMode
    {
        get => _panMode;
        set
        {
            _panMode = value;
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_pan_mode(_soundGroup, (ma_pan_mode)value);
        }
    }

    /// <summary>
    /// If true, spatial audio (3D positioning) is enabled.
    /// </summary>
    public bool Spatial
    {
        get => _spatial;
        set
        {
            _spatial = value;
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_spatialization_enabled(_soundGroup, value ? (uint)1 : 0);
        }
    }

    /// <summary>
    /// Strength of the pitch shift from relative motion. 0 disables doppler, 1 is the real world
    /// amount, and above that exaggerates it. Never negative, which would shift the wrong way.
    /// </summary>
    public float DopplerFactor
    {
        get => _dopplerFactor;
        set
        {
            _dopplerFactor = Sane(value, 0.0f, float.MaxValue, 1.0f);
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_doppler_factor(_soundGroup, _dopplerFactor);
        }
    }

    /// <summary>
    /// Distance below which the source plays at full volume. Never negative, and never past
    /// <see cref="MaxDistance"/>, which is carried up with it if need be.
    /// </summary>
    public float MinDistance
    {
        get => _minDistance;
        set
        {
            _minDistance = value;
            ApplyDistances();
        }
    }

    /// <summary>
    /// Distance at which the source reaches its quietest. Never below <see cref="MinDistance"/>.
    /// </summary>
    public float MaxDistance
    {
        get => _maxDistance;
        set
        {
            _maxDistance = value;
            ApplyDistances();
        }
    }

    /// <summary>
    /// Keeps the attenuation range coherent and pushes it across.
    /// </summary>
    /// <remarks>
    /// A minimum past the maximum carries the maximum up with it rather than being clamped away, so
    /// widening the range by dragging either end works without having to do it in a particular order.
    /// An inverted range is not a curve at all, and both ends are now inspector fields, which write
    /// the backing values directly and never go through these setters.
    /// </remarks>
    private void ApplyDistances()
    {
        _minDistance = Sane(_minDistance, 0.0f, float.MaxValue, 1.0f);
        _maxDistance = Maths.Max(Sane(_maxDistance, 0.0f, float.MaxValue, 10.0f), _minDistance);

        if (_soundGroup.pointer == IntPtr.Zero) return;

        MiniAudioNative.ma_sound_group_set_min_distance(_soundGroup, _minDistance);
        MiniAudioNative.ma_sound_group_set_max_distance(_soundGroup, _maxDistance);
    }

    /// <summary>
    /// Attenuation model for spatial audio distance falloff.
    /// </summary>
    public AttenuationModel AttenuationModel
    {
        get => _attenuationModel;
        set
        {
            _attenuationModel = value;
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_attenuation_model(_soundGroup, (ma_attenuation_model)value);
        }
    }

    /// <summary>
    /// Returns true if the audio source is currently playing.
    /// </summary>
    public bool IsPlaying
    {
        get
        {
            if (_mainSource == null || _mainSource.handle == IntPtr.Zero)
                return false;
            return MiniAudioExNative.ma_ex_audio_source_get_is_playing(_mainSource.handle) > 0;
        }
    }

    /// <summary>
    /// Playback position, counted in the frames the device runs at.
    /// </summary>
    /// <remarks>
    /// This is the engine's own read position, so it advances at the rate the device was opened at
    /// whatever rate the clip was authored at. That is not the unit <see cref="Length"/> is in, so
    /// the two do not belong in one expression. <see cref="NormalizedTime"/> is what compares them.
    ///
    /// It also keeps counting across a loop rather than starting over, so on a looping source it is
    /// how long the source has been sounding for. <see cref="PlaybackTime"/> is where in the clip it
    /// currently is.
    /// </remarks>
    public ulong Cursor
    {
        get
        {
            if (_mainSource == null || _mainSource.handle == IntPtr.Zero)
                return 0;
            return MiniAudioExNative.ma_ex_audio_source_get_pcm_position(_mainSource.handle);
        }
        set
        {
            if (_mainSource != null && _mainSource.handle != IntPtr.Zero)
                MiniAudioExNative.ma_ex_audio_source_set_pcm_position(_mainSource.handle, value);
        }
    }

    /// <summary>
    /// Length of the playing clip, counted in the frames it was authored at.
    /// </summary>
    /// <remarks>
    /// This comes from the decoder rather than from the mix, so it is in the clip's own rate. Only a
    /// clip that happens to have been authored at the rate the device was opened at counts the same
    /// way <see cref="Cursor"/> does.
    /// </remarks>
    public ulong Length
    {
        get
        {
            if (_mainSource == null || _mainSource.handle == IntPtr.Zero)
                return 0;
            return MiniAudioExNative.ma_ex_audio_source_get_pcm_length(_mainSource.handle);
        }
    }

    /// <summary>
    /// The rate <see cref="Length"/> is counted at, which is the playing clip's own. Falls back to the
    /// device's, which is also the one case where the two cannot disagree.
    /// </summary>
    /// <remarks>
    /// Asking a clip its format decodes it once, so this is read where a caller has asked for a time
    /// in seconds rather than on the path that starts playback.
    /// </remarks>
    private int ClipFrameRate
    {
        get
        {
            AudioClip clip = _clip.Res;
            int rate = clip.IsValid() ? clip.SampleRate : 0;
            return rate > 0 ? rate : AudioContext.SampleRate;
        }
    }

    /// <summary>
    /// Where in the clip playback currently is, in seconds. Named to stay clear of the engine's
    /// <see cref="Time"/> class.
    /// </summary>
    public float PlaybackTime
    {
        get
        {
            float position = AudioContext.SampleRate > 0 ? (float)(Cursor / (double)AudioContext.SampleRate) : 0.0f;
            float duration = _loop ? Duration : 0.0f;

            return duration > 0.0f ? position % duration : position;
        }
        set => Cursor = value <= 0.0f ? 0 : (ulong)(value * AudioContext.SampleRate);
    }

    /// <summary>Length of the playing clip in seconds, 0 when nothing is loaded.</summary>
    public float Duration
    {
        get
        {
            int rate = ClipFrameRate;
            return rate > 0 ? (float)(Length / (double)rate) : 0.0f;
        }
    }

    /// <summary>
    /// Playback position as a fraction of the clip, 0 at the start and 1 at the end.
    /// </summary>
    /// <remarks>
    /// Compared as seconds, because the two frame counts are not counted at the same rate. Dividing
    /// one by the other answers the ratio between those rates as much as it answers the position: a
    /// clip authored at half the rate the device runs at reached 1 barely half way through.
    /// </remarks>
    public float NormalizedTime
    {
        get
        {
            float duration = Duration;
            return duration > 0.0f ? Maths.Clamp(PlaybackTime / duration, 0.0f, 1.0f) : 0.0f;
        }
        set
        {
            float duration = Duration;

            if (duration > 0.0f)
                PlaybackTime = Maths.Clamp(value, 0.0f, 1.0f) * duration;
        }
    }

    #endregion

    #region MonoBehaviour Lifecycle

    public override void OnEnable()
    {
        AudioContext.DeviceClosing += OnDeviceClosing;
        CreateNativeResources();
    }

    /// <summary>
    /// True while the native objects this source holds belong to the device that is open now. Anything
    /// held from an older one has to be dropped rather than uninitialized.
    /// </summary>
    private bool NativeIsLive => AudioContext.IsInitialized && _deviceGeneration == AudioContext.DeviceGeneration;

    /// <summary>
    /// The device is closing. This is the last point at which its objects can be read or released, so
    /// capture what is needed to pick playback up again and then let them go.
    /// </summary>
    private void OnDeviceClosing()
    {
        _resumePlaying = IsPlaying;
        _resumeCursor = _resumePlaying ? Cursor : 0;

        DestroyNativeResources();
    }

    private void CreateNativeResources()
    {
        // No device means no native objects to hand a null context to. Every playback entry point
        // already no-ops on a null sound group, so the component stays inert but usable.
        if (!AudioContext.IsInitialized)
        {
            Debug.LogWarningOnce("Audio.NoContext", "No audio device is initialized, audio components stay inactive.");
            return;
        }

        _deviceGeneration = AudioContext.DeviceGeneration;

        // Initialize native resources
        _previousPosition = Transform.Position;
        _outputBuffer = new AudioBuffer(8192);
        _proceduralProcessCallback = OnProceduralProcess;

        // Create sound group
        _soundGroup.pointer = MiniAudioExNative.ma_ex_sound_group_init(AudioContext.NativeContext);

        if (_soundGroup.pointer != IntPtr.Zero)
        {
            // Create main audio source
            _mainSource = new SourceInfo();
            _mainSource.handle = MiniAudioExNative.ma_ex_audio_source_init(AudioContext.NativeContext);
            _mainSource.atEnd = false;
            MiniAudioExNative.ma_ex_audio_source_set_group(_mainSource.handle, _soundGroup.pointer);

            // Setup effect node
            _effectNode = new ma_effect_node_ptr(true);
            _onEffectNodeProcess = OnEffectProcess;

            ma_effect_node_config effectNodeConfig = MiniAudioNative.ma_effect_node_config_init(
                (UInt32)AudioContext.Channels,
                (UInt32)AudioContext.SampleRate,
                _onEffectNodeProcess,
                IntPtr.Zero
            );

            ma_engine_ptr pEngine = new ma_engine_ptr(MiniAudioExNative.ma_ex_context_get_engine(AudioContext.NativeContext));

            _effectNodeReady = MiniAudioNative.ma_effect_node_init(MiniAudioNative.ma_engine_get_node_graph(pEngine), ref effectNodeConfig, _effectNode) == ma_result.success;

            if (_effectNodeReady)
            {
                MiniAudioNative.ma_node_attach_output_bus(new ma_node_ptr(_soundGroup.pointer), 0, new ma_node_ptr(_effectNode.pointer), 0);
            }
            else
            {
                Debug.LogError($"[{Name}] Could not create the audio effect node. This source will be heard, but its effects will not be applied.");
            }

            // Either way: routing is what decides which bus this source feeds, and losing the effect
            // node should not quietly cost it that as well.
            RouteToOutputGroup();

            // Apply all serialized settings
            ApplySettings();
            RefreshEffects();

            // Handle playback. If the clip is still streaming in (async loading), defer the
            // auto-play until it arrives (see Update) instead of silently never playing.
            if (!TryAutoPlay() && _playOnStart)
                _pendingAutoPlay = true;
        }
    }

    /// <summary>Perform the OnEnable play-on-start if the clip is loaded.
    /// Returns false if the clip hasn't streamed in yet so the caller can defer.</summary>
    private bool TryAutoPlay()
    {
        if (_clip.Res == null) return false;

        if (_playOnStart)
            Play();

        return true;
    }

    public override void Update()
    {
        // The device was reopened, so everything built against the old one is gone. Rebuild and pick
        // playback back up where it was, rather than going silent until the component is toggled.
        if (_deviceGeneration != AudioContext.DeviceGeneration)
            RebuildForNewDevice();

        if (_soundGroup.pointer == IntPtr.Zero) return;

        // The bus this source feeds rebuilds its node when the device changes, and is replaced
        // outright when the mixer is reimported. Either way this source is attached to something that
        // is no longer the bus it was told to feed.
        if (RoutingIsStale())
            RouteToOutputGroup();

        // Deferred auto-play: the clip was still streaming in at OnEnable; play once it arrives.
        if (_pendingAutoPlay && TryAutoPlay())
            _pendingAutoPlay = false;

        // Update spatial audio properties based on transform
        if (_spatial)
        {
            // Checked here rather than at Play, so scene setup order cannot make this fire spuriously
            // for a source that starts before its listener exists.
            if (AudioListener.ActiveCount == 0 && IsPlaying)
            {
                Debug.LogWarningOnce("Audio.NoListener",
                    "A spatial AudioSource is playing with no enabled AudioListener in the scene, so it is positioned relative to the world origin.");
            }

            Float3 pos = Transform.Position;
            Float3 audioPos = AudioContext.ToAudioSpace(pos);
            MiniAudioNative.ma_sound_group_set_position(_soundGroup, audioPos.X, audioPos.Y, audioPos.Z);

            Float3 forward = AudioContext.ToAudioSpace(Transform.Forward);
            MiniAudioNative.ma_sound_group_set_direction(_soundGroup, forward.X, forward.Y, forward.Z);

            // Velocity for doppler, against the same wall clock the listener uses. Doppler is the
            // difference between the two, so a source measured in scaled time and a listener measured
            // in real time invent a relative velocity out of nothing but the time scale.
            float deltaTime = AudioContext.DeltaTime;
            if (deltaTime > 0)
            {
                _velocity = (pos - _previousPosition) / deltaTime;
                Float3 velocity = AudioContext.ToAudioSpace(_velocity);
                MiniAudioNative.ma_sound_group_set_velocity(_soundGroup, velocity.X, velocity.Y, velocity.Z);
            }

            _previousPosition = pos;
        }

        // Check for end of playback
        if (_mainSource != null && _mainSource.handle != IntPtr.Zero)
        {
            if (MiniAudioExNative.ma_ex_audio_source_get_is_at_end(_mainSource.handle) > 0)
            {
                if (!_mainSource.atEnd)
                {
                    _mainSource.atEnd = true;
                    End?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// The attenuation range, so the falloff can be placed against the scene rather than guessed at
    /// from two numbers in the inspector. Only while selected: a scene full of emitters would be
    /// unreadable otherwise.
    /// </summary>
    public override void DrawGizmosSelected()
    {
        if (!_spatial) return;

        Float3 position = Transform.Position;

        // Inside the inner sphere the source is at full volume, past the outer one it is at its
        // quietest, and the falloff curve runs between them.
        Debug.DrawWireSphere(position, Maths.Max(_minDistance, 0.001f), new Color(0.4f, 1.0f, 0.5f, 1.0f));
        Debug.DrawWireSphere(position, Maths.Max(_maxDistance, _minDistance), new Color(0.2f, 0.5f, 1.0f, 1.0f));
    }

    private void RebuildForNewDevice()
    {
        // Whatever is left belongs to a device that has already gone, and DestroyNativeResources knows
        // not to uninitialize those. The state worth resuming was captured by OnDeviceClosing, while
        // the handles still answered.
        DestroyNativeResources();
        CreateNativeResources();

        // Recorded whether or not a device came back, so this does not retry every frame.
        _deviceGeneration = AudioContext.DeviceGeneration;

        bool resume = _resumePlaying;
        ulong resumeFrom = _resumeCursor;
        _resumePlaying = false;
        _resumeCursor = 0;

        if (!resume || _clip.Res == null) return;

        Play();
        Cursor = resumeFrom;
    }

    /// <summary>The inspector writes the backing fields directly, so the native side has to be
    /// re-synced from them rather than from the property setters.</summary>
    public override void OnValidate()
    {
        ApplySettings();
        RefreshEffects();
        RouteToOutputGroup();
    }

    public override void OnDisable()
    {
        AudioContext.DeviceClosing -= OnDeviceClosing;
        DestroyNativeResources();
    }

    private void DestroyNativeResources()
    {
        // Handles from a device that has already closed are released by the context going away, and
        // uninitializing them would be detaching from a node graph that no longer exists.
        bool live = NativeIsLive;

        DestroyOneShotVoices(live);

        if (_mainSource != null)
        {
            if (live && _mainSource.handle != IntPtr.Zero)
            {
                MiniAudioExNative.ma_ex_audio_source_stop(_mainSource.handle);
                MiniAudioExNative.ma_ex_audio_source_uninit(_mainSource.handle);
            }

            // Whether or not the voice could be uninitialized, the reference it held on its clip's
            // buffer is this component's to give back.
            ReleaseVoiceClip(_mainSource);
            _mainSource = null;
        }

        _isPaused = false;
        _pausedCursor = 0;

        // Cleanup effect node. Only uninit what actually initialized: the pointer is non-zero from
        // the allocation alone, so uninit on a failed init would be running over uninitialized memory.
        if (_effectNode.pointer != IntPtr.Zero)
        {
            if (live && _effectNodeReady)
                MiniAudioNative.ma_effect_node_uninit(_effectNode);

            // Freed either way: this one is a plain allocation, not something the device owns.
            _effectNode.Free();
            _effectNodeReady = false;
        }

        ClearRouting();

        // Cleanup sound group
        if (_soundGroup.pointer != IntPtr.Zero)
        {
            if (live)
                MiniAudioExNative.ma_ex_sound_group_uninit(_soundGroup.pointer);

            _soundGroup.pointer = IntPtr.Zero;
        }

        // The effect chain deliberately survives: effects are managed DSP state that belongs to the
        // source, not to the native objects above, and toggling a component must not silently wipe
        // the chain the user built.
    }

    protected override void OnDispose()
    {
        ClearEffects();
        base.OnDispose();
    }

    #endregion

    #region Playback Control

    /// <summary>
    /// Plays the assigned AudioClip.
    /// </summary>
    public void Play()
    {
        if (_soundGroup.pointer == IntPtr.Zero || _clip.Res == null) return;
        if (_mainSource == null || _mainSource.handle == IntPtr.Zero) return;

        _mainSource.atEnd = false;
        _isPaused = false;
        MiniAudioExNative.ma_ex_audio_source_set_loop(_mainSource.handle, _loop ? (uint)1 : 0);

        StartVoice(_mainSource, _clip.Res);
    }

    /// <summary>
    /// Starts a clip on a voice, holding a reference on the clip's shared buffer for as long as that
    /// voice can read from it.
    /// </summary>
    /// <remarks>
    /// The native decoder reads straight out of that buffer on the audio thread, and the
    /// <see cref="AudioClip"/> that loaded it is free to go in the meantime: an asset reimport
    /// disposes the cached instance, and a clip nothing holds a reference to is collected. Either one
    /// used to free the buffer out from under a sounding voice.
    ///
    /// The new reference is taken before the old one is dropped, so replaying the same clip cannot
    /// pass through zero holders and free the buffer it is about to play from.
    /// </remarks>
    private static void StartVoice(SourceInfo voice, AudioClip clip)
    {
        ulong retained = AudioContext.RetainClipHandle(clip.Hash) ? clip.Hash : 0;

        ReleaseVoiceClip(voice);
        voice.clipHash = retained;

        // A clip whose buffer could not be referenced has no memory to play from, whether it never
        // had any or it has just been released, so it falls back to the file the same way one that
        // was never loaded into memory does.
        if (retained != 0)
            MiniAudioExNative.ma_ex_audio_source_play_from_memory(voice.handle, clip.Handle, clip.DataSize);
        else
            MiniAudioExNative.ma_ex_audio_source_play_from_file(voice.handle, clip.FilePath, clip.StreamFromDisk ? (uint)1 : 0);

        // The only reference to the clip may be the argument, and the native side now holds a raw
        // pointer into its data, so the finalizer must not be allowed to run before this point.
        GC.KeepAlive(clip);
    }

    /// <summary>
    /// Drops the reference a voice holds on its clip's buffer.
    /// </summary>
    /// <remarks>
    /// Called when the voice is torn down or pointed at something else, not when it is stopped.
    /// Stopping only clears a flag, and the audio thread can still be part way through a block, so
    /// the buffer has to outlive that. Anything holding a clip past its last sound is bounded by the
    /// voices a source owns, which is bounded by <see cref="MaxOneShotVoices"/>.
    /// </remarks>
    private static void ReleaseVoiceClip(SourceInfo voice)
    {
        if (voice.clipHash == 0)
            return;

        AudioContext.ReleaseClipHandle(voice.clipHash);
        voice.clipHash = 0;
    }

    /// <summary>
    /// Plays procedurally generated audio using the Read event callback.
    /// </summary>
    public void PlayProcedural()
    {
        if (_soundGroup.pointer == IntPtr.Zero) return;
        if (_mainSource == null || _mainSource.handle == IntPtr.Zero) return;

        _mainSource.atEnd = false;
        MiniAudioExNative.ma_ex_audio_source_play_from_callback(_mainSource.handle, _proceduralProcessCallback, IntPtr.Zero);

        // The callback has replaced whatever clip was feeding this voice.
        ReleaseVoiceClip(_mainSource);
    }

    /// <summary>
    /// Stops playback and rewinds to the start. Use <see cref="Pause"/> to stop without losing the
    /// position.
    /// </summary>
    public void Stop()
    {
        if (_mainSource == null || _mainSource.handle == IntPtr.Zero) return;

        MiniAudioExNative.ma_ex_audio_source_stop(_mainSource.handle);
        MiniAudioExNative.ma_ex_audio_source_set_pcm_position(_mainSource.handle, 0);
        _mainSource.atEnd = false;
        _isPaused = false;
        _pausedCursor = 0;
    }

    /// <summary>
    /// Stops playback while remembering the position, so <see cref="Resume"/> picks up where it left
    /// off. Does nothing if the source is not playing.
    /// </summary>
    public void Pause()
    {
        if (_mainSource == null || _mainSource.handle == IntPtr.Zero) return;
        if (_isPaused || !IsPlaying) return;

        _pausedCursor = Cursor;
        _isPaused = true;
        MiniAudioExNative.ma_ex_audio_source_stop(_mainSource.handle);
    }

    /// <summary>
    /// Continues from where <see cref="Pause"/> stopped. Does nothing if the source is not paused.
    /// </summary>
    /// <remarks>
    /// The position is restored explicitly after starting rather than relying on the stopped source
    /// having kept it, so this behaves the same whether the native layer rewinds on stop or not.
    /// </remarks>
    public void Resume()
    {
        if (!_isPaused) return;

        ulong resumeFrom = _pausedCursor;
        _isPaused = false;
        _pausedCursor = 0;

        Play();
        Cursor = resumeFrom;
    }

    /// <summary>True while <see cref="Pause"/> is holding the position for a later <see cref="Resume"/>.</summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// Plays a clip on its own voice, without disturbing whatever this source is already playing.
    /// Overlapping calls layer instead of cutting each other off, which is what a one shot is for.
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="volumeScale">Level for this voice alone, on top of the source's own volume.</param>
    /// <remarks>
    /// Voices are pooled and share the source's sound group, so they inherit its volume, pitch and
    /// spatial settings and follow the object as it moves. Once <see cref="MaxOneShotVoices"/> are
    /// sounding at once the longest running one is taken over.
    /// </remarks>
    public void PlayOneShot(AudioClip clip, float volumeScale = 1.0f)
    {
        if (_soundGroup.pointer == IntPtr.Zero || clip == null) return;

        SourceInfo voice = AcquireOneShotVoice();

        if (voice == null) return;

        voice.atEnd = false;
        voice.startOrder = ++_oneShotCounter;

        MiniAudioExNative.ma_ex_audio_source_set_loop(voice.handle, 0);
        MiniAudioExNative.ma_ex_audio_source_set_volume(voice.handle, volumeScale);

        StartVoice(voice, clip);
    }

    /// <summary>Stops every one shot voice this source is sounding. Leaves the main playback alone.</summary>
    public void StopOneShots()
    {
        foreach (SourceInfo voice in _oneShots)
        {
            if (voice.handle != IntPtr.Zero)
                MiniAudioExNative.ma_ex_audio_source_stop(voice.handle);
        }
    }

    /// <summary>How many one shot voices are sounding right now.</summary>
    public int ActiveOneShotCount
    {
        get
        {
            // Voices from a closed device cannot be asked anything, and none of them are sounding.
            if (!NativeIsLive) return 0;

            int count = 0;

            foreach (SourceInfo voice in _oneShots)
            {
                if (voice.handle != IntPtr.Zero && MiniAudioExNative.ma_ex_audio_source_get_is_playing(voice.handle) > 0)
                    count++;
            }

            return count;
        }
    }

    /// <summary>
    /// Finds a voice that has finished, or makes one, or takes over the longest running voice once
    /// the pool is at its limit. Voices are kept rather than torn down so a rapid-fire emitter is not
    /// allocating native objects every shot.
    /// </summary>
    private SourceInfo AcquireOneShotVoice()
    {
        SourceInfo oldest = null;

        foreach (SourceInfo voice in _oneShots)
        {
            if (voice.handle == IntPtr.Zero)
                continue;

            if (MiniAudioExNative.ma_ex_audio_source_get_is_playing(voice.handle) == 0)
                return voice;

            if (oldest == null || voice.startOrder < oldest.startOrder)
                oldest = voice;
        }

        if (_oneShots.Count < Maths.Max(1, _maxOneShotVoices))
        {
            IntPtr handle = MiniAudioExNative.ma_ex_audio_source_init(AudioContext.NativeContext);

            if (handle == IntPtr.Zero)
                return oldest;

            MiniAudioExNative.ma_ex_audio_source_set_group(handle, _soundGroup.pointer);

            var created = new SourceInfo { handle = handle };
            _oneShots.Add(created);
            return created;
        }

        if (oldest != null)
            MiniAudioExNative.ma_ex_audio_source_stop(oldest.handle);

        return oldest;
    }

    /// <summary>
    /// Brings the pool down to the cap, quietest first: an idle voice costs nothing to take, so those
    /// go before anything still sounding, and only then the longest running, which is the order
    /// <see cref="AcquireOneShotVoice"/> steals in.
    /// </summary>
    private void TrimOneShotVoices()
    {
        _maxOneShotVoices = Maths.Max(1, _maxOneShotVoices);

        // Reachable from ApplySettings, so an inspector edit can land between a device closing and the
        // rebuild noticing. Those voices are already gone; the rebuild clears the list.
        if (!NativeIsLive) return;

        while (_oneShots.Count > _maxOneShotVoices)
        {
            SourceInfo victim = null;

            foreach (SourceInfo voice in _oneShots)
            {
                if (voice.handle == IntPtr.Zero || MiniAudioExNative.ma_ex_audio_source_get_is_playing(voice.handle) == 0)
                {
                    victim = voice;
                    break;
                }

                if (victim == null || voice.startOrder < victim.startOrder)
                    victim = voice;
            }

            if (victim == null) break;

            if (victim.handle != IntPtr.Zero)
            {
                MiniAudioExNative.ma_ex_audio_source_stop(victim.handle);
                MiniAudioExNative.ma_ex_audio_source_uninit(victim.handle);
            }

            ReleaseVoiceClip(victim);
            _oneShots.Remove(victim);
        }
    }

    private void DestroyOneShotVoices(bool live)
    {
        foreach (SourceInfo voice in _oneShots)
        {
            if (live && voice.handle != IntPtr.Zero)
            {
                MiniAudioExNative.ma_ex_audio_source_stop(voice.handle);
                MiniAudioExNative.ma_ex_audio_source_uninit(voice.handle);
            }

            ReleaseVoiceClip(voice);
        }

        _oneShots.Clear();
    }

    /// <summary>
    /// Plays a clip at a world position with no emitter of its own, for sounds whose source is gone by
    /// the time they finish, like an impact or a pickup. Returns null if there is no scene to put it in.
    /// </summary>
    public static AudioSource PlayClipAtPoint(AudioClip clip, Float3 position, float volume = 1.0f)
    {
        if (clip == null) return null;

        // The temporary object is destroyed by the End event, which only fires while audio is running.
        // Without a device it would never fire, so a headless run would leak an object per call.
        if (!AudioContext.IsInitialized)
            return null;

        Scene scene = Scene.Current;

        if (scene == null)
        {
            Debug.LogWarning("PlayClipAtPoint needs a loaded scene to place the sound in.");
            return null;
        }

        var go = new GameObject($"One Shot Audio ({clip.Name})");
        go.Transform.Position = position;

        AudioSource source = go.AddComponent<AudioSource>();
        source.Volume = volume;
        source.Spatial = true;
        source.Clip = clip;

        scene.Add(go);

        // Added to the scene first: OnEnable is what creates the native source Play needs.
        source.End += () => go.Destroy();
        source.Play();

        // Nothing started, so nothing will ever raise End to clean this up.
        if (!source.IsPlaying)
        {
            go.Destroy();
            return null;
        }

        return source;
    }

    #endregion

    #region Effects Management

    /// <summary>The effect chain, in processing order.</summary>
    public IReadOnlyList<AudioEffect> Effects => _effects;

    /// <summary>
    /// Adds an audio effect to the processing chain. The source takes ownership: the effect is
    /// destroyed when it is removed from the chain or when the source itself is destroyed. Disabling
    /// and re-enabling the source keeps the chain intact.
    /// </summary>
    public void AddEffect(AudioEffect effect)
    {
        if (effect == null) return;

        if (!effect.TryClaim(this))
        {
            Debug.LogError($"[{Name}] That audio effect is already in another chain. An effect carries " +
                           "its own filter and delay state, so it can only be in one.");
            return;
        }

        _effects.Add(effect);
        effect.Initialize(AudioContext.SampleRate, AudioContext.Channels);
        _chain.Publish(_effects);
    }

    /// <summary>
    /// Removes an audio effect from the processing chain and destroys it.
    /// </summary>
    public void RemoveEffect(AudioEffect effect)
    {
        if (effect == null) return;

        if (!_effects.Remove(effect)) return;

        _chain.Publish(_effects);
        effect.OnDestroy();
    }

    /// <summary>
    /// Removes an audio effect by index and destroys it.
    /// </summary>
    public void RemoveEffect(int index)
    {
        if (index < 0 || index >= _effects.Count) return;

        RemoveEffect(_effects[index]);
    }

    /// <summary>
    /// Removes and destroys every audio effect in the chain.
    /// </summary>
    public void ClearEffects()
    {
        AudioEffect[] removed = _effects.ToArray();
        _effects.Clear();
        _chain.Publish(_effects);

        foreach (AudioEffect effect in removed)
            effect?.OnDestroy();
    }

    /// <summary>
    /// Binds every effect to the current audio format and republishes the chain the audio thread
    /// reads. Call after editing <see cref="Effects"/> in place.
    /// </summary>
    public void RefreshEffects()
    {
        foreach (AudioEffect effect in _effects)
        {
            if (effect == null) continue;

            if (!effect.IsInitialized)
            // An effect that belongs to another chain is taken back out rather than left in a list
            // that claims to own it. Assigning one in two places is the inspector's easiest mistake.
            if (!effect.TryClaim(this))
            {
                Debug.LogError($"[{Name}] An audio effect in this chain already belongs to another one, " +
                               "so it has been removed. An effect carries its own state and can only be in one chain.");
                _effects.RemoveAt(i);
                continue;
            }

                effect.Initialize(AudioContext.SampleRate, AudioContext.Channels);
            else
                effect.OnValidate();
        }

        _chain.Publish(_effects);
    }

    /// <summary>
    /// Gets the number of active effects.
    /// </summary>
    public int EffectCount => _effects.Count;

    #endregion

    #region Utility Methods

    /// <summary>
    /// The world space velocity the doppler shift is being driven from, in units per real second.
    /// </summary>
    /// <remarks>
    /// This used to difference the sound group's position against the previous position that Update
    /// had already overwritten with it, so it always answered roughly zero, and divided by a delta it
    /// never checked. It now reports the value Update actually handed the audio engine.
    /// </remarks>
    public Float3 GetCalculatedVelocity()
    {
        return _velocity;
    }

    /// <summary>
    /// Gets the output buffer after effect processing (useful for FFT analysis).
    /// </summary>
    public bool GetOutputBuffer(ref float[] buffer, out int length)
    {
        if (_outputBuffer != null)
        {
            length = _outputBuffer.Read(ref buffer);
            return length > 0;
        }

        length = 0;
        return false;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Points this source's output at its mixer group, or at the engine endpoint when it has none.
    /// The group builds its own native node on demand, so this works whether the group has been used
    /// before or not.
    /// </summary>
    /// <summary>Whatever runs last in this source is what feeds the bus: the effect node when it came
    /// up, the sound group itself otherwise. A failed effect node costs the effects, not the routing.</summary>
    private IntPtr OutputTail => _effectNodeReady && _effectNode.pointer != IntPtr.Zero
        ? _effectNode.pointer
        : _soundGroup.pointer;

    private void RouteToOutputGroup()
    {
        if (!AudioContext.IsInitialized || _soundGroup.pointer == IntPtr.Zero)
            return;

        AudioMixerGroup group = _outputGroup.Res;
        IntPtr target = group.IsValid() ? group.NativeNode : IntPtr.Zero;
        int generation = group.IsValid() ? group.NodeGeneration : 0;

        if (target == IntPtr.Zero)
        {
            var engine = new ma_engine_ptr(MiniAudioExNative.ma_ex_context_get_engine(AudioContext.NativeContext));
            target = MiniAudioNative.ma_engine_get_endpoint(engine).pointer;

            // Straight to the endpoint, so there is no bus to follow. A group that exists but could
            // not build a node is in the same position as no group at all.
            group = null;
            generation = 0;
        }

        // OnValidate runs for every field, so without this a volume drag re-attaches the node graph
        // once per frame for no reason. The generation is part of the test because a bus that has
        // been torn down and rebuilt can land on the address its old node had.
        if (target == _routedTo && ReferenceEquals(group, _routedGroup) && generation == _routedGeneration)
            return;

        FollowGroup(group);

        MiniAudioNative.ma_node_attach_output_bus(new ma_node_ptr(OutputTail), 0, new ma_node_ptr(target), 0);
        _routedTo = target;
        _routedGeneration = generation;
    }

    /// <summary>
    /// True when what this source is attached to is no longer what it was told to feed, which is the
    /// one thing nothing else reports.
    /// </summary>
    /// <remarks>
    /// A bus rebuilds its node when the device changes, and its whole object is replaced when the
    /// mixer is reimported. Neither is something a source can see from the pointer it attached to.
    /// </remarks>
    private bool RoutingIsStale()
    {
        // Attached straight to the engine endpoint, which only moves when the device does, and that
        // is handled by the rebuild in Update.
        if (_routedGroup is null)
            return false;

        // The group object itself has gone, which is what a mixer reimport does to every one of them.
        if (_routedGroup.IsNotValid())
            return true;

        return _routedGroup.NodeGeneration != _routedGeneration;
    }

    /// <summary>Subscribes to the bus this source now feeds, and stops following the previous one.</summary>
    private void FollowGroup(AudioMixerGroup group)
    {
        if (ReferenceEquals(group, _routedGroup))
            return;

        // Not guarded on validity: a disposed group can still be holding this handler, and leaving it
        // there keeps the source alive behind it.
        if (_routedGroup is not null)
            _routedGroup.NativeReleasing -= OnOutputGroupReleasing;

        _routedGroup = group;

        if (_routedGroup is not null)
            _routedGroup.NativeReleasing += OnOutputGroupReleasing;
    }

    /// <summary>
    /// The bus this source feeds is tearing its node down. Let go now, while the call still lands on
    /// a node that exists, and let <see cref="Update"/> put this source on the rebuilt one.
    /// </summary>
    private void OnOutputGroupReleasing()
    {
        if (_soundGroup.pointer == IntPtr.Zero)
            return;

        MiniAudioNative.ma_node_detach_output_bus(new ma_node_ptr(OutputTail), 0);

        // Detached rather than moved to the endpoint on purpose: a source whose bus is muted or
        // quiet must not jump to full volume on the master for the frame it takes to be re-routed.
        _routedTo = IntPtr.Zero;
    }

    /// <summary>Stops following whatever bus this source was attached to.</summary>
    private void ClearRouting()
    {
        FollowGroup(null);
        _routedTo = IntPtr.Zero;
        _routedGeneration = 0;
    }

    private void ApplySettings()
    {
        // First, and outside the guard below: the inspector and the deserializer write these fields
        // directly rather than through the setters that check them, so this is where an inverted
        // distance range, an impossible pitch or a lowered voice cap takes effect, device or not.
        _volume = Sane(_volume, 0.0f, float.MaxValue, 1.0f);
        _pitch = Sane(_pitch, MinPitch, float.MaxValue, 1.0f);
        _pan = Sane(_pan, -1.0f, 1.0f, 0.0f);
        _dopplerFactor = Sane(_dopplerFactor, 0.0f, float.MaxValue, 1.0f);

        ApplyDistances();
        TrimOneShotVoices();

        if (_soundGroup.pointer == IntPtr.Zero) return;

        MiniAudioNative.ma_sound_group_set_volume(_soundGroup, _volume);
        MiniAudioNative.ma_sound_group_set_pitch(_soundGroup, _pitch);
        MiniAudioNative.ma_sound_group_set_pan(_soundGroup, _pan);
        MiniAudioNative.ma_sound_group_set_pan_mode(_soundGroup, (ma_pan_mode)_panMode);
        MiniAudioNative.ma_sound_group_set_spatialization_enabled(_soundGroup, _spatial ? (uint)1 : 0);
        MiniAudioNative.ma_sound_group_set_doppler_factor(_soundGroup, _dopplerFactor);
        MiniAudioNative.ma_sound_group_set_attenuation_model(_soundGroup, (ma_attenuation_model)_attenuationModel);

        if (_mainSource != null && _mainSource.handle != IntPtr.Zero)
            MiniAudioExNative.ma_ex_audio_source_set_loop(_mainSource.handle, _loop ? (uint)1 : 0);
    }

    // Native calls this on the audio thread, so an exception leaving it is undefined behaviour and in
    // practice takes the process down with no usable stack. A user effect throwing, or an effect
    // changing the frame count so a buffer copy no longer fits, has to cost silence and one log line.
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
            UInt32 countOut = _chain.Process(framesIn[0], *frameCountIn, framesOut[0], *frameCountOut, channels);

            if (Process != null)
            {
                var bufferIn = new NativeArray<float>(framesIn[0], (int)(*frameCountIn * channels));
                var bufferOut = new NativeArray<float>(framesOut[0], (int)(countOut * channels));

                Process.Invoke(bufferIn, *frameCountIn, bufferOut, ref countOut, channels);

                // A subscriber gets the same say an effect does, within the same limit.
                if (countOut > *frameCountOut)
                    countOut = *frameCountOut;
            }

            *frameCountOut = countOut;

            _outputBuffer.Write(new NativeArray<float>(framesOut[0], (int)(countOut * channels)));
        }
        catch (Exception ex)
        {
            // Flagged rather than logged every callback: a permanently broken effect would otherwise
            // build a message thousands of times a second on the audio thread.
            if (!_effectProcessFailed)
            {
                _effectProcessFailed = true;
                Debug.LogError($"[{Name}] Audio effect processing threw and is now producing silence: {ex}");
            }

            if (framesOut != null && framesOut[0] != null)
                new Span<float>(framesOut[0], (int)(*frameCountOut * channels)).Clear();
        }
    }

    private unsafe void OnProceduralProcess(IntPtr pUserData, IntPtr pFramesOut, UInt64 frameCount, UInt32 channels)
    {
        int length = (int)(frameCount * channels);

        try
        {
            NativeArray<float> framesOut = new NativeArray<float>(pFramesOut, length);
            Read?.Invoke(framesOut, frameCount, (int)channels);
        }
        catch (Exception ex)
        {
            if (!_proceduralProcessFailed)
            {
                _proceduralProcessFailed = true;
                Debug.LogError($"[{Name}] Procedural audio generation threw and is now producing silence: {ex}");
            }

            if (pFramesOut != IntPtr.Zero)
                new Span<float>((void*)pFramesOut, length).Clear();
        }
    }

    #endregion

    #region Serialization Callbacks

    public override void OnAfterDeserialize()
    {
        base.OnAfterDeserialize();

        // The loaded values are only in the fields at this point, so push them at the native side for
        // the paths that deserialize into an already-running component (clipboard paste, undo, prefab
        // revert). ApplySettings is a no-op while there is no sound group.
        ApplySettings();

        // Deserialized effects arrive with their parameters but no DSP state, so they have to be
        // bound to the audio format before the chain the audio thread reads is published.
        RefreshEffects();
    }

    #endregion
}
