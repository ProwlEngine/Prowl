// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.Audio;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Music}  Audio/{FontAwesome6.Microphone}  Audio Listener")]
public class AudioListener : MonoBehaviour
{
    private uint _lastVersion;
    private Vector3 lastPos;

    public override void OnEnable()
    {
        lastPos = GameObject.Transform.position;
        AudioSystem.RegisterListener(this);
    }
    public override void OnDisable() => AudioSystem.UnregisterListener(this);
    public override void Update()
    {
        if (_lastVersion != GameObject.Transform.version)
        {
            AudioSystem.ListenerTransformChanged(GameObject.Transform, lastPos);
            lastPos = GameObject.Transform.position;
            _lastVersion = GameObject.Transform.version;
        }
    }
}
