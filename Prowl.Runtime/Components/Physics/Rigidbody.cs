using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;
using Prowl.Icons;
using System.Collections.Generic;
using System.Linq;


// Partially based on https://github.com/suzuke/JitterPhysicsForUnity

namespace Prowl.Runtime
{
    /// <summary> A GameObject Component that describes a Dynamic or Static Physical Rigidbody </summary>
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Cubes}  Rigidbody")]
    public class Rigidbody : MonoBehaviour
    {
        private PhysicalSpace space;
        public PhysicalSpace Space {
            get => space ??= Physics.DefaultSpace;
            set {
                space = value;
                if (Body != null) {
                    space.world.Remove(Body);
                    Body = null;
                }
            }
        }


        internal Jitter2.Dynamics.RigidBody Body { get; private set; }

        private List<Shape> shapes;
        private List<Shape> Shapes => shapes ??= CreateShapes();

        [SerializeField] private bool affectedByGravity = true;
        public bool AffectedByGravity {
            get => affectedByGravity;
            set {
                affectedByGravity = value;
                Body.AffectedByGravity = value;
            }
        }

        [SerializeField] private bool isStatic;
        public bool IsStatic {
            get => isStatic;
            set {
                isStatic = value;
                Body.IsStatic = value;
            }
        }

        [SerializeField] private bool isKinematic;
        public bool IsKinematic {
            get => isKinematic;
            set => isKinematic = value;
        }

        public bool IsActive {
            get => Body?.IsActive ?? false;
            set => Body?.SetActivationState(value);
        }

        [SerializeField] private bool speculativeContacts;
        public bool SpeculativeContacts {
            get => speculativeContacts;
            set {
                speculativeContacts = value;
                Body.EnableSpeculativeContacts = value;
            }
        }

        [SerializeField]
        private float mass;
        public float Mass {
            get => mass;
            set {
                mass = value;
                Body.SetMassInertia(mass);
            }
        }

        public Vector3 Torque => new(Body.Torque.X, Body.Torque.Y, Body.Torque.Z);

        public Vector3 Force {
            get => new(Body.Force.X, Body.Force.Y, Body.Force.Z);
            set => Body.Force = value;
        }

        public Vector3 Velocity {
            get => new(Body.Velocity.X, Body.Velocity.Y, Body.Velocity.Z);
            set => Body.Velocity = value;
        }

        public Vector3 AngularVelocity {
            get => new(Body.AngularVelocity.X, Body.AngularVelocity.Y, Body.AngularVelocity.Z);
            set => Body.AngularVelocity = value;
        }

        public List<Collision> Contacts => Body.Contacts.Select(x => new Collision(this, x)).ToList();
        public List<Rigidbody> TouchingBodies => Body.Connections.Select(x => (x.Tag as Rigidbody)!).ToList();


        public void AddForce(Vector3 force) => Body.AddForce(force);

        public void AddForceAtPosition(Vector3 force, Vector3 position) => Body.AddForce(force, position);

        public Vector3 GetPointVelocity(Vector3 point) => Body.Velocity + JVector.Cross(Body.AngularVelocity, point - Body.Position);

        public void SetTransform(Vector3 position, Quaternion rotation)
        {
            SetPosition(position);
            SetRotation(rotation);
        }

        public void SetPosition(Vector3 position) => Body.Position = position;

        public void SetRotation(Quaternion rotation) => Body.Orientation = JMatrix.CreateFromQuaternion(rotation);

        public void Refresh()
        {
            if(isKinematic || mass > 0)
                Mass = isKinematic ? 1e6f : mass;
            IsStatic = isStatic;
            AffectedByGravity = isKinematic ? false : affectedByGravity;
            SpeculativeContacts = speculativeContacts;
            shapes = CreateShapes();

            IsActive = true;

            SetPosition(GameObject.transform.position);
            SetRotation(GameObject.transform.rotation);
        }

        public void RefreshShape()
        {
            // TODO: Causes Crash, Shapes get cached and dont appear to support being registered/attached to a body a second time
            //Body.ClearShapes(false);
            //shapes = CreateShapes();
            //Body.AddShape(Shapes);
        }

        private List<Shape> CreateShapes()
        {
            var colliders = GetComponentsInChildren<Collider>().ToList();

            List<Shape> allShapes = new();
            foreach (var collider in colliders) {
                if (collider.GameObject.InstanceID == GameObject.InstanceID)
                    allShapes.AddRange(collider.Shape);
                else
                    allShapes.AddRange(collider.CreateTransformedShape(this));
            }

            return allShapes;
        }

        #region Prowl Methods
        public override void OnValidate() { if (Application.isPlaying) Refresh(); }

        private void OnEnable()
        {
            Body = Space.world.CreateRigidBody();
            Body.AddShape(Shapes);
            Body.Tag = this;
            Body.AffectedByGravity = AffectedByGravity;
            Body.IsStatic = IsStatic;

            if (isKinematic) {
                Body.AffectedByGravity = false;
                Body.SetMassInertia(1e6f);
            } else if (Mass > 0) Body.SetMassInertia(Mass);
            else Body.SetMassInertia();

            SetPosition(GameObject.transform.position);
            SetRotation(GameObject.transform.rotation);
        }

        private void OnDisable()
        {
            Space.world.Remove(Body);
            Body = null;
        }

        private void LateUpdate()
        {
            GameObject.transform.position = Body.Position;
            GameObject.transform.rotation = JQuaternion.CreateFromMatrix(Body.Orientation);
        }

        #endregion
    }
}