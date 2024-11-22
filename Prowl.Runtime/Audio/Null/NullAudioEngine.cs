// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Audio.Null;

public class NullAudioEngine : AudioEngine
{
    public override void SetListenerOrientation(Vector3 forward, Vector3 up)
    {
    }

    public override void SetListenerVelocity(Vector3 velocity)
    {
    }

    public override void SetListenerPosition(Vector3 position)
    {
    }

    private readonly AudioBuffer _nullAudioBuffer = new NullAudioBuffer();
    private readonly ActiveAudio _nullAudioSource = new NullAudioSource();

    public override AudioBuffer CreateAudioBuffer() => _nullAudioBuffer;

    public override ActiveAudio CreateAudioSource() => _nullAudioSource;
}
