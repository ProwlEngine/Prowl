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
    /// <summary>
    /// Static flag to control whether AudioSources should resume their playback position on deserialization.
    /// If false, all AudioSources will start from the beginning when loaded.
    /// </summary>
    public static bool ResumePositionOnLoad = true;

    private class SourceInfo
    {
        public IntPtr handle;
        public bool atEnd;
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

    // Playback state (serialized for resume support)
    [SerializeField, HideInInspector]
    private ulong _savedCursor = 0;
    [SerializeField, HideInInspector]
    private bool _wasPlaying = false;

    // Native handles
    private SourceInfo _mainSource;
    private ma_sound_group_ptr _soundGroup;
    private ma_effect_node_ptr _effectNode;
    private Float3 _previousPosition;
    private ma_effect_node_process_proc _onEffectNodeProcess;
    private ma_procedural_data_source_proc _proceduralProcessCallback;

    // Effects and buffers
    private ConcurrentList<IAudioEffect> _effects = new();
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
    /// The AudioClip to play.
    /// </summary>
    public AudioClip? Clip
    {
        get => _clip.Res;
        set
        {
            _clip = value;
            if (_mainSource != null && _clip.Res != null && _playOnStart)
            {
                Play();
            }
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
    /// Volume of the audio source (0.0 to 1.0+).
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_volume(_soundGroup, value);
        }
    }

    /// <summary>
    /// Pitch of the audio source. 1.0 is normal pitch.
    /// </summary>
    public float Pitch
    {
        get => _pitch;
        set
        {
            _pitch = value;
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_pitch(_soundGroup, value);
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
            _pan = value;
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_pan(_soundGroup, value);
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
    /// Doppler effect intensity for spatial audio.
    /// </summary>
    public float DopplerFactor
    {
        get => _dopplerFactor;
        set
        {
            _dopplerFactor = value;
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_doppler_factor(_soundGroup, value);
        }
    }

    /// <summary>
    /// Minimum distance for spatial audio attenuation.
    /// </summary>
    public float MinDistance
    {
        get => _minDistance;
        set
        {
            _minDistance = value;
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_min_distance(_soundGroup, value);
        }
    }

    /// <summary>
    /// Maximum distance for spatial audio attenuation.
    /// </summary>
    public float MaxDistance
    {
        get => _maxDistance;
        set
        {
            _maxDistance = value;
            if (_soundGroup.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_group_set_max_distance(_soundGroup, value);
        }
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
    /// Gets or sets the current playback position in PCM samples.
    /// </summary>
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
    /// Gets the total length of the current audio clip in PCM samples.
    /// </summary>
    public ulong Length
    {
        get
        {
            if (_mainSource == null || _mainSource.handle == IntPtr.Zero)
                return 0;
            return MiniAudioExNative.ma_ex_audio_source_get_pcm_length(_mainSource.handle);
        }
    }

    #endregion

    #region MonoBehaviour Lifecycle

    public override void OnEnable()
    {
        // No device means no native objects to hand a null context to. Every playback entry point
        // already no-ops on a null sound group, so the component stays inert but usable.
        if (!AudioContext.IsInitialized)
        {
            Debug.LogWarningOnce("Audio.NoContext", "No audio device is initialized, audio components stay inactive.");
            return;
        }

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

            if (MiniAudioNative.ma_effect_node_init(MiniAudioNative.ma_engine_get_node_graph(pEngine), ref effectNodeConfig, _effectNode) == ma_result.success)
            {
                MiniAudioNative.ma_node_attach_output_bus(new ma_node_ptr(_effectNode.pointer), 0, MiniAudioNative.ma_engine_get_endpoint(pEngine), 0);
                MiniAudioNative.ma_node_attach_output_bus(new ma_node_ptr(_soundGroup.pointer), 0, new ma_node_ptr(_effectNode.pointer), 0);
            }

            // Apply all serialized settings
            ApplySettings();

            // Handle playback. If the clip is still streaming in (async loading), defer the
            // auto-play until it arrives (see Update) instead of silently never playing.
            if (!TryAutoPlay() && (_playOnStart || (ResumePositionOnLoad && _wasPlaying && _savedCursor > 0)))
                _pendingAutoPlay = true;
        }
    }

    /// <summary>Perform the OnEnable auto-play (resume or play-on-start) if the clip is loaded.
    /// Returns false if the clip hasn't streamed in yet so the caller can defer.</summary>
    private bool TryAutoPlay()
    {
        if (_clip.Res == null) return false;

        if (ResumePositionOnLoad && _wasPlaying && _savedCursor > 0)
        {
            Play();
            Cursor = _savedCursor;
        }
        else if (_playOnStart)
        {
            Play();
        }
        return true;
    }

    public override void Update()
    {
        if (_soundGroup.pointer == IntPtr.Zero) return;

        // Deferred auto-play: the clip was still streaming in at OnEnable; play once it arrives.
        if (_pendingAutoPlay && TryAutoPlay())
            _pendingAutoPlay = false;

        // Update spatial audio properties based on transform
        if (_spatial)
        {
            var pos = Transform.Position;
            MiniAudioNative.ma_sound_group_set_position(_soundGroup, (float)pos.X, (float)pos.Y, (float)pos.Z);

            var forward = Transform.Forward;
            MiniAudioNative.ma_sound_group_set_direction(_soundGroup, (float)forward.X, (float)forward.Y, (float)forward.Z);

            // Calculate velocity based on position change
            float deltaTime = Time.DeltaTime;
            if (deltaTime > 0)
            {
                Float3 velocity = (pos - _previousPosition) / deltaTime;
                MiniAudioNative.ma_sound_group_set_velocity(_soundGroup, (float)velocity.X, (float)velocity.Y, (float)velocity.Z);
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

    /// <summary>The inspector writes the backing fields directly, so the native side has to be
    /// re-synced from them rather than from the property setters.</summary>
    public override void OnValidate() => ApplySettings();

    public override void OnDisable()
    {
        // Save state for serialization
        if (_mainSource != null && _mainSource.handle != IntPtr.Zero)
        {
            _wasPlaying = IsPlaying;
            _savedCursor = Cursor;

            // Stop playback
            MiniAudioExNative.ma_ex_audio_source_stop(_mainSource.handle);
            MiniAudioExNative.ma_ex_audio_source_uninit(_mainSource.handle);
            _mainSource = null;
        }

        // Cleanup effect node
        if (_effectNode.pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_effect_node_uninit(_effectNode);
            _effectNode.Free();
        }

        // Cleanup sound group
        if (_soundGroup.pointer != IntPtr.Zero)
        {
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
        MiniAudioExNative.ma_ex_audio_source_set_loop(_mainSource.handle, _loop ? (uint)1 : 0);

        if (_clip.Res.Handle != IntPtr.Zero)
            MiniAudioExNative.ma_ex_audio_source_play_from_memory(_mainSource.handle, _clip.Res.Handle, _clip.Res.DataSize);
        else
            MiniAudioExNative.ma_ex_audio_source_play_from_file(_mainSource.handle, _clip.Res.FilePath, _clip.Res.StreamFromDisk ? (uint)1 : 0);
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
    }

    /// <summary>
    /// Stops playback. The cursor position is maintained (can be used as pause).
    /// </summary>
    public void Stop()
    {
        if (_mainSource != null && _mainSource.handle != IntPtr.Zero)
        {
            MiniAudioExNative.ma_ex_audio_source_stop(_mainSource.handle);
            _mainSource.atEnd = false;
        }
    }

    /// <summary>
    /// Plays the given clip as a one-shot (fire and forget).
    /// Note: This uses the main source, so it will interrupt currently playing audio.
    /// </summary>
    public void PlayOneShot(AudioClip clip)
    {
        if (_soundGroup.pointer == IntPtr.Zero || clip == null) return;
        if (_mainSource == null || _mainSource.handle == IntPtr.Zero) return;

        // Stop current playback and reset
        if (IsPlaying)
        {
            MiniAudioExNative.ma_ex_audio_source_stop(_mainSource.handle);
            MiniAudioExNative.ma_ex_audio_source_set_pcm_position(_mainSource.handle, 0);
        }

        _mainSource.atEnd = false;

        if (clip.Handle != IntPtr.Zero)
            MiniAudioExNative.ma_ex_audio_source_play_from_memory(_mainSource.handle, clip.Handle, clip.DataSize);
        else
            MiniAudioExNative.ma_ex_audio_source_play_from_file(_mainSource.handle, clip.FilePath, clip.StreamFromDisk ? (uint)1 : 0);
    }

    #endregion

    #region Effects Management

    /// <summary>
    /// Adds an audio effect to the processing chain. The source takes ownership: the effect is
    /// destroyed when it is removed from the chain or when the source itself is destroyed. Disabling
    /// and re-enabling the source keeps the chain intact.
    /// </summary>
    public void AddEffect(IAudioEffect effect)
    {
        if (effect == null) return;
        _effects.Add(effect);
    }

    /// <summary>
    /// Removes an audio effect from the processing chain and destroys it.
    /// </summary>
    public void RemoveEffect(IAudioEffect effect)
    {
        if (effect == null) return;

        if (_effects.Remove(effect))
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
        foreach (IAudioEffect effect in _effects.TakeAll())
            effect.OnDestroy();
    }

    /// <summary>
    /// Gets the number of active effects.
    /// </summary>
    public int EffectCount => _effects.Count;

    #endregion

    #region Utility Methods

    /// <summary>
    /// Gets the calculated velocity based on position changes.
    /// </summary>
    public Float3 GetCalculatedVelocity()
    {
        if (_soundGroup.pointer == IntPtr.Zero)
            return new Float3(0, 0, 0);

        float deltaTime = AudioContext.DeltaTime;
        var result = MiniAudioNative.ma_sound_group_get_position(_soundGroup);
        Float3 currentPosition = new Float3(result.x, result.y, result.z);

        float dx = currentPosition.X - _previousPosition.X;
        float dy = currentPosition.Y - _previousPosition.Y;
        float dz = currentPosition.Z - _previousPosition.Z;

        return new Float3(dx / deltaTime, dy / deltaTime, dz / deltaTime);
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

    private void ApplySettings()
    {
        if (_soundGroup.pointer == IntPtr.Zero) return;

        MiniAudioNative.ma_sound_group_set_volume(_soundGroup, _volume);
        MiniAudioNative.ma_sound_group_set_pitch(_soundGroup, _pitch);
        MiniAudioNative.ma_sound_group_set_pan(_soundGroup, _pan);
        MiniAudioNative.ma_sound_group_set_pan_mode(_soundGroup, (ma_pan_mode)_panMode);
        MiniAudioNative.ma_sound_group_set_spatialization_enabled(_soundGroup, _spatial ? (uint)1 : 0);
        MiniAudioNative.ma_sound_group_set_doppler_factor(_soundGroup, _dopplerFactor);
        MiniAudioNative.ma_sound_group_set_min_distance(_soundGroup, _minDistance);
        MiniAudioNative.ma_sound_group_set_max_distance(_soundGroup, _maxDistance);
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
            NativeArray<float> bufferIn = new NativeArray<float>(framesIn[0], (int)(*frameCountIn * channels));
            NativeArray<float> bufferOut = new NativeArray<float>(framesOut[0], (int)(*frameCountOut * channels));

            // Just in case we end up with no sound at all because no effects were active (prevents silence)
            bufferIn.CopyTo(bufferOut);

            // An effect can modify the number of frames it processes so we need to keep track of this
            UInt32 countIn = *frameCountIn;
            UInt32 countOut = *frameCountOut;

            for (int i = 0; i < _effects.Count; i++)
            {
                _effects[i].OnProcess(bufferIn, countIn, bufferOut, ref countOut, channels);

                //Since effects processing is like a stack, the output needs to be copied to the input for the next effect
                bufferOut.CopyTo(bufferIn);

                countIn = countOut;
            }

            Process?.Invoke(bufferIn, countIn, bufferOut, ref countOut, pEffectNode->config.channels);

            *frameCountOut = countOut;

            _outputBuffer.Write(new NativeArray<float>(framesOut[0], (int)(*frameCountOut * channels)));
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

    public override void OnBeforeSerialize()
    {
        base.OnBeforeSerialize();

        bool live = ResumePositionOnLoad && _mainSource != null && _mainSource.handle != IntPtr.Zero;
        _savedCursor = live ? Cursor : 0;
        _wasPlaying = live && IsPlaying;
    }

    public override void OnAfterDeserialize()
    {
        base.OnAfterDeserialize();

        // The loaded values are only in the fields at this point, so push them at the native side for
        // the paths that deserialize into an already-running component (clipboard paste, undo, prefab
        // revert). ApplySettings is a no-op while there is no sound group.
        ApplySettings();
    }

    #endregion
}
