// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

using Prowl.Echo;
using Prowl.Runtime.Audio;
using Prowl.Runtime.Audio.Native;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Video;

[AddComponentMenu("Video/Video Player")]
[ComponentIcon("\uf008")]
public class VideoPlayer : Component
{
    private bool HasNoClip => Clip.IsNotValid();

    [SerializeField] public VideoClip? Clip;

    [ShowIf(nameof(HasNoClip))]
    [SerializeField] public string Url = string.Empty;

    [SerializeField] public bool PlayOnAwake = true;
    [SerializeField] public bool IsLooping = false;

    [SerializeField] public Material? TargetMaterial;
    [SerializeField] public string TargetTextureProperty = "_MainTex";

    [SerializeIgnore] private FFmpegVideoDecoder? _decoder;
    [SerializeIgnore] private Texture2D? _videoTex;
    public Texture2D? VideoTexture => _videoTex;
    [SerializeIgnore] private double _playbackClock;
    [SerializeIgnore] private bool _isPlaying;
    [SerializeIgnore] private bool _isPaused;

    // Background Threading & Queues
    [SerializeIgnore] private Thread? _decodeThread;
    [SerializeIgnore] private volatile bool _stopDecoding;
    [SerializeIgnore] private readonly ConcurrentQueue<VideoFrame> _videoQueue = new();
    [SerializeIgnore] private readonly ConcurrentQueue<float[]> _audioQueue = new();
    [SerializeIgnore] private VideoFrame? _nextVideoFrame;

    // Miniaudio
    [SerializeIgnore] private ma_procedural_data_source_proc? _audioReadDelegate;
    [SerializeIgnore] private GCHandle _gcHandle;
    [SerializeIgnore] private ma_engine_ptr _ownEnginePtr;
    [SerializeIgnore] private bool _ownsAudioEngine;
    [SerializeIgnore] private ma_sound_ptr _soundPtr;
    [SerializeIgnore] private float[] _leftoverAudio = [];
    [SerializeIgnore] private int _leftoverPos;

    public bool IsPlaying => _isPlaying && !_isPaused;

    public override void OnEnable()
    {
        if (Application.IsPlaying && PlayOnAwake)
            Play();
    }

    public override void OnDisable() => Stop();

    public void Play()
    {
        if (Application.IsHeadless || !FFmpegLoader.IsAvailable) return;

        if (_isPaused && _isPlaying)
        {
            _isPaused = false;
            if (_decoder != null && _decoder.HasAudio && _soundPtr.pointer != IntPtr.Zero)
                MiniAudioNative.ma_sound_start(_soundPtr);
            return;
        }

        ReleaseDecoder();

        _decoder = new FFmpegVideoDecoder(_audioQueue);
        bool opened = false;

        if (!string.IsNullOrWhiteSpace(Url))
        {
            string resolvedPath = Path.IsPathRooted(Url) ? Url : Path.Combine(Application.DataPath, Url);
            opened = File.Exists(resolvedPath) && _decoder.TryOpenFile(resolvedPath);
        }
        else if (Clip.IsValid())
        {
            if (!string.IsNullOrEmpty(Clip.StreamFilePath) && File.Exists(Clip.StreamFilePath))
                opened = _decoder.TryOpenFile(Clip.StreamFilePath);
            else if (Clip.EncodedData is { Length: > 0 } data)
                opened = _decoder.TryOpenMemory(data);
        }

        if (!opened)
        {
            Debug.LogError($"[VideoPlayer] Failed to open video source on {GameObject.Name}");
            ReleaseDecoder();
            return;
        }

        EnsureTargetTexture(_decoder.Width, _decoder.Height);
        _playbackClock = 0.0;
        _isPlaying = true;
        _isPaused = false;

        // Boot Miniaudio
        if (_decoder.HasAudio)
        {
            ma_engine_ptr enginePtr = ResolveAudioEngine();
            if (enginePtr.pointer != IntPtr.Zero)
            {
                _audioReadDelegate = OnAudioRead;
                _gcHandle = GCHandle.Alloc(this);

                ma_procedural_data_source_config config = MiniAudioNative.ma_procedural_data_source_config_init(
                    ma_format.f32,
                    (uint)_decoder.AudioChannels,
                    (uint)_decoder.AudioSampleRate,
                    _audioReadDelegate,
                    GCHandle.ToIntPtr(_gcHandle)
                );

                _soundPtr = new ma_sound_ptr(true);

                ma_result res = MiniAudioNative.ma_sound_init_from_callback(
                    enginePtr,
                    ref config,
                    ma_sound_flags.no_spatialization | ma_sound_flags.no_pitch,
                    new ma_sound_group_ptr(),
                    new ma_fence_ptr(),
                    _soundPtr
                );

                if (res == ma_result.success)
                {
                    MiniAudioNative.ma_sound_start(_soundPtr);
                }
                else
                {
                    _soundPtr.Free();
                }
            }
        }

        _stopDecoding = false;
        _decodeThread = new Thread(DecodeWorker) { IsBackground = true, Name = "VideoDecoder" };
        _decodeThread.Start();
    }

