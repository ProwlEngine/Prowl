using BepuPhysics;
using BepuPhysics.Collidables;

namespace Prowl.Runtime
{
    public abstract class Collider : MonoBehaviour
    {
        public float mass = 1f;
        public Vector3 offset = Vector3.zero;

        public IShape? shape { get; protected set; }
        public TypedIndex? shapeIndex { get; protected set; }
        public BodyInertia? bodyInertia { get; protected set; }
        public StaticHandle? staticHandle { get; private set; }
        private uint staticLastVersion = 0;

        public override void OnEnable()
        {
            CreateShape();
            Physics.UpdateHierarchy(this.GameObject.Transform.root);
        }

        public override void OnDisable()
        {
            if (shapeIndex != null)
            {
                Physics.Sim.Shapes.RemoveAndDispose(shapeIndex.Value, Physics.Pool);
                shapeIndex = null;
                bodyInertia = null;
            }
            DestroyStatic();
            Physics.UpdateHierarchy(this.GameObject.Transform.root);
        }

        internal void DestroyStatic()
        {
            if (staticHandle == null) return;
            Physics.Sim.Statics.Remove(staticHandle.Value);
            staticHandle = null;
        }

        internal void BuildStatic()
        {
            if (staticHandle != null) return;

            // Make sure Shape Exists

            // Create Static
            var pose = new RigidPose(this.GameObject.Transform.position, this.GameObject.Transform.rotation);
            staticHandle = Physics.Sim.Statics.Add(new StaticDescription(pose, shapeIndex.Value));
            staticLastVersion = this.GameObject.Transform.version;
        }

        public override void FixedUpdate()
        {
            if (staticHandle == null) return;

            if (staticLastVersion != this.GameObject.Transform.version)
            {
                staticLastVersion = this.GameObject.Transform.version;
                var refStatic = Physics.Sim.Statics.GetStaticReference(staticHandle.Value);
                refStatic.Pose.Position = this.GameObject.Transform.position;
                refStatic.Pose.Orientation = this.GameObject.Transform.rotation;
            }
        }

        public abstract void CreateShape();
    }

}