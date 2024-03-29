using Prowl.Runtime.Audio;
using System;

namespace Prowl.Runtime
{
    public sealed class AudioSource : MonoBehaviour
    {
        public AssetRef<AudioClip> Clip;
        public bool Looping = false;
        public float Volume = 1f;

        private ActiveAudio _source;
        private AudioBuffer _buffer;
        private uint _lastVersion;
        private bool _looping = false;
        private float _gain = 1f;

        public void Play()
        {
            if (Clip.IsAvailable)
                _source.Play(_buffer);
        }

        public void Stop()
        {
            if (Clip.IsAvailable)
                _source.Stop();
        }

        public override void Awake()
        {
            _source = AudioSystem.Engine.CreateAudioSource();
            _source.Position = this.GameObject.transform.position;
            _source.Direction = this.GameObject.transform.forward;
            _source.Gain = Volume;
            _source.Looping = Looping;
            if (Clip.IsAvailable)
                _buffer = AudioSystem.GetAudioBuffer(Clip.Res!);
        }

        public override void Update()
        {
            if(_lastVersion != this.GameObject.transform.version)
            {
                _source.Position = this.GameObject.transform.position;
                _source.Direction = this.GameObject.transform.forward;
            }

            if (Clip.IsAvailable)
                _buffer = AudioSystem.GetAudioBuffer(Clip.Res!);

            if (_looping != Looping)
            {
                _source.Looping = Looping;
                _looping = Looping;
            }

            if (_gain != Volume)
            {
                _source.Gain = Volume;
                _gain = Volume;
            }
        }

        public override void OnDisable() => _source.Stop();

        public override void OnDestroy()
        {
            _source.Dispose();
        }
    }
}
