// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Audio;
using Prowl.Runtime.Audio.Native;
using Prowl.Vector;


// TODO: Camera-Relative Audio Listener

namespace Prowl.Runtime;

/// <summary> This class represents a point in the 3D space where audio is perceived or heard. </summary>
[AddComponentMenu("Audio/Audio Listener")]
[ComponentIcon("\uf025")] // Headphones
public sealed class AudioListener : MonoBehaviour
{
    private IntPtr handle;
    private Float3 previousPosition;
    private int _deviceGeneration = -1;

    private static int s_activeCount;

    /// <summary> A handle to the native ma_audio_listener instance. </summary>
    public IntPtr Handle => handle;

    /// <summary>
    /// How many listeners are currently enabled. Spatial audio is only well defined with exactly one:
    /// none leaves every source positioned relative to the world origin, and more than one leaves it
    /// undefined which of them sources are heard from.
    /// </summary>
    public static int ActiveCount => s_activeCount;

    public override void OnEnable()
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
            s_activeCount++;

            if (s_activeCount > 1)
            {
                Debug.LogWarning($"[{GameObject.Name}] There are now {s_activeCount} enabled AudioListeners. " +
                                 "Spatial audio is only defined for one, so which of them sources are heard from is arbitrary.");
            }

            previousPosition = Transform.Position;

            // Set Initial Values
            MiniAudioExNative.ma_ex_audio_listener_set_spatialization(handle, 1);

            Apply(Transform.Up, Transform.Forward, previousPosition);
            MiniAudioExNative.ma_ex_audio_listener_set_velocity(handle, 0f, 0f, 0f);
        }
    }

    public override void Update()
    {
        // The device was reopened, so the old listener is gone with it.
        if (_deviceGeneration != AudioContext.DeviceGeneration)
        {
            OnDisable();
            OnEnable();
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
        if (handle != IntPtr.Zero)
        {
            MiniAudioExNative.ma_ex_audio_listener_uninit(handle);
            handle = IntPtr.Zero;
            s_activeCount = Math.Max(0, s_activeCount - 1);
        }
    }
}