    public void Pause()
    {
        if (!_isPlaying || _isPaused) return;
        _isPaused = true;

        if (_decoder != null && _decoder.HasAudio && _soundPtr.pointer != IntPtr.Zero)
            MiniAudioNative.ma_sound_stop(_soundPtr);
    }

    public void Stop()
    {
        _isPlaying = false;
        _isPaused = false;
        _stopDecoding = true;
        _decodeThread?.Join(100);

        _videoQueue.Clear();
        _audioQueue.Clear();
        _leftoverAudio = [];
        _leftoverPos = 0;
        _nextVideoFrame = null;

        ReleaseDecoder();
    }

    public void Seek(double timeSeconds)
    {
        if (!_isPlaying || _decoder == null) return;

        _stopDecoding = true;
        _decodeThread?.Join();

        _videoQueue.Clear();
        _audioQueue.Clear();
        _leftoverAudio = [];
        _leftoverPos = 0;
        _nextVideoFrame = null;

        _decoder.Seek(timeSeconds);
        _playbackClock = Math.Max(0.0, timeSeconds);

        if (_decoder.HasAudio && _soundPtr.pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_sound_seek_to_pcm_frame(_soundPtr, 0);
        }

        _stopDecoding = false;
        _decodeThread = new Thread(DecodeWorker) { IsBackground = true, Name = "VideoDecoder" };
        _decodeThread.Start();
    }

    public override void Update()
    {
        if (!_isPlaying || _isPaused || _decoder == null || Application.IsPaused) return;

        if (_decoder.HasAudio && _soundPtr.pointer != IntPtr.Zero)
            _playbackClock = MiniAudioNative.ma_sound_get_time_in_milliseconds(_soundPtr) / 1000.0;
        else
            _playbackClock += Prowl.Runtime.Time.DeltaTime;

        VideoFrame? frameToDraw = null;

        while (_nextVideoFrame == null || _nextVideoFrame.Time <= _playbackClock)
        {
            if (_nextVideoFrame != null)
                frameToDraw = _nextVideoFrame;

            if (_videoQueue.TryDequeue(out VideoFrame? next))
            {
                _nextVideoFrame = next;
            }
            else if (_stopDecoding && _videoQueue.IsEmpty)
            {
                if (IsLooping)
                {
                    Seek(0.0);
                    if (_decoder.HasAudio && _soundPtr.pointer != IntPtr.Zero)
                        MiniAudioNative.ma_sound_start(_soundPtr);
                }
                else
                {
                    Stop();
                }
                break;
            }
            else
            {
                break;
            }
        }

        if (frameToDraw != null && _videoTex.IsValid())
        {
            _videoTex.SetData<byte>(frameToDraw.Data.AsMemory());

            if (TargetMaterial.IsValid() && !string.IsNullOrEmpty(TargetTextureProperty))
            {
                TargetMaterial.SetTexture(TargetTextureProperty, _videoTex);
            }
        }
    }

