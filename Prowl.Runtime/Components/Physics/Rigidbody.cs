using BepuPhysics;
using BepuPhysics.Collidables;
using Prowl.Icons;
using System.Collections.Generic;


namespace Prowl.Runtime
{
    /// <summary> A GameObject Component that describes a Dynamic or Static Physical Rigidbody </summary>
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Cubes}  Rigidbody")]
    public class Rigidbody : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        private bool _kinematic = false;

        //public enum UpdateMode
        //{
        //    None,
        //    Interpolation,
        //    Extrapolation
        //}
        //public UpdateMode updateMode;

        private BodyHandle? bodyHandle;
        private BodyReference body;
        private TypedIndex? compoundShape;

        private BodyInertia _nativeIntertia;

        [ShowInInspector]
        public bool Kinematic {
            get => _kinematic;
            set {
                if (_kinematic == value)
                    return;

                _kinematic = value;
                if (body.Exists)
                {
                    body.GetDescription(out var description);
                    description.LocalInertia = Kinematic ? new BodyInertia() : _nativeIntertia;
                    body.ApplyDescription(description);
                    body.Awake = true;
                }
            }
        }

        public BodyHandle? BodyHandle {
            get => bodyHandle;
        }

        public Vector3 Position {
            get => body.Pose.Position;
            set => body.Pose.Position = value;
        }

        public Quaternion Rotation {
            get => body.Pose.Orientation;
            set => body.Pose.Orientation = value;
        }

        public bool IsKinematic {
            get => body.Kinematic;
        }

        public Vector3 Velocity {
            get => body.Velocity.Linear;
            set => body.Velocity.Linear = value;
        }

        public Vector3 AngularVelocity {
            get => body.Velocity.Angular;
            set => body.Velocity.Angular = value;
        }

        public void AddForce(Vector3 force) => body.ApplyLinearImpulse(force);

        public void AddTorque(Vector3 torque) => body.ApplyAngularImpulse(torque);

        public void AddForceAtPosition(Vector3 force, Vector3 worldPosition) => body.ApplyImpulse(force, worldPosition - Position);

        public Vector3 GetPointVelocity(Vector3 point) {
            body.GetVelocityForOffset(point - Position, out var velocity);
            return velocity;
        }

        private List<Collider> colliders = [];
        public void StartBuild() => colliders.Clear();
        public void AddCollider(Collider collider) => colliders.Add(collider);

        public void Build()
        {
            Detach();

            if (colliders.Count == 0)
            {
                Debug.LogWarning("Rigidbody has no colliders attached");
                return;
            }

            var shape = ComputeShape(out _nativeIntertia, out System.Numerics.Vector3 compoundCenter);
            var collidableDescription = new CollidableDescription(shape, 0.1f);
            bodyHandle = Physics.Sim.Bodies.Add(BodyDescription.CreateDynamic(
                new RigidPose(this.Transform.position, this.Transform.rotation), _kinematic ? new BodyInertia() : _nativeIntertia, collidableDescription,
                0.01f));
            body = Physics.Sim.Bodies[bodyHandle.Value];
        }

        internal TypedIndex ComputeShape(out BodyInertia compoundInertia, out System.Numerics.Vector3 compoundCenter)
        {
            // Get all colliders
            using (var compoundBuilder = new CompoundBuilder(Physics.Pool, Physics.Sim.Shapes, colliders.Count))
            {
                foreach (var collider in colliders)
                {
                    var target = this.GameObject.Transform;
                    var localPos = target.InverseTransformPoint(collider.Transform.position) + collider.offset;
                    var localRot = target.InverseTransformRotation(collider.Transform.rotation);
                    compoundBuilder.Add(collider.shapeIndex.Value, new RigidPose(localPos, localRot), collider.bodyInertia.Value);
                }
                compoundBuilder.BuildDynamicCompound(out var compoundChildren, out compoundInertia, out compoundCenter);
                compoundBuilder.Reset();
                compoundShape = Physics.Sim.Shapes.Add(new Compound(compoundChildren));
                lastVersion = this.GameObject.Transform.version;
                return compoundShape.Value;
            }
        }

        #region Prowl Methods

        public override void OnEnable()
        {
            Physics.UpdateHierarchy(this.GameObject.Transform.root);
        }

        public override void OnDisable()
        {
            Detach();

            Physics.UpdateHierarchy(this.GameObject.Transform.root);
        }

        private void Detach()
        {
            if (bodyHandle != null)
                Physics.Sim.Bodies.Remove(bodyHandle.Value);
            bodyHandle = null;
        }

        private uint lastVersion = 0;
        public override void Update()
        {
            if (bodyHandle == null) return;

            if (lastVersion != this.GameObject.Transform.version)
            {
                body.Pose.Position = this.GameObject.Transform.position;
                body.Pose.Orientation = this.GameObject.Transform.rotation;
                body.Velocity.Linear = Vector3.zero;
                body.Velocity.Angular = Vector3.zero;
                body.Awake = true;
                lastVersion = this.GameObject.Transform.version;
            }

            //if (updateMode == UpdateMode.Interpolation)
            //{
            //    this.GameObject.Transform.position = body.Pose.Position;
            //    this.GameObject.Transform.rotation = body.Pose.Orientation;
            //}
            //else if (updateMode == UpdateMode.Extrapolation)
            //{
            //    this.GameObject.Transform.position = body.Pose.Position + body.Velocity.Linear * Time.deltaTimeF;
            //    this.GameObject.Transform.rotation = body.Pose.Orientation.ToDouble() + Quaternion.Euler(body.Velocity.Angular * Time.deltaTimeF);
            //}
            //else
            {
                this.GameObject.Transform.position = body.Pose.Position;
                this.GameObject.Transform.rotation = body.Pose.Orientation;
                lastVersion = this.GameObject.Transform.version;
            }
        }

    }

    #endregion
}