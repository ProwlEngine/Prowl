// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

public abstract class ScriptableObject : EngineObject, ISerializationCallbackReceiver
{
    public ScriptableObject() : base() { }
    private ScriptableObject(string name) : base(name) { }

    // ScriptableObjects can only be created via the AssetDatabase loading them, so their guranteed to always Deserialize
    public virtual void OnAfterDeserialize() => OnEnable();
    public virtual void OnBeforeSerialize() { }

    public virtual void OnEnable() { }

}
