// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

// NOTE: This implementation is based on the raycast wheel from Jitter2's demo.

using System;
using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A raycast-based wheel collider for vehicle physics.
/// Uses raycasting instead of actual collision shapes for better performance and stability.
/// </summary>
[AddComponentMenu("Physics/Wheel Collider")]
public sealed class WheelCollider : MonoBehaviour
{
    [SerializeField] private float radius = 0.5f;
    [SerializeField] private float suspensionTravel = 0.2f;
    [SerializeField] private float suspensionStiffness = 50.0f;
    [SerializeField] private float suspensionDamping = 5.0f;
    [SerializeField] private float sideFriction = 3.0f;
    [SerializeField] private float forwardFriction = 5.0f;
    [SerializeField] private float wheelInertia = 1.0f;
    [SerializeField] private float maxAngularVelocity = 200.0f;
    [SerializeField] private int numberOfRays = 10;
    [SerializeField] private bool locked = false;

    private Rigidbody3D rigidbody;
    private float displacement;
    private float upSpeed;
    private float lastDisplacement;
    private bool onFloor;
    private float driveTorque;
    private float angularVelocity;
    private float angularVelocityForGrip;
    private float torque;
    private float wheelRotation;
    private float steerAngle;

    private const float DampingFrac = 0.8f;
    private const float SpringFrac = 0.45f;

    /// <summary>
    /// Gets or sets the steering angle of the wheel in radians.
    /// </summary>
    public float SteerAngle
    {
        get => steerAngle;
        set => steerAngle = value;
    }

    /// <summary>
    /// Gets the current rotation of the wheel in radians.
    /// </summary>
    public float WheelRotation => wheelRotation;

    /// <summary>
    /// Gets the current angular velocity of the wheel.
    /// </summary>
    public float AngularVelocity => angularVelocity;

    /// <summary>
    /// Gets whether the wheel is currently touching the ground.
    /// </summary>
    public bool IsGrounded => onFloor;

    /// <summary>
    /// Gets the current suspension compression (0 = fully extended, 1 = fully compressed).
    /// </summary>
    public float SuspensionCompression => suspensionTravel > 0 ? displacement / suspensionTravel : 0;

    /// <summary>
    /// Gets or sets the wheel radius.
    /// </summary>
    public float Radius
    {
        get => radius;
        set => radius = value;
    }

    /// <summary>
    /// Gets or sets the maximum suspension travel distance.
    /// </summary>
    public float SuspensionTravel
    {
        get => suspensionTravel;
        set => suspensionTravel = value;
    }

    /// <summary>
    /// Gets or sets the suspension spring stiffness.
    /// </summary>
    public float SuspensionStiffness
    {
        get => suspensionStiffness;
        set => suspensionStiffness = value;
    }

    /// <summary>
    /// Gets or sets the suspension damping.
    /// </summary>
    public float SuspensionDamping
    {
        get => suspensionDamping;
        set => suspensionDamping = value;
    }

    /// <summary>
    /// Gets or sets the sideways friction coefficient.
    /// </summary>
    public float SideFriction
    {
        get => sideFriction;
        set => sideFriction = value;
    }

    /// <summary>
    /// Gets or sets the forward friction coefficient.
    /// </summary>
    public float ForwardFriction
    {
        get => forwardFriction;
        set => forwardFriction = value;
    }

    /// <summary>
    /// Gets or sets the wheel inertia.
    /// </summary>
    public float WheelInertia
    {
        get => wheelInertia;
        set => wheelInertia = value;
    }

    /// <summary>
    /// Gets or sets the maximum angular velocity of the wheel.
    /// </summary>
    public float MaxAngularVelocity
    {
        get => maxAngularVelocity;
        set => maxAngularVelocity = value;
    }

    /// <summary>
    /// Gets or sets the number of rays used for ground detection.
    /// More rays = more stable but more expensive.
    /// </summary>
    public int NumberOfRays
    {
        get => numberOfRays;
        set => numberOfRays = Maths.Max(1, value);
    }

