using HexaEngine.ImPlotNET;
using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using Jitter2.SoftBodies;
using Microsoft.VisualBasic;
using Prowl.Icons;
using Prowl.Runtime.SceneManagement;
using Silk.NET.Input;
using Silk.NET.SDL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.ConstrainedExecution;


// Partially based on https://github.com/suzuke/JitterPhysicsForUnity

namespace Prowl.Runtime.Components
{
    /// <summary> A GameObject Component that describes a Dynamic or Static Physical Rigidbody </summary>
    [RequireComponent(typeof(Transform))]
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

            SetPosition(GameObject.Transform.GlobalPosition);
            SetRotation(GameObject.Transform.GlobalOrientation);
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
                if (collider.GameObject.Transform!.InstanceID == GameObject.Transform!.InstanceID)
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

            SetPosition(GameObject.Transform.GlobalPosition);
            SetRotation(GameObject.Transform.GlobalOrientation);
        }

        private void OnDisable()
        {
            Space.world.Remove(Body);
            Body = null;
        }

        private void LateUpdate()
        {
            GameObject.Transform.GlobalPosition = Body.Position;
            GameObject.Transform.GlobalOrientation = JQuaternion.CreateFromMatrix(Body.Orientation);
        }

        #endregion
    }

    public abstract class Collider : MonoBehaviour
    {
        private List<Shape> shape;
        public List<Shape> Shape => shape ??= CreateShapes();
        public void OnEnable() => GetComponentInParent<Rigidbody>()?.RefreshShape();
        public void OnDisable() => GetComponentInParent<Rigidbody>()?.RefreshShape();
        public abstract List<Shape> CreateShapes();
        public virtual List<TransformedShape> CreateTransformedShape(Rigidbody body)
        {
            // Transform is guranteed to exist, since a Collider must be On or under a Rigidbody which requires a Transform
            // COllider is on a child without a transform the Parent transform of the Rigidbody is used via Inheritance
            // This is fine since we use Global positions here, since the Body and Collider share a transform their global positions are identical
            // Results in no offsets being applied to the shape
            var position = GameObject.Transform!.GlobalPosition - body.GameObject.Transform!.GlobalPosition;
            var rotation = Quaternion.RotateTowards(body.GameObject.Transform!.GlobalOrientation, GameObject.Transform!.GlobalOrientation, 360);

            var invRotation = Quaternion.Inverse(body.GameObject.Transform!.GlobalOrientation);
            rotation = invRotation * rotation;
            position = Vector3.Transform(position, invRotation);
            var jRot = JMatrix.CreateFromQuaternion(rotation);

            return Shape.Select(x => new TransformedShape(x, position, jRot)).ToList();
        }
    }

    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Box}  Box Collider")]
    public class BoxCollider : Collider
    {
        public Vector3 size = Vector3.One;

        public override List<Shape> CreateShapes() => [ new BoxShape(size * GameObject.Transform!.Scale) ];
        public override void OnValidate()
        {
            (Shape[0] as BoxShape).Size = size * GameObject.Transform!.Scale;
            Shape[0].UpdateShape();
            GetComponentInParent<Rigidbody>().IsActive = true;
        }

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
        public override List<Shape> CreateShapes() => [ new SphereShape(radius * (float)GameObject.Transform!.Scale.x) ];
        public override void OnValidate()
        {
            (Shape[0] as SphereShape).Radius = radius * (float)GameObject.Transform!.Scale.x;
            Shape[0].UpdateShape();
            GetComponentInParent<Rigidbody>().IsActive = true;
        }

        public void DrawGizmosSelected()
        {
            var mat = Matrix4x4.Identity;
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateScale((radius * (float)GameObject.Transform!.Scale.x) * 1.0025f));
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
        public override List<Shape> CreateShapes() => [ new CapsuleShape(radius, height) ];
        public override void OnValidate()
        {
            (Shape[0] as CapsuleShape).Radius = radius;
            (Shape[0] as CapsuleShape).Length = height;
            Shape[0].UpdateShape();
            GetComponentInParent<Rigidbody>().IsActive = true;
        }
    }

    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Cylinder Collider")]
    public class CylinderCollider : Collider
    {
        public float radius = 1f;
        public float height = 1f;
        public override List<Shape> CreateShapes() => [ new CylinderShape(radius, height) ];
        public override void OnValidate()
        {
            (Shape[0] as CapsuleShape).Radius = radius;
            (Shape[0] as CapsuleShape).Length = height;
            Shape[0].UpdateShape();
            GetComponentInParent<Rigidbody>().IsActive = true;
        }
    }

    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Mesh Collider")]
    public class MeshCollider : Collider
    {
        public AssetRef<Mesh> mesh;

        public bool convex = false;

        public enum Approximation
        {
            Level1 = 6,
            Level2 = 7,
            Level3 = 8,
            Level4 = 9,
            Level5 = 10,
            Level6 = 11,
            Level7 = 12,
            Level8 = 13,
            Level9 = 15,
            Level10 = 20,
            Level15 = 25,
            Level20 = 30
        }

        public Approximation convexApprox = Approximation.Level5;

        public override List<Shape> CreateShapes()
        {
            if (mesh.IsAvailable == false) return [new SphereShape(0.001f)]; // Mesh is missing so we create a sphere with a tiny radius to prevent errors

            if (!convex) {
                var indices = mesh.Res.indices;
                var vertices = mesh.Res.vertices;

                List<JTriangle> triangles = new();

                for (int i = 0; i < mesh.Res.triangleCount; i += 3) {
                    JVector v1 = vertices[i + 0].Position.ToDouble();
                    JVector v2 = vertices[i + 1].Position.ToDouble();
                    JVector v3 = vertices[i + 2].Position.ToDouble();
                    triangles.Add(new JTriangle(v1, v2, v3));
                }

                var jtm = new TriangleMesh(triangles);
                List<Shape> shapesToAdd = new();

                for (int i = 0; i < jtm.Indices.Length; i++) {
                    TriangleShape ts = new TriangleShape(jtm, i);
                    shapesToAdd.Add(ts);
                }
                return shapesToAdd;
            } else {
                var points = mesh.Res.vertices.Select(x => (JVector)x.Position.ToDouble());
                return [new PointCloudShape(BuildConvexCloud(points.ToList()))];
            }

        }

        private List<JVector> BuildConvexCloud(List<JVector> pointCloud)
        {
            List<JVector> allIndices = new();

            int steps = (int)convexApprox;

            for (int thetaIndex = 0; thetaIndex < steps; thetaIndex++) {
                // [0,PI]
                float theta = MathF.PI / (steps - 1) * thetaIndex;
                float sinTheta = (float)Math.Sin(theta);
                float cosTheta = (float)Math.Cos(theta);

                for (int phiIndex = 0; phiIndex < steps; phiIndex++) {
                    // [-PI,PI]
                    float phi = (2.0f * MathF.PI) / (steps - 0) * phiIndex - MathF.PI;
                    float sinPhi = (float)Math.Sin(phi);
                    float cosPhi = (float)Math.Cos(phi);

                    JVector dir = new JVector(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi);

                    int index = FindExtremePoint(pointCloud, ref dir);
                    allIndices.Add(pointCloud[index]);
                }
            }

            return allIndices.Distinct().ToList();
        }

        private static int FindExtremePoint(List<JVector> points, ref JVector dir)
        {
            int index = 0;
            float current = float.MinValue;

            JVector point; float value;

            for (int i = 1; i < points.Count; i++) {
                point = points[i];

                value = JVector.Dot(ref point, ref dir);
                if (value > current) { current = value; index = i; }
            }

            return index;
        }

    }

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

        private World.RaycastFilterPre rayCast;

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
        public Vector3 Position = Vector3.Zero;

        public float AngularVelocity => angVel;

        /// <summary> Sets or gets the current steering angle of the wheel in degrees. </summary>
        public float SteerAngle { get => steerAngle; set { heldSteerAngle = value; } }

        /// <summary> Gets the current rotation of the wheel in degrees. </summary>
        public float WheelRotation { get; private set; }

        [Space()]
        [Seperator()]
        [Space()]
        [Header("Calculate default forces based on Mass, Radius and Wheel Count.")]
        [NonSerialized] public int WheelCount = 4;

        private const float dampingFrac = 0.8f;
        private const float springFrac = 0.45f;

        public void OnEnable()
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
            float wheelMass = car.Mass * 0.03f;

            Inertia = 0.5f * (Radius * Radius) * wheelMass;
            Spring = mass * car.Space.world.Gravity.Length() / (WheelTravel * springFrac);
            Damping = 2.0f * (float)Math.Sqrt(Spring * car.Mass) * 0.25f * dampingFrac;
        }

        /// <summary>
        /// Adds drivetorque.
        /// </summary>
        /// <param name="torque">The amount of torque applied to this wheel.</param>
        public void AddTorque(float torque) => heldTorque += torque;

        public void FixedUpdate()
        {
            car.Body.DeactivationTime = TimeSpan.MaxValue;

            PreStep();
            driveTorque = heldTorque;
            steerAngle = heldSteerAngle;
            PostStep();
            heldTorque = 0;
        }

        public void DrawGizmos()
        {
            car = GetComponentInParent<Rigidbody>();
            if (car == null) return;

            Vector3 worldPos = car.GameObject.Transform!.GlobalPosition + Vector3.Transform(Position, car.GameObject.Transform!.GlobalOrientation);
            worldPos -= (Camera.Current.GameObject.Transform?.GlobalPosition ?? Vector3.Zero);
            Vector3 worldAxis = Vector3.Transform(JVector.UnitY, car.GameObject.Transform!.GlobalOrientation);

            Matrix4x4 wheelMatrix = Matrix4x4.Identity;
            wheelMatrix = Matrix4x4.Multiply(wheelMatrix, Matrix4x4.CreateScale(Radius * 2f));
            wheelMatrix = Matrix4x4.Multiply(wheelMatrix, Matrix4x4.CreateRotationY((steerAngle + 90) * MathF.PI / 180));
            wheelMatrix = Matrix4x4.Multiply(wheelMatrix, Matrix4x4.CreateFromQuaternion(car.GameObject.Transform!.GlobalOrientation));
            wheelMatrix = Matrix4x4.Multiply(wheelMatrix, Matrix4x4.CreateTranslation(worldPos + (worldAxis * displacement)));

            Matrix4x4 centerMatrix = Matrix4x4.Identity;
            centerMatrix = Matrix4x4.Multiply(centerMatrix, Matrix4x4.CreateScale(0.02f));
            centerMatrix = Matrix4x4.Multiply(centerMatrix, Matrix4x4.CreateRotationY((steerAngle + 90) * MathF.PI / 180));
            centerMatrix = Matrix4x4.Multiply(centerMatrix, Matrix4x4.CreateFromQuaternion(car.GameObject.Transform!.GlobalOrientation));
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

            onFloor = car.Space.world.Raycast(worldPos, -worldAxis, rayCast, null, out Shape? shape, out JVector groundNormal, out float dist);
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