// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Audio;
using Prowl.Runtime.Audio.Native;
using Prowl.Vector;


// TODO: Camera-Relative Audio Listener

namespace Prowl.Runtime
{
    /// <summary> This class represents a point in the 3D space where audio is perceived or heard. </summary>
    public sealed class AudioListener : MonoBehaviour
    {
        private IntPtr handle;
        private Float3 previousPosition;

        /// <summary> A handle to the native ma_audio_listener instance. </summary>
        public IntPtr Handle => handle;

        public override void OnEnable()
        {
            handle = MiniAudioExNative.ma_ex_audio_listener_init(AudioContext.NativeContext);

            if (handle != IntPtr.Zero)
            {
                previousPosition = this.Transform.Position;

                // Set Initial Values
                MiniAudioExNative.ma_ex_audio_listener_set_spatialization(handle, 1);

                var up = Transform.Up;
                var forward = Transform.Forward;
                var pos = Transform.Position;
                MiniAudioExNative.ma_ex_audio_listener_set_world_up(handle, (float)up.X, (float)up.Y, (float)up.Z);
                MiniAudioExNative.ma_ex_audio_listener_set_direction(handle, (float)forward.X, (float)forward.Y, (float)forward.Z);
                MiniAudioExNative.ma_ex_audio_listener_set_position(handle, (float)pos.X, (float)pos.Y, (float)pos.Z);
                MiniAudioExNative.ma_ex_audio_listener_set_velocity(handle, 0f, 0f, 0f);
            }
        }

        public override void Update()
        {
            var up = -Transform.Up;
            var forward = Transform.Forward;
            var pos = Transform.Position;

            MiniAudioExNative.ma_ex_audio_listener_set_world_up(handle, (float)up.X, (float)up.Y, (float)up.Z);
            MiniAudioExNative.ma_ex_audio_listener_set_direction(handle, (float)forward.X, (float)forward.Y, (float)forward.Z);


            MiniAudioExNative.ma_ex_audio_listener_get_position(handle, out float prevX, out float prevY, out float prevZ);
            previousPosition = new Float3(prevX, prevY, prevZ);
            MiniAudioExNative.ma_ex_audio_listener_set_position(handle, (float)pos.X, (float)pos.Y, (float)pos.Z);


            float deltaTime = AudioContext.DeltaTime;
            Float3 currentPosition = Transform.Position;
            float dx = currentPosition.X - previousPosition.X;
            float dy = currentPosition.Y - previousPosition.Y;
            float dz = currentPosition.Z - previousPosition.Z;
            var vel = new Float3(dx / deltaTime, dy / deltaTime, dz / deltaTime);

            MiniAudioExNative.ma_ex_audio_listener_set_velocity(handle, (float)vel.X, (float)vel.Y, (float)vel.Z);
        }

        public override void OnDisable()
        {
            if (handle != IntPtr.Zero)
            {
                MiniAudioExNative.ma_ex_audio_listener_uninit(handle);
                handle = IntPtr.Zero;
            }
        }
    }
}
