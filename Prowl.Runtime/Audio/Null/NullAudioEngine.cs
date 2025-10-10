// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Audio.Null;

public class NullAudioEngine : AudioEngine
{
    public override void SetListenerOrientation(Double3 forward, Double3 up)
    {
    }

    public override void SetListenerVelocity(Double3 velocity)
    {
    }

    public override void SetListenerPosition(Double3 position)
    {
    }

    private readonly AudioBuffer _nullAudioBuffer = new NullAudioBuffer();
    private readonly ActiveAudio _nullAudioSource = new NullAudioSource();

    public override AudioBuffer CreateAudioBuffer() => _nullAudioBuffer;

    public override ActiveAudio CreateAudioSource() => _nullAudioSource;
}
