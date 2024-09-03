using Prowl.Icons;
using Prowl.Runtime.Audio;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.Music}  Audio/{FontAwesome6.Microphone}  Audio Listener")]
    public class AudioListener : MonoBehaviour
    {
        private uint _lastVersion;
        private Vector3 lastPos;

        public override void OnEnable()
        {
            lastPos = this.GameObject.Transform.position;
            AudioSystem.RegisterListener(this);
        }
        public override void OnDisable() => AudioSystem.UnregisterListener(this);
        public override void Update()
        {
            if (_lastVersion != this.GameObject.Transform.version)
            {
                AudioSystem.ListenerTransformChanged(this.GameObject.Transform, lastPos);
                lastPos = this.GameObject.Transform.position;
                _lastVersion = this.GameObject.Transform.version;
            }
        }
    }
}
