// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Audio
{
    public abstract class AudioEngine
    {
        public abstract void SetListenerPosition(Vector3 position);
        public abstract void SetListenerVelocity(Vector3 velocity);
        public abstract void SetListenerOrientation(Vector3 forward, Vector3 up);
        public abstract ActiveAudio CreateAudioSource();
        public abstract AudioBuffer CreateAudioBuffer();
    }
}
