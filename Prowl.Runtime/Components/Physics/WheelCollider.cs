using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using System;

namespace Prowl.Runtime
{
    /// <summary>
    /// A Raycast based Wheel "Collider"
    /// </summary>
    /// Taken from on jitter physics 2 Demos
    public class WheelCollider : MonoBehaviour
    {
        private Rigidbody car;

        private float displacement, lastDisplacement;
        private bool onFloor;
        private float driveTorque;
        private float heldTorque;
        private float steerAngle;
        private float heldSteerAngle;

        private float angVel;

        /// used to estimate the friction
        private float angVelForGrip;

        private float torque;

        private World.RayCastFilterPre rayCast;

        /// <summary> The damping factor of the suspension spring. </summary>
        public float Damping = 4000f;

        /// <summary> The suspension spring. </summary>
        public float Spring = 30000f;

        /// <summary> Inertia of the wheel. </summary>
        public float Inertia = 1.0f;

        /// <summary> The wheel radius. </summary>
        public float Radius = 0.5f;

        /// <summary> The friction of the car in the side direction. </summary>
        public float SideFriction = 3.2f;

        /// <summary> Friction of the car in forward direction. </summary>
        public float ForwardFriction = 5f;

        /// <summary> The length of the suspension spring. </summary>
        public float WheelTravel = 0.2f;

        /// <summary> If set to true the wheel blocks. </summary>
        public bool Locked = false;

        /// <summary> The highest possible velocity of the wheel. </summary>
        public float MaximumAngularVelocity = 200;

        /// <summary> The position of the wheel in body space. </summary>
        public Vector3 Position = Vector3.zero;

        public float AngularVelocity => angVel;

        /// <summary> Sets or gets the current steering angle of the wheel in degrees. </summary>
        public float SteerAngle { get => steerAngle; set { heldSteerAngle = value; } }

        /// <summary> Gets the current rotation of the wheel in degrees. </summary>
        public float WheelRotation { get; private set; }

        [Space()]
        [Separator()]
        [Space()]
        [Header("Calculate default forces based on Mass, Radius and Wheel Count.")]
        [NonSerialized] public int WheelCount = 4;

        private const float dampingFrac = 0.8f;
        private const float springFrac = 0.45f;

        public override void OnEnable()
        {
            car = GetComponentInParent<Rigidbody>();
            if(car == null) Debug.LogWarning("WheelCollider: No RigidBody found in parent.");
            rayCast = shape => shape.RigidBody.Tag != car;
        }

        [ImGUIButton("Calculate Forces")]
        public void AutoCalculate()
        {
            car ??= GetComponentInParent<Rigidbody>();
            if (car == null) Debug.LogWarning("WheelCollider: No RigidBody found in parent.");
            float mass = car.Mass / 4.0f;
            //float wheelMass = car.Mass * 0.5f;

            Inertia = 0.5f * (Radius * Radius) * mass;
            Spring = mass * car.Space.world.Gravity.Length() / (WheelTravel * springFrac) * 0.5f;
            Damping = 2.0f * (float)Math.Sqrt(Spring * car.Mass) * 0.25f * dampingFrac;
        }

        /// <summary>
        /// Adds drivetorque.
        /// </summary>
        /// <param name="torque">The amount of torque applied to this wheel.</param>
        public void AddTorque(float torque) => heldTorque += torque;

        public override void FixedUpdate()
        {
            car.Body.DeactivationTime = TimeSpan.MaxValue;

            PreStep();
            driveTorque = heldTorque;
            steerAngle = heldSteerAngle;
            PostStep();
            heldTorque = 0;
        }

        public override void DrawGizmos()
        {
            car = GetComponentInParent<Rigidbody>();
            if (car == null) return;

            var carRotation = car.GameObject.transform.rotation;
            Vector3 worldPos = car.GameObject.transform.position + Vector3.Transform(Position, carRotation);
            worldPos -= Camera.Current.GameObject.transform.position;
            Vector3 worldAxis = Vector3.Transform(JVector.UnitY, carRotation);

            Matrix4x4 wheelMatrix = Matrix4x4.Identity;
            wheelMatrix = Matrix4x4.Multiply(wheelMatrix, Matrix4x4.CreateScale(Radius * 2f));
            wheelMatrix = Matrix4x4.Multiply(wheelMatrix, Matrix4x4.CreateRotationY((steerAngle + 90) * MathF.PI / 180));
            wheelMatrix = Matrix4x4.Multiply(wheelMatrix, Matrix4x4.CreateFromQuaternion(carRotation));
            wheelMatrix = Matrix4x4.Multiply(wheelMatrix, Matrix4x4.CreateTranslation(worldPos + (worldAxis * displacement)));

            Matrix4x4 centerMatrix = Matrix4x4.Identity;
            centerMatrix = Matrix4x4.Multiply(centerMatrix, Matrix4x4.CreateScale(0.02f));
            centerMatrix = Matrix4x4.Multiply(centerMatrix, Matrix4x4.CreateRotationY((steerAngle + 90) * MathF.PI / 180));
            centerMatrix = Matrix4x4.Multiply(centerMatrix, Matrix4x4.CreateFromQuaternion(carRotation));
            centerMatrix = Matrix4x4.Multiply(centerMatrix, Matrix4x4.CreateTranslation(worldPos));

            Gizmos.Matrix = wheelMatrix;
            Gizmos.Circle(Color.yellow);
            Gizmos.Matrix = centerMatrix;
            Gizmos.Circle(Color.red);
        }

