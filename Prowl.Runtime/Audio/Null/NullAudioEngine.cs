using System.Numerics;

namespace Prowl.Runtime.Audio.Null
{
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
}
