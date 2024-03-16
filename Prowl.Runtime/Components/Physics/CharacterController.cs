using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;
using Jitter2.LinearMath;
using Prowl.Icons;
using Silk.NET.Input;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Gamepad}  Character Controller")]
    public class CharacterController : MonoBehaviour
    {
        private PhysicalSpace space;
        public PhysicalSpace Space {
            get => space ??= Physics.DefaultSpace;
            set {
                space = value;
                if (Body != null)
                {
                    space.world.Remove(Body);
                    Body = null;
                }
            }
        }

        public float height = 2.0f;
        public float radius = 0.5f;

        public bool isGrounded { get; private set; } = false;
        public Shape floor { get; private set; } = null;
        public Vector3 hitPoint { get; private set; } = Vector3.zero;

        public RigidBody Body { get; private set; }

        private float capsuleHalfHeight;
        private Vector3 accumulatedMovement;

        public override void Awake()
        {
            Body = Space.world.CreateRigidBody();
            var cs = new CapsuleShape(radius, height * 0.5f);
            Body.AddShape(cs);
            Body.Position = this.GameObject.transform.position;
            Body.AffectedByGravity = false;

            // disable velocity damping
            Body.Damping = (0, 0);

            this.capsuleHalfHeight = cs.Radius + cs.Length * 0.5f;

            // Disable deactivation
            Body.DeactivationTime = TimeSpan.MaxValue;

            // Make the capsule stand upright, but able to rotate 360 degrees.
            var ur = Space.world.CreateConstraint<HingeAngle>(Body, Space.world.NullBody);
            ur.Initialize(JVector.UnitY, AngularLimit.Full);
        }

        public override void Update()
        {
            this.GameObject.transform.position = Body.Position;
        }

        public override void FixedUpdate()
        {
            if (Body == null) return;
            this.Body.SetActivationState(true);

            isGrounded = IsOnFloor(out var floor, out var hitPoint);
            this.floor = floor;
            this.hitPoint = hitPoint;

            //this.Body.Position += accumulatedMovement;
            this.Body.AddForce(accumulatedMovement * 30);
            accumulatedMovement = Vector3.zero;

            if (isGrounded)
                this.Body.Velocity *= 0.98f;
        }

        public void Move(Vector3 deltaMove) => accumulatedMovement += deltaMove;

        private World.RayCastFilterPre? preFilter = null;
        private bool FilterShape(Shape shape)
        {
            if (shape.RigidBody != null)
            {
                if (shape.RigidBody == Body) return false;
            }
            return true;
        }

        private bool IsOnFloor(out Shape? floor, out JVector hitPoint)
        {
            preFilter ??= FilterShape;

            bool hit = space.world.RayCast(Body.Position, -JVector.UnitY, preFilter, null,
                out floor, out JVector normal, out float fraction);

            float delta = fraction - capsuleHalfHeight;

            hitPoint = Body.Position - JVector.UnitY * fraction;
            return (hit && delta < 0.04f && floor != null);
        }


    }
}