    private void DecodeWorker()
    {
        while (!_stopDecoding)
        {
            if (_videoQueue.Count > 12 || _audioQueue.Count > 30)
            {
                Thread.Sleep(5);
                continue;
            }

            if (_decoder == null) break;
            VideoFrame? frame = _decoder.DecodeNextVideoFrame(out bool isEOF);
            if (frame != null)
                _videoQueue.Enqueue(frame);
            if (isEOF)
                _stopDecoding = true;
        }
    }

    private static unsafe void OnAudioRead(IntPtr pUserData, IntPtr pFramesOut, ulong frameCount, uint channels)
    {
        if (pUserData == IntPtr.Zero || pFramesOut == IntPtr.Zero) return;

        object? target = GCHandle.FromIntPtr(pUserData).Target;
        if (target is not VideoPlayer player) return;

        float* outBuf = (float*)pFramesOut;
        int samplesNeeded = (int)frameCount * (int)channels;
        int written = 0;

        if (player._leftoverPos < player._leftoverAudio.Length)
        {
            int toCopy = Math.Min(samplesNeeded, player._leftoverAudio.Length - player._leftoverPos);
            Marshal.Copy(player._leftoverAudio, player._leftoverPos, (IntPtr)outBuf, toCopy);
            written += toCopy;
            player._leftoverPos += toCopy;
        }

        while (written < samplesNeeded)
        {
            if (player._audioQueue.TryDequeue(out float[]? packet))
            {
                int toCopy = Math.Min(samplesNeeded - written, packet.Length);
                Marshal.Copy(packet, 0, (IntPtr)(outBuf + written), toCopy);
                written += toCopy;

                if (toCopy < packet.Length)
                {
                    player._leftoverAudio = packet;
                    player._leftoverPos = toCopy;
                }
            }
            else
            {
                break;
            }
        }

        for (int i = written; i < samplesNeeded; i++)
            outBuf[i] = 0f;
    }

    private ma_engine_ptr ResolveAudioEngine()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (FieldInfo field in typeof(AudioContext).GetFields(flags))
        {
            if (field.FieldType == typeof(ma_engine_ptr) && field.GetValue(null) is ma_engine_ptr engine && engine.pointer != IntPtr.Zero)
                return engine;
        }
        foreach (PropertyInfo prop in typeof(AudioContext).GetProperties(flags))
        {
            if (prop.PropertyType == typeof(ma_engine_ptr) && prop.GetValue(null) is ma_engine_ptr engine && engine.pointer != IntPtr.Zero)
                return engine;
        }

        _ownEnginePtr = new ma_engine_ptr(true);
        if (MiniAudioNative.ma_engine_init(_ownEnginePtr) == ma_result.success)
        {
            _ownsAudioEngine = true;
            return _ownEnginePtr;
        }

        _ownEnginePtr.Free();
        return default;
    }

    private void EnsureTargetTexture(int width, int height)
    {
        if (_videoTex.IsValid() && _videoTex.Width == width && _videoTex.Height == height)
            return;

        DisposeTextures();

        TextureFormat format = Enum.TryParse("RGBA8", ignoreCase: true, out TextureFormat fmt) ? fmt : default;

        _videoTex = new Texture2D((uint)width, (uint)height, format, false);
    }

    private void DisposeTextures()
    {
        if (_videoTex.IsValid())
        {
            _videoTex.Dispose();
            _videoTex = null;
        }
    }

    private void ReleaseDecoder()
    {
        if (_soundPtr.pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_sound_stop(_soundPtr);
            MiniAudioNative.ma_sound_uninit(_soundPtr);
            _soundPtr.Free();
        }

        if (_ownsAudioEngine && _ownEnginePtr.pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_engine_uninit(_ownEnginePtr);
            _ownEnginePtr.Free();
            _ownsAudioEngine = false;
        }

        if (_gcHandle.IsAllocated)
            _gcHandle.Free();

        _audioReadDelegate = null;
        _decoder?.Dispose();
        _decoder = null;
    }

    protected override void OnDispose()
    {
        Stop();
        DisposeTextures();
        base.OnDispose();
    }
}
