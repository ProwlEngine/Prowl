namespace Prowl.Runtime
{
    public abstract class ScriptableObject : EngineObject, ISerializationCallbackReceiver
    {
        private ScriptableObject() : base() { }
        private ScriptableObject(string name) : base(name) { }

        // ScriptableObjects can only be created via the AssetDatabase loading them, so their guranteed to always Deserialize
        public void OnAfterDeserialize() => OnEnable();
        public void OnBeforeSerialize() { }

        public virtual void OnEnable() { }

    }
}
