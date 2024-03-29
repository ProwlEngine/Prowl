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
            lastPos = this.GameObject.transform.position;
            AudioSystem.RegisterListener(this);
        }
        public override void OnDisable() => AudioSystem.UnregisterListener(this);
        public override void Update()
        {
            if (_lastVersion != this.GameObject.transform.version)
            {
                AudioSystem.ListenerTransformChanged(this.GameObject.transform, lastPos);
                lastPos = this.GameObject.transform.position;
                _lastVersion = this.GameObject.transform.version;
            }
        }
    }
}