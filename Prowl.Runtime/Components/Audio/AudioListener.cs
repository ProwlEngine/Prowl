using Prowl.Runtime.Audio;

namespace Prowl.Runtime
{
    public class AudioListener : MonoBehaviour
    {
        private uint _lastVersion;

        public override void OnEnable() => AudioSystem.RegisterListener(this);
        public override void OnDisable() => AudioSystem.UnregisterListener(this);
        public override void Update()
        {
            if (_lastVersion != this.GameObject.transform.version)
            {
                AudioSystem.ListenerTransformChanged(this.GameObject.transform);
            }
        }
    }
}