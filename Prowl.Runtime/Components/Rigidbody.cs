using BepuPhysics;
using BepuPhysics.Collidables;
using Prowl.Icons;
using System.Linq;

namespace Prowl.Runtime.Components
{
    // Some TODO's for physics
    // Layer Mask/Support
    // Monobehaviour OnColliderEnter/Exit
    // Make Collision shapes support changes to scale/position/rotation
    // Mesh Collider
    // Ensure Compound Collider work well
    // Ensure child colliders work well
    // Capsule Collider Gizmos
    // Cylinder Collider Gizmos
    // Support transform manipulations in Update() (Auto-Sync transform with Rigidbody)
    // Physics.Raycast and other queries

    // Weld Joint
    // Hinge Joint
    // BallSocket Joint
    // DistanceLimit Joint

    // look into joints
    // Slider, PointOnLine, PointOnPlane, AngularServo, LinearServo, SwivelHinge, SwivelHinge2, TwistServo, UniversalJoint, UniversalJoint2D


    /// <summary> A GameObject Component that describes a Dynamic or Static Physical Rigidbody </summary>
    [RequireComponent(typeof(Transform))]
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Cubes}  Rigidbody")]
    public class Rigidbody : MonoBehaviour
    {
        CompoundBuilder builder;
        BodyHandle? bodyHandle;
        StaticHandle? staticHandle;
        TypedIndex? shapeIndex;

        [SerializeField]
        private bool isKinematic = false;

        /// <summary> Get or Set if this Rigidbody is Kinematic</summary>
        public bool IsKinematic {
            get => isKinematic;
            set {
                if (isKinematic == value) return;
                isKinematic = value;
                UpdateBepuBody();
            }
        }


        public override void OnValidate()
        {
            if (!(isKinematic ? staticHandle.HasValue : bodyHandle.HasValue))
                UpdateBepuBody();
        }

        public void UpdateBepuBody()
        {
            // Initialize Builder
            if (builder.Pool == null) builder = new CompoundBuilder(Physics.BufferPool, Physics.Simulation.Shapes, 4);
            else builder.Reset();

            // Get all Colliders
            var colliders = GetComponentsInChildren<Collider>();
             
            // If no colliders found, warn and return
            if (colliders.Count() == 0) {
                Debug.LogWarning("No Colliders found on Rigidbody, Rigidbody will not work!");
                return;
            }

            // Add all Colliders
            foreach (var collider in colliders)
                collider.AddToBuilder(ref builder, collider.GameObject.InstanceID != GameObject.InstanceID);

            // Calculate Compound Data
            builder.BuildDynamicCompound(out var compoundChildren, out var compoundInertia);

            // Track old shape index and add new shape
            var oldShape = shapeIndex;
            shapeIndex = Physics.Simulation.Shapes.Add(new Compound(compoundChildren));

            // If Kinematic has changed (kinematic true but body exists or vice verse)
            if (isKinematic && bodyHandle.HasValue) {
                Physics.Simulation.Bodies.Remove(bodyHandle.Value);
                bodyHandle = null;
            } else if (!isKinematic && staticHandle.HasValue) {
                Physics.Simulation.Statics.Remove(staticHandle.Value);
                staticHandle = null;
            }

            // If has body
            if (!(isKinematic ? staticHandle.HasValue : bodyHandle.HasValue)) {
                // Create a new Body
                if (isKinematic)
                    staticHandle = Physics.Simulation.Statics.Add(new StaticDescription(GetRigidPose(), shapeIndex.Value));
                else
                    bodyHandle = Physics.Simulation.Bodies.Add(BodyDescription.CreateDynamic(GetRigidPose(), compoundInertia, shapeIndex.Value, 0.01f));
            } else {
                // Body already exists so just update it
                if (isKinematic)
                    Physics.Simulation.Statics.SetShape(staticHandle.Value, shapeIndex.Value);
                else
                    Physics.Simulation.Bodies.SetShape(bodyHandle.Value, shapeIndex.Value);
            }

            // Remove old shape and dispose it if we have one
            if (oldShape != null)
                Physics.Simulation.Shapes.RecursivelyRemoveAndDispose(oldShape.Value, Physics.BufferPool);
        }

        public void OnEnable()
        {
            UpdateBepuBody();
        }

        public void Update()
        {
            if (!(isKinematic ? staticHandle.HasValue : bodyHandle.HasValue)) return;
            if (isKinematic) {
                var reference = Physics.Simulation.Statics.GetStaticReference(staticHandle.Value);
                GameObject.Transform!.GlobalPosition = reference.Pose.Position;
                GameObject.Transform!.GlobalOrientation = new(reference.Pose.Orientation.X, reference.Pose.Orientation.Y, reference.Pose.Orientation.Z, reference.Pose.Orientation.W);
            } else {
                var reference = Physics.Simulation.Bodies.GetBodyReference(bodyHandle.Value);
                GameObject.Transform!.GlobalPosition = reference.Pose.Position;
                GameObject.Transform!.GlobalOrientation = new(reference.Pose.Orientation.X, reference.Pose.Orientation.Y, reference.Pose.Orientation.Z, reference.Pose.Orientation.W);
            }

        }

        private RigidPose GetRigidPose() => new() {
            Position = GameObject.Transform!.GlobalPosition,
            Orientation = GameObject.Transform!.GlobalOrientation.ToFloat()
        };

    }

    public abstract class Collider : MonoBehaviour
    {
        public Vector3 offset = Vector3.Zero;
        public float weight = 1f;

        public void AddToBuilder(ref CompoundBuilder builder, bool isChild)
        {
            AddShape(ref builder, GetRigidPose(isChild));
        }

        public void OnEnable()
        {
            GetComponentInParent<Rigidbody>()?.UpdateBepuBody();
        }

        public void OnDisable()
        {
            GetComponentInParent<Rigidbody>()?.UpdateBepuBody();
        }

        private RigidPose GetRigidPose(bool isChild) => new() {
            Position = isChild ? GameObject.Transform!.Position + offset : offset,
            Orientation = (isChild && GameObject.Transform != null) ? GameObject.Transform!.Orientation.ToFloat() : System.Numerics.Quaternion.Identity
        };

        public abstract void AddShape(ref CompoundBuilder builder, RigidPose pose);
    }

    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Box}  Box Collider")]
    public class BoxCollider : Collider
    {
        public Vector3 size = Vector3.One;
        public override void AddShape(ref CompoundBuilder builder, RigidPose pose) => 
            builder.Add(new Box((float)(size.X * GameObject.Transform!.Scale.X), (float)(size.Y * GameObject.Transform!.Scale.Y), (float)(size.Z * GameObject.Transform!.Scale.Z)), pose, weight);

        public void DrawGizmosSelected()
        {
            Gizmos.Matrix = Matrix4x4.CreateScale(size * 1.0025f) * GameObject.Transform!.GlobalCamRelative;
            Gizmos.Cube(Color.yellow);
        }
    }

    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Circle}  Sphere Collider")]
    public class SphereCollider : Collider
    {
        public float radius = 1f;
        public override void AddShape(ref CompoundBuilder builder, RigidPose pose) =>
        builder.Add(new Sphere(radius * (float)GameObject.Transform!.Scale.X), pose, weight);

        public void DrawGizmosSelected()
        {
            var mat = Matrix4x4.Identity;
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateScale((radius * (float)GameObject.Transform!.Scale.X) * 1.0025f));
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateTranslation(GameObject.Transform!.GlobalPosition - (Camera.Current.GameObject.Transform?.GlobalPosition ?? Vector3.Zero)));
            Gizmos.Matrix = mat;
            Gizmos.Sphere(Color.yellow);
        }
    }

    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Capsule Collider")]
    public class CapsuleCollider : Collider
    {
        public float radius = 1f;
        public float height = 1f;
        public override void AddShape(ref CompoundBuilder builder, RigidPose pose) =>
        builder.Add(new Capsule(radius * (float)GameObject.Transform!.Scale.X, height * (float)GameObject.Transform!.Scale.X), pose, weight);
    }

    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Cylinder Collider")]
    public class CylinderCollider : Collider
    {
        public float radius = 1f;
        public float height = 1f;
        public override void AddShape(ref CompoundBuilder builder, RigidPose pose) =>
        builder.Add(new Cylinder(radius * (float)GameObject.Transform!.Scale.X, height * (float)GameObject.Transform!.Scale.X), pose, weight);
    }
}
