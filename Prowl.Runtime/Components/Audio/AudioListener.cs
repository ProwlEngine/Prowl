// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Audio;
using Prowl.Runtime.Audio.Native;
using Prowl.Runtime.Resources;
using Prowl.Vector;


// TODO: Camera-Relative Audio Listener

namespace Prowl.Runtime;

/// <summary>
/// A point in 3D space where audio is heard. Only listeners in <see cref="SceneInstance.Main"/> are heard, so spatial
/// sources in other worlds play relative to Main's listener.
/// </summary>
[AddComponentMenu("Audio/Audio Listener")]
[ComponentIcon("\uf025")] // Headphones
public sealed class AudioListener : Component
{
    private IntPtr handle;
    private Float3 previousPosition;
    private int _deviceGeneration = -1;

    /// <summary> A handle to the native ma_audio_listener instance. </summary>
    public IntPtr Handle => handle;

    // Spatial audio is only defined for one listener, so a second enabled in the same world is an authoring mistake.
    internal override void JoinWorld(SceneInstance world)
    {
        if (!world.AudioListeners.Contains(this)) world.AudioListeners.Add(this);
        if (world.AudioListeners.Count > 1)
            Debug.LogWarning($"[{GameObject.Name}] There are now {world.AudioListeners.Count} enabled AudioListeners in '{world.Name}'. " +
                             "Spatial audio is only defined for one, so which of them sources are heard from is arbitrary.");
    }

    internal override void LeaveWorld(SceneInstance world) => world.AudioListeners.Remove(this);

    public override void OnEnable()
    {
        AudioContext.DeviceClosing += OnDeviceClosing;
        SceneInstance.MainChanged += OnMainChanged;
        Refresh();
    }

    private bool IsHeard => SceneInstance.HasMain && ReferenceEquals(GameObject.Scene, SceneInstance.Main);

    private void OnMainChanged(SceneInstance? previous, SceneInstance? next) => Refresh();

    // Makes the native listener while this is in Main, and lets it go otherwise.
    private void Refresh()
    {
        if (!IsHeard) DestroyNative();
        else if (handle == IntPtr.Zero) CreateNative();
    }

    /// <summary>
    /// True while the listener this holds belongs to the device that is open now. One from an older
    /// device died with it, and uninitializing it would be a use after free.
    /// </summary>
    private bool NativeIsLive => AudioContext.IsInitialized && _deviceGeneration == AudioContext.DeviceGeneration;

    /// <summary>Let go while the calls still mean something, rather than after the context has gone.</summary>
    private void OnDeviceClosing() => DestroyNative();

    private void CreateNative()
    {
        if (!AudioContext.IsInitialized)
        {
            Debug.LogWarningOnce("Audio.NoContext", "No audio device is initialized, audio components stay inactive.");
            return;
        }

        _deviceGeneration = AudioContext.DeviceGeneration;
        handle = MiniAudioExNative.ma_ex_audio_listener_init(AudioContext.NativeContext);

        if (handle != IntPtr.Zero)
        {
            previousPosition = Transform.Position;

            // Set Initial Values
            MiniAudioExNative.ma_ex_audio_listener_set_spatialization(handle, 1);

            Apply(Transform.Up, Transform.Forward, previousPosition);
            MiniAudioExNative.ma_ex_audio_listener_set_velocity(handle, 0f, 0f, 0f);
        }
    }

    public override void Update()
    {
        // The device was reopened, so the old listener is gone with it. Rebuilt directly rather than by
        // calling the lifecycle callbacks by hand, which dragged the subscription along for the ride.
        if (_deviceGeneration != AudioContext.DeviceGeneration)
        {
            DestroyNative();
            if (IsHeard) CreateNative();
            _deviceGeneration = AudioContext.DeviceGeneration;
        }

        if (handle == IntPtr.Zero) return;

        Float3 position = Transform.Position;

        Apply(Transform.Up, Transform.Forward, position);

        // Only compute velocity with a positive delta - a zero delta would produce Inf/NaN velocity
        // and feed NaN into the Doppler calculation (AudioSource.Update guards the same way).
        float deltaTime = AudioContext.DeltaTime;
        if (deltaTime > 0f)
        {
            Float3 velocity = AudioContext.ToAudioSpace((position - previousPosition) / deltaTime);
            MiniAudioExNative.ma_ex_audio_listener_set_velocity(handle, velocity.X, velocity.Y, velocity.Z);
        }

        previousPosition = position;
    }

    /// <summary>Pushes the listener's orientation and position across, converted to audio space.</summary>
    private void Apply(Float3 up, Float3 forward, Float3 position)
    {
        up = AudioContext.ToAudioSpace(up);
        forward = AudioContext.ToAudioSpace(forward);
        position = AudioContext.ToAudioSpace(position);

        MiniAudioExNative.ma_ex_audio_listener_set_world_up(handle, up.X, up.Y, up.Z);
        MiniAudioExNative.ma_ex_audio_listener_set_direction(handle, forward.X, forward.Y, forward.Z);
        MiniAudioExNative.ma_ex_audio_listener_set_position(handle, position.X, position.Y, position.Z);
    }

    public override void OnDisable()
    {
        AudioContext.DeviceClosing -= OnDeviceClosing;
        SceneInstance.MainChanged -= OnMainChanged;
        DestroyNative();
    }

    private void DestroyNative()
    {
        if (handle == IntPtr.Zero)
            return;

        // A listener from a device that has already closed went with it, so this only releases one
        // the current device still owns.
        if (NativeIsLive)
            MiniAudioExNative.ma_ex_audio_listener_uninit(handle);

        handle = IntPtr.Zero;
    }
}
