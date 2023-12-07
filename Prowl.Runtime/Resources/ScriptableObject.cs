namespace Prowl.Runtime
{
    public class ScriptableObject : EngineObject
    {
        public ScriptableObject() : base() { }
        public ScriptableObject(string name) : base(name) { }

        public virtual void OnValidate() { }

    }
}
