// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Audio;

public abstract class AudioEngine
{
    public abstract void SetListenerPosition(Double3 position);
    public abstract void SetListenerVelocity(Double3 velocity);
    public abstract void SetListenerOrientation(Double3 forward, Double3 up);
    public abstract ActiveAudio CreateAudioSource();
    public abstract AudioBuffer CreateAudioBuffer();
}