    /// <summary>
    /// Gets or sets whether the wheel is locked (acts as a brake).
    /// </summary>
    public bool Locked
    {
        get => locked;
        set => locked = value;
    }

    public override void OnEnable()
    {
        rigidbody = GetComponentInParent<Rigidbody3D>();
        if (rigidbody == null)
        {
            Debug.LogError("WheelCollider requires a Rigidbody3D component on the same GameObject.");
            return;
        }

        // Subscribe to physics events
        GameObject.Scene.Physics.PreStep += OnPreStep;
        GameObject.Scene.Physics.PostStep += OnPostStep;

        AdjustWheelValues();
    }

    public override void OnDisable()
    {
        // Unsubscribe from physics events
        if (GameObject?.Scene?.Physics != null)
        {
            GameObject.Scene.Physics.PreStep -= OnPreStep;
            GameObject.Scene.Physics.PostStep -= OnPostStep;
        }
    }

    /// <summary>
    /// Adjusts wheel inertia, spring, and damping based on the rigidbody mass and gravity.
    /// Should be called after changing wheel or rigidbody properties.
    /// </summary>
    public void AdjustWheelValues()
    {
        rigidbody = GetComponentInParent<Rigidbody3D>();
        if (rigidbody == null)
        {
            Debug.LogError("WheelCollider requires a Rigidbody3D component on the same GameObject.");
            return;
        }

        float mass = (float)rigidbody.Mass / 4.0f;
        float wheelMass = (float)(rigidbody.Mass * 0.03);
        wheelInertia = 0.5f * (radius * radius) * wheelMass;
    
        float gravity = (float)Float3.Length(GameObject.Scene.Physics.Gravity);
        suspensionStiffness = mass * gravity / (suspensionTravel * SpringFrac);
        suspensionDamping = 2.0f * (float)Maths.Sqrt(suspensionStiffness * rigidbody.Mass) * 0.25f * DampingFrac;
    }

    /// <summary>
    /// Adds drive torque to the wheel.
    /// </summary>
    public void AddTorque(float torque)
    {
        driveTorque += torque;
    }

    /// <summary>
    /// Gets the world position of the wheel center.
    /// </summary>
    public Float3 GetWheelCenter()
    {
        var up = Transform.Up;
        return Transform.Position + up * displacement;
    }

    private void OnPostStep(float timeStep)
    {
        if (rigidbody == null || rigidbody._body == null || timeStep <= 0.0) return;

        float dt = (float)timeStep;
        float origAngVel = angularVelocity;
        upSpeed = (displacement - lastDisplacement) / dt;

        if (locked)
        {
            angularVelocity = 0;
            torque = 0;
        }
        else
        {
            angularVelocity += torque * dt / wheelInertia;
            torque = 0;

            if (!onFloor) driveTorque *= 0.1f;

            // Prevent friction from reversing direction
            if ((origAngVel > angularVelocityForGrip && angularVelocity < angularVelocityForGrip) ||
                (origAngVel < angularVelocityForGrip && angularVelocity > angularVelocityForGrip))
                angularVelocity = angularVelocityForGrip;

            angularVelocity += driveTorque * dt / wheelInertia;
            driveTorque = 0;

            angularVelocity = Maths.Clamp(angularVelocity, -maxAngularVelocity, maxAngularVelocity);

            wheelRotation += dt * angularVelocity;
        }
    }