        public void PreStep()
        {
            JVector worldPos = car.Body.Position + JVector.Transform(Position, car.Body.Orientation);
            JVector worldAxis = JVector.Transform(JVector.UnitY, car.Body.Orientation);

            JVector forward = car.Body.Orientation.GetColumn(2);
            JVector wheelFwd = JVector.Transform(forward, JMatrix.CreateRotationMatrix(worldAxis, SteerAngle));

            JVector wheelLeft = JVector.Cross(worldAxis, wheelFwd);
            wheelLeft.Normalize();

            onFloor = car.Space.world.RayCast(worldPos, -worldAxis, rayCast, null, out Shape? shape, out JVector groundNormal, out float dist);
            if (!onFloor || dist > Radius * 2f) return;
            if (groundNormal.LengthSquared() > 0.0f) groundNormal.Normalize();

            // Hooke's Law - Suspension
            lastDisplacement = displacement;
            displacement = MathF.Max(0.0f, Radius - (dist - Radius));
            var springVelocity = (displacement - lastDisplacement) / (float)Time.fixedDeltaTime;
            var springForce = Spring * displacement;
            springForce *= MathF.Max(0.0f, JVector.Dot(groundNormal, worldAxis));
            var damperForce = Damping * springVelocity;

            // Apply force to car body
            JVector force = worldAxis * (springForce + damperForce);

            // side-slip friction and drive force. Work out wheel- and floor-relative coordinate frame
            JVector groundUp = groundNormal;
            JVector groundLeft = JVector.Cross(groundNormal, wheelFwd);
            if (groundLeft.LengthSquared() > 0.0f) groundLeft.Normalize();
            JVector groundFwd = JVector.Cross(groundLeft, groundUp);
            
            JVector wheelPointVel = car.Body.Velocity + JVector.Cross(car.Body.AngularVelocity, JVector.Transform(Position, car.Body.Orientation));

            JVector groundPos = worldPos + (-worldAxis * dist);
            // rimVel=(wxr)*v
            JVector rimVel = angVel * JVector.Cross(wheelLeft, groundPos - worldPos);
            wheelPointVel += rimVel;

            RigidBody worldBody = shape!.RigidBody!;
            JVector worldVel = worldBody.Velocity + JVector.Cross(worldBody.AngularVelocity, groundPos - worldBody.Position);
            wheelPointVel -= worldVel;

            // sideways forces
            float sidewaysFriction = CalculateFriction(SideFriction, groundLeft, wheelPointVel);
            force += ((-sidewaysFriction / (float)Time.fixedDeltaTime) * groundLeft);

            // fwd/back forces
            float forwardFriction = CalculateFriction(ForwardFriction, groundFwd, wheelPointVel);
            float fwdForce = (-forwardFriction / (float)Time.fixedDeltaTime);
            force += (fwdForce * groundFwd);

            // fwd force also spins the wheel
            JVector wheelCentreVel = car.Body.Velocity + JVector.Cross(car.Body.AngularVelocity, JVector.Transform(Position, car.Body.Orientation));

            angVelForGrip = JVector.Dot(wheelCentreVel, groundFwd) / Radius;
            torque += -fwdForce * Radius;

            // add force to car
            car.AddForceAtPosition(force, groundPos);

            // add force to the world
            if (!worldBody.IsStatic) {
                const float maxOtherBodyAcc = 500.0f;
                float maxOtherBodyForce = maxOtherBodyAcc * worldBody.Mass;

                if (force.LengthSquared() > (maxOtherBodyForce * maxOtherBodyForce))
                    force *= maxOtherBodyForce / force.Length();

                worldBody.SetActivationState(true);
                worldBody.AddForce(force * -1, groundPos);
            }
        }

        private static float CalculateFriction(float frictionStrength, JVector directory, JVector wheelVelocity, float noslipVel = 0.2f, float slipVel = 0.4f, float slipFactor = 0.7f, float smallVel = 3.0f)
        {
            float friction = frictionStrength;
            float vel = JVector.Dot(wheelVelocity, directory);
            if (vel > slipVel || vel < -slipVel)
                friction *= slipFactor;
            else if (vel > noslipVel || vel < -noslipVel)
                friction *= 1.0f - (1.0f - slipFactor) * (Math.Abs(vel) - noslipVel) / (slipVel - noslipVel);
            if (vel < 0.0f) friction *= -1.0f;
            if (Math.Abs(vel) < smallVel) friction *= Math.Abs(vel) / smallVel;
            return friction;
        }

        public void PostStep()
        {
            float timeStep = (float)Time.fixedDeltaTime;
            if (timeStep <= 0.0f) return;

            float origAngVel = angVel;

            if (Locked) {
                angVel = 0;
                torque = 0;
            } else {
                angVel += torque * timeStep / Inertia;
                torque = 0;

                if (!onFloor) 
                    driveTorque *= 0.1f;

                // prevent friction from reversing dir - todo do this better
                // by limiting the torque
                if ((origAngVel > angVelForGrip && angVel < angVelForGrip) ||
                    (origAngVel < angVelForGrip && angVel > angVelForGrip))
                    angVel = angVelForGrip;

                angVel += driveTorque * timeStep / Inertia;
                driveTorque = 0;

                float maxAngVel = MaximumAngularVelocity;
                angVel = Math.Clamp(angVel, -maxAngVel, maxAngVel);

                WheelRotation += timeStep * angVel;
            }
        }
    }

}