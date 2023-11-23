namespace Prowl.Runtime.Resources
{
    public class ScriptableObject : EngineObject
    {
        public ScriptableObject() : base() { }
        public ScriptableObject(string name) : base(name) { }

        public override void CreatedInstance()
        {
            base.CreatedInstance();
            Awake();
        }

        public virtual void Awake() { }

    }
}