    private void OnPreStep(float timeStep)
    {
        if (rigidbody == null || rigidbody._body == null || timeStep <= 0.0) return;

        float dt = (float)timeStep;
        RigidBody car = rigidbody._body;
        World world = car.World;

        JVector force = JVector.Zero;
        lastDisplacement = displacement;
        displacement = 0.0f;

        // Get wheel position and orientation in world space
        var wheelPosWorld = new JVector(Transform.Position.X, Transform.Position.Y, Transform.Position.Z);
        var worldAxis = new JVector(Transform.Up.X, Transform.Up.Y, Transform.Up.Z);
        JVector.NormalizeInPlace(ref worldAxis);

        var forward = new JVector(Transform.Forward.X, Transform.Forward.Y, Transform.Forward.Z);
        JVector wheelFwd = JVector.Transform(forward, JMatrix.CreateRotationMatrix(worldAxis, steerAngle));

        JVector wheelLeft = JVector.Cross(worldAxis, wheelFwd);
        JVector.NormalizeInPlace(ref wheelLeft);

        JVector wheelUp = JVector.Cross(wheelFwd, wheelLeft);

        float rayLen = 2.0f * radius + suspensionTravel;

        JVector wheelRayEnd = wheelPosWorld - radius * worldAxis;
        JVector wheelRayOrigin = wheelRayEnd + rayLen * worldAxis;
        JVector wheelRayDelta = wheelRayEnd - wheelRayOrigin;

        float deltaFwd = 2.0f * radius / (numberOfRays + 1);
        float deltaFwdStart = deltaFwd;

        onFloor = false;

        JVector groundNormal = JVector.Zero;
        JVector groundPos = JVector.Zero;
        float deepestFrac = float.MaxValue;
        RigidBody worldBody = null;

        // Perform raycasts
        for (int i = 0; i < numberOfRays; i++)
        {
            //float distFwd = deltaFwdStart + i * deltaFwd - radius;
            //float zOffset = radius * (1.0f - (float)Maths.Cos(Maths.PI / 2.0f * (distFwd / radius)));
            //
            //JVector newOrigin = wheelRayOrigin + distFwd * wheelFwd + zOffset * wheelUp;

            float distFwd = deltaFwdStart + i * deltaFwd - Radius;
            float normalizedDist = distFwd / Radius;
            float zOffset = Radius - Maths.Sqrt(Maths.Max(0, Radius * Radius - distFwd * distFwd));
            JVector newOrigin = wheelRayOrigin + distFwd * wheelFwd + zOffset * wheelUp;


            bool result = world.DynamicTree.RayCast(newOrigin, wheelRayDelta,
                RayCastFilter, null, out IDynamicTreeProxy shape, out JVector normal, out float frac);

            if (result && frac <= 1.0 && shape is RigidBodyShape rbs)
            {
                RigidBody body = rbs.RigidBody;

                if (frac < deepestFrac)
                {
                    deepestFrac = (float)frac;
                    groundPos = newOrigin + (float)frac * wheelRayDelta;
                    worldBody = body;
                    groundNormal = normal;
                }

                onFloor = true;
            }
        }

        if (!onFloor) return;

        if (groundNormal.LengthSquared() > 0.0)
            JVector.NormalizeInPlace(ref groundNormal);

        displacement = rayLen * (1.0f - deepestFrac);
        displacement = Maths.Clamp(displacement, 0.0f, suspensionTravel);

        float displacementForceMag = displacement * suspensionStiffness;

        // Reduce force when suspension is parallel to ground
        displacementForceMag *= (float)JVector.Dot(groundNormal, worldAxis);

        // Apply damping
        float dampingForceMag = upSpeed * suspensionDamping;

        float totalForceMag = displacementForceMag + dampingForceMag;

        if (totalForceMag < 0.0f) totalForceMag = 0.0f;

        JVector extraForce = totalForceMag * worldAxis;
        force += extraForce;

        // Calculate ground-relative frame
        JVector groundUp = groundNormal;
        JVector groundLeft = JVector.Cross(groundNormal, wheelFwd);
        if (groundLeft.LengthSquared() > 0.0f)
            JVector.NormalizeInPlace(ref groundLeft);

        JVector groundFwd = JVector.Cross(groundLeft, groundUp);

        // Calculate wheel point velocity
        JVector wheelPointVel = car.Velocity +
                                JVector.Cross(car.AngularVelocity, wheelPosWorld - car.Position);

        // Add rim velocity
        JVector rimVel = angularVelocity * JVector.Cross(wheelLeft, groundPos - wheelPosWorld);
        wheelPointVel += rimVel;
        Debug.Log(rimVel);

        if (worldBody != null)
        {
            JVector worldVel = worldBody.Velocity +
                               JVector.Cross(worldBody.AngularVelocity, groundPos - worldBody.Position);
            wheelPointVel -= worldVel;
        }

        // Sideways friction
        float noslipVel = 0.2f;
        float slipVel = 0.4f;
        float slipFactor = 0.7f;
        float smallVel = 3.0f;

        float friction = sideFriction;
        float sideVel = (float)JVector.Dot(wheelPointVel, groundLeft);

        if (Maths.Abs(sideVel) > slipVel)
        {
            friction *= slipFactor;
        }
        else if (Maths.Abs(sideVel) > noslipVel)
        {
            friction *= 1.0f - (1.0f - slipFactor) * (Maths.Abs(sideVel) - noslipVel) / (slipVel - noslipVel);
        }

        if (sideVel < 0.0f)
            friction *= -1.0f;

        if (Maths.Abs(sideVel) < smallVel)
            friction *= Maths.Abs(sideVel) / smallVel;

        float sideForce = -friction * totalForceMag;
        extraForce = sideForce * groundLeft;
        force += extraForce;

        // Forward/backward friction
        friction = forwardFriction;
        float fwdVel = (float)JVector.Dot(wheelPointVel, groundFwd);

        if (Maths.Abs(fwdVel) > slipVel)
        {
            friction *= slipFactor;
        }
        else if (Maths.Abs(fwdVel) > noslipVel)
        {
            friction *= 1.0f - (1.0f - slipFactor) * (Maths.Abs(fwdVel) - noslipVel) / (slipVel - noslipVel);
        }

        if (fwdVel < 0.0f)
            friction *= -1.0f;

        if (Maths.Abs(fwdVel) < smallVel)
            friction *= Maths.Abs(fwdVel) / smallVel;

        float fwdForce = -friction * totalForceMag;
        extraForce = fwdForce * groundFwd;
        force += extraForce;

        // Forward force spins the wheel
        JVector wheelCentreVel = car.Velocity +
                                 JVector.Cross(car.AngularVelocity, wheelPosWorld - car.Position);

        angularVelocityForGrip = (float)JVector.Dot(wheelCentreVel, groundFwd) / radius;
        torque += -fwdForce * radius;

        // Apply force to car
        car.AddForce(force, groundPos);

        // Apply force to the ground body
        if (worldBody != null && !worldBody.IsStatic)
        {
            const float maxOtherBodyAcc = 500.0f;
            float maxOtherBodyForce = maxOtherBodyAcc * (float)worldBody.Mass;

            if (force.LengthSquared() > (maxOtherBodyForce * maxOtherBodyForce))
                force *= maxOtherBodyForce / force.Length();

            worldBody.SetActivationState(true);
            worldBody.AddForce(force * -1, groundPos);
        }
    }

