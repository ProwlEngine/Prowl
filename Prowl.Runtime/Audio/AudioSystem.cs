// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Audio.Null;
using Prowl.Runtime.Audio.OpenAL;

namespace Prowl.Runtime.Audio;

public static class AudioSystem
{
    private const uint InitialFreeSources = 2;

    private static AudioEngine _engine;
    private static readonly Dictionary<AudioClip, AudioBuffer> _buffers = [];

    private static readonly List<ActiveAudio> _active = [];
    private static readonly List<ActiveAudio> _pool = [];
    private static AudioListener? s_listener;

    public static AudioListener Listener => s_listener;

    public static AudioEngine Engine => _engine;

    public static void Initialize()
    {
        try
        {
            _engine = new OpenALEngine();
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to initialize OpenAL audio engine: " + e.Message);
            _engine = new NullAudioEngine();
        }

        for (uint i = 0; i < InitialFreeSources; i++)
            _pool.Add(CreateSource());
    }

    private static ActiveAudio GetOrCreateSource()
    {
        if (_pool.Count != 0)
        {
            var source = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            return source;
        }
        return CreateSource();
    }

    private static ActiveAudio CreateSource()
    {
        ActiveAudio source = _engine.CreateAudioSource();
        source.Position = new Vector3();
        source.PositionKind = AudioPositionKind.ListenerRelative;
        return source;
    }

    public static void UpdatePool()
    {
        for (int i = 0; i < _active.Count; i++)
        {
            ActiveAudio source = _active[i];
            if (!source.IsPlaying || source.PlaybackPosition >= 1f)
            {
                _active.Remove(source);
                _pool.Add(source);
                i--;
            }
        }
    }

    public static void RegisterListener(AudioListener audioListener)
    {
        if (s_listener != null)
        {
            Debug.LogWarning("Audio listener already registered, only the first in the scene will work as intended! Please destroy that one first before instantiating a new Listener.");
            return;
        }
        s_listener = audioListener;
    }

    public static void UnregisterListener(AudioListener audioListener)
    {
        s_listener = null;
    }

    public static AudioBuffer GetAudioBuffer(AudioClip clip)
    {
        AudioBuffer buffer;
        if (!_buffers.TryGetValue(clip, out buffer))
        {
            buffer = _engine.CreateAudioBuffer();
            buffer.BufferData(clip.Data, clip.Format, clip.SampleRate);
            _buffers.Add(clip, buffer);
        }

        return buffer;
    }

    public static void ListenerTransformChanged(Transform t, Vector3 lastPost)
    {
        _engine.SetListenerPosition(t.position);
        _engine.SetListenerVelocity(t.position - lastPost);
        _engine.SetListenerOrientation(t.forward, t.up);
    }

    public static ActiveAudio PlaySound(AudioClip clip)
    {
        return PlaySound(clip, 1.0f, 1.0f);
    }

    public static ActiveAudio PlaySound(AudioBuffer buffer)
    {
        return PlaySound(buffer, 1.0f, 1.0f, Vector3.zero, AudioPositionKind.ListenerRelative, 32f);
    }

    public static ActiveAudio PlaySound(AudioClip clip, float volume)
    {
        return PlaySound(clip, volume, 1f);
    }

    public static ActiveAudio PlaySound(AudioClip clip, float volume, float pitch)
    {
        AudioBuffer buffer = GetAudioBuffer(clip);
        return PlaySound(buffer, volume, pitch, Vector3.zero, AudioPositionKind.ListenerRelative, 32f);
    }

    public static ActiveAudio PlaySound(AudioClip clip, float volume, float pitch, Vector3 position, AudioPositionKind positionKind)
    {
        AudioBuffer buffer = GetAudioBuffer(clip);
        return PlaySound(buffer, volume, pitch, position, positionKind, 32f);
    }

    public static ActiveAudio PlaySound(AudioBuffer buffer, float volume, float pitch, Vector3 position, AudioPositionKind positionKind, float maxDistance)
    {
        ActiveAudio source = GetOrCreateSource();
        source.Gain = volume;
        source.Pitch = pitch;
        source.MaxDistance = maxDistance;
        source.Play(buffer);
        source.Position = position;
        source.PositionKind = positionKind;
        _active.Add(source);
        return source;
    }

    public static void Dispose()
    {
        foreach (var source in _active)
            source.Dispose();

        foreach (var source in _pool)
            source.Dispose();

        foreach (var kvp in _buffers)
            kvp.Value.Dispose();

        if (_engine is IDisposable disposable)
            disposable.Dispose();
    }
}