    private bool RayCastFilter(IDynamicTreeProxy shape)
    {
        if (shape is not RigidBodyShape rbs) return false;
        if (rigidbody?._body == null) return false;
        return rbs.RigidBody != rigidbody._body;
    }

    public override void DrawGizmos()
    {
        // Draw wheel visualization
        var wheelCenter = GetWheelCenter();


        var worldAxis = new JVector(Transform.Up.X, Transform.Up.Y, Transform.Up.Z);
        var forward = new JVector(Transform.Forward.X, Transform.Forward.Y, Transform.Forward.Z);
        JVector wheelFwd = JVector.Transform(forward, JMatrix.CreateRotationMatrix(worldAxis, steerAngle));

        JVector wheelLeft = JVector.Cross(worldAxis, wheelFwd);

        // Draw wheel sphere
        Color wheelColor = onFloor ? new Color(0f, 1f, 0f, 1f) : new Color(1f, 0f, 0f, 1f);

        // Draw suspension line
        Debug.DrawLine(Transform.Position, wheelCenter, new Color(1f, 1f, 0f, 1f));

        // Draw wheel circle (simplified)
        var up = Transform.Up;
        var right = Transform.Right;
        int segments = 16;
        Debug.DrawWireCircle(wheelCenter, new Float3(wheelLeft.X, wheelLeft.Y, wheelLeft.Z), radius, wheelColor, segments);
    }
}
