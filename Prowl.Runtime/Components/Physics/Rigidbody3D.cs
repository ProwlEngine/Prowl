// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.DataStructures;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// How a rigidbody's Transform is filled in between physics steps. Physics runs at a fixed rate, so
/// without smoothing the visuals move in fixed-rate jumps whenever the frame rate differs from it.
/// </summary>
public enum RigidbodyInterpolation
{
    /// <summary>Write the simulated pose as-is. Cheapest, and visibly steps at high frame rates.</summary>
    None,
    /// <summary>Render between the last two steps. Smooth, at the cost of trailing one fixed step behind.</summary>
    Interpolate,
    /// <summary>Predict ahead of the last step from the body's velocity. No lag, but can overshoot a collision.</summary>
    Extrapolate
}

[AddComponentMenu("Physics/Rigidbody")]
[ComponentIcon("\uf1b2")] // Cube
public sealed class Rigidbody3D : MonoBehaviour
{
    public class RigidBodyUserData
    {
        public Rigidbody3D Rigidbody { get; set; }
        public int InstanceID { get; set; }
        public int Layer { get; set; }
    }

    [SerializeField] private MotionType motionType = MotionType.Dynamic;
    [SerializeField] private bool isSpeculative;
    [SerializeField] private bool useGravity = true;
    [SerializeField] private bool enableGyroscopicForces = false;
    [SerializeField] private float mass = 1;
    [SerializeField] private float linearDamping = 0.0f;
    [SerializeField] private float angularDamping = 0.0f;
    [SerializeField] private float friction = 0.2f;
    [SerializeField] private float restitution = 0;
    [SerializeField] private float deactivationTime = 1.0f;
    [SerializeField] private float linearSleepThreshold = 0.1f;
    [SerializeField] private float angularSleepThreshold = 0.1f;

    [SerializeField] private RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;

    private float interpTimer = 0;

    // The poses the last two steps produced, and how far into the current step we have rendered.
    private Float3 _previousPosition, _currentPosition;
    private Quaternion _previousRotation, _currentRotation;
    private bool _hasPose;

    // Transform.Version last pushed into the physics body, so SyncTransformToBody only pushes when
    // the user actually edited the Transform (not when the physics readback wrote it).
    private uint _lastSyncedTransformVersion;

    /// <summary>
    /// How the Transform is filled in between fixed steps. Turn this on for anything the player
    /// watches closely; leave it off for bodies whose exact pose per frame does not matter.
    /// </summary>
    public RigidbodyInterpolation Interpolation
    {
        get => interpolation;
        set
        {
            interpolation = value;
            ResetPose();
        }
    }

    /// <summary>
    /// How this body participates in the simulation: <see cref="MotionType.Dynamic"/>,
    /// <see cref="MotionType.Kinematic"/>, or <see cref="MotionType.Static"/>.
    /// </summary>
    public MotionType MotionType
    {
        get => motionType;
        set
        {
            motionType = value;
            if (_body != null) _body.MotionType = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether speculative contacts are enabled.
    /// </summary>
    public bool EnableSpeculativeContacts
    {
        get => isSpeculative;
        set
        {
            isSpeculative = value;
            if (_body != null) _body.EnableSpeculativeContacts = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether this Rigidbody3D is affected by gravity.
    /// </summary>
    public bool AffectedByGravity
    {
        get => useGravity;
        set
        {
            useGravity = value;
            if (_body != null) _body.AffectedByGravity = value;
        }
    }

    /// <summary>
    /// Gets or sets the mass of this Rigidbody3D.
    /// </summary>
    public float Mass
    {
        get => mass;
        set
        {
            if (value <= 0.0)
                throw new ArgumentException("Mass can not be zero or negative.", nameof(value));

            mass = value;
            ApplyMassInertia();
        }
    }

    /// <summary>
    /// Gets or sets the friction of this Rigidbody3D.
    /// </summary>
    public float Friction
    {
        get => friction;
        set
        {
            if (value < 0.0 || value > 1.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Restitution must be between 0 and 1.");

            friction = value;
            if (_body != null) _body.Friction = value;
        }
    }

    /// <summary>
    /// Gets or sets the restitution of this Rigidbody3D.
    /// </summary>
    public float Restitution
    {
        get => restitution;
        set
        {
            if (value < 0.0 || value > 1.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Restitution must be between 0 and 1.");

            restitution = value;
            if (_body != null) _body.Restitution = value;
        }
    }

    /// <summary>
    /// Gets or sets the linear damping of this Rigidbody3D.
    /// Higher values slow down linear movement faster. Range: 0 to 1.
    /// </summary>
    public float LinearDamping
    {
        get => linearDamping;
        set
        {
            if (value < 0.0 || value > 1.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Linear damping must be between 0 and 1.");

            linearDamping = value;
            if (_body != null) _body.Damping = (linearDamping, _body.Damping.angular);
        }
    }

    /// <summary>
    /// Gets or sets the angular damping of this Rigidbody3D.
    /// Higher values slow down rotation faster. Range: 0 to 1.
    /// </summary>
    public float AngularDamping
    {
        get => angularDamping;
        set
        {
            if (value < 0.0 || value > 1.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Angular damping must be between 0 and 1.");

            angularDamping = value;
            if (_body != null) _body.Damping = (_body.Damping.linear, angularDamping);
        }
    }

    /// <summary>
    /// Gets or sets whether gyroscopic forces are enabled for this Rigidbody3D.
    /// Useful for spinning objects with high inertia anisotropy (like propellers).
    /// </summary>
    public bool EnableGyroscopicForces
    {
        get => enableGyroscopicForces;
        set
        {
            enableGyroscopicForces = value;
            if (_body != null) _body.EnableGyroscopicForces = value;
        }
    }

    /// <summary>
    /// Gets or sets the deactivation time in seconds.
    /// The body sleeps if velocity stays below threshold for this duration.
    /// </summary>
    public float DeactivationTime
    {
        get => deactivationTime;
        set
        {
            deactivationTime = value;
            if (_body != null) _body.DeactivationTime = System.TimeSpan.FromSeconds(value);
        }
    }

    /// <summary>
    /// Gets or sets the linear velocity threshold for sleeping.
    /// </summary>
    public float LinearSleepThreshold
    {
        get => linearSleepThreshold;
        set
        {
            linearSleepThreshold = value;
            if (_body != null) _body.DeactivationThreshold = (value, _body.DeactivationThreshold.angular);
        }
    }

    /// <summary>
    /// Gets or sets the angular velocity threshold for sleeping (radians/second).
    /// </summary>
    public float AngularSleepThreshold
    {
        get => angularSleepThreshold;
        set
        {
            angularSleepThreshold = value;
            if (_body != null) _body.DeactivationThreshold = (_body.DeactivationThreshold.linear, value);
        }
    }

    /// <summary>
    /// Gets whether this Rigidbody3D is currently active (not sleeping).
    /// </summary>
    public bool IsActive => _body?.IsActive ?? false;

    /// <summary>
    /// Gets or sets the Linear Velocity of this Rigidbody3D.
    /// </summary>
    public Float3 LinearVelocity
    {
        get => _body == null ? Float3.Zero : new(_body.Velocity.X, _body.Velocity.Y, _body.Velocity.Z);
        set { EnsureBody(); if (_body != null) _body.Velocity = new(value.X, value.Y, value.Z); }
    }

    /// <summary>
    /// Gets or sets the Angular Velocity of this Rigidbody3D.
    /// </summary>
    public Float3 AngularVelocity
    {
        get => _body == null ? Float3.Zero : new(_body.AngularVelocity.X, _body.AngularVelocity.Y, _body.AngularVelocity.Z);
        set { EnsureBody(); if (_body != null) _body.AngularVelocity = new(value.X, value.Y, value.Z); }
    }

    /// <summary>
    /// Gets or sets the Torque of this Rigidbody3D.
    /// </summary>
    public Float3 Torque
    {
        get => _body == null ? Float3.Zero : new(_body.Torque.X, _body.Torque.Y, _body.Torque.Z);
        set { EnsureBody(); if (_body != null) _body.Torque = new JVector(value.X, value.Y, value.Z); }
    }

    [SerializeIgnore]
    internal RigidBody _body;

    // The collider each live contact is against, recorded when the contact forms. OnCollisionEnd runs
    // after Jitter has freed the contact data, so this is the only way it can still name the surface.
    private readonly Dictionary<Arbiter, Collider> _contactColliders = [];

    /// <summary>
    /// Ensures the underlying Jitter body exists. Body creation normally happens in OnEnable, but
    /// game code can touch a rigidbody (e.g. set velocity or add a force) in the same frame it is
    /// added, before OnEnable runs create it on demand so those calls don't hit a null body.
    /// </summary>
    private void EnsureBody()
    {
        if (_body != null && !_body.Handle.IsZero) return;
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        World? world = scene.IsValid() ? scene.Physics?.World : null;
        if (world != null) CreateBody(world);
    }

    public RigidBody CreateBody(World world)
    {
        _body = world.CreateRigidBody();
        UpdateProperties(_body);
        UpdateShapes(_body);
        UpdateTransform(_body);
        _lastSyncedTransformVersion = Transform.Version; // initial pose is already in the body
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsValid()) scene.Physics?.RegisterBody(this);
        _body.Tag = new RigidBodyUserData()
        {
            Rigidbody = this,
            InstanceID = this.InstanceID,
            Layer = GameObject.LayerIndex,
            //HasTransformConstraints = rotationConstraints != Vector3Int.one || translationConstraints != Vector3Int.one,
            //RotationConstraint = new JVector(rotationConstraints.x, rotationConstraints.y, rotationConstraints.z),
            //TranslationConstraint = new JVector(translationConstraints.x, translationConstraints.y, translationConstraints.z)
        };

        // Hook up collision events
        _body.BeginCollide += OnJitterBeginCollide;
        _body.EndCollide += OnJitterEndCollide;

        return _body;
    }

    private void OnJitterBeginCollide(Arbiter arbiter)
    {
        RigidBody otherBody = arbiter.Body1 == _body ? arbiter.Body2 : arbiter.Body1;
        var userData = otherBody.Tag as RigidBodyUserData;

        Collider collider = ResolveOtherCollider(arbiter, otherBody);
        _contactColliders[arbiter] = collider;

        // Contact data lives in unmanaged memory that is valid only while the arbiter is.
        ref ContactData data = ref arbiter.Handle.Data;
        JVector normal = data.Contact0.Normal;
        JVector worldPos = otherBody.Position + data.Contact0.RelativePosition2;

        SceneDispatcher.CollisionBegin(GameObject, new Collision(
            userData?.Rigidbody, collider,
            new Float3(worldPos.X, worldPos.Y, worldPos.Z),
            new Float3(normal.X, normal.Y, normal.Z),
            data.Contact0.Impulse));
    }

    private void OnJitterEndCollide(Arbiter arbiter)
    {
        RigidBody otherBody = arbiter.Body1 == _body ? arbiter.Body2 : arbiter.Body1;
        var userData = otherBody.Tag as RigidBodyUserData;

        // Jitter frees the contact data before raising this, so the shape ids are already gone and
        // there is no contact point to report. The collider is recovered from what Begin recorded.
        _contactColliders.Remove(arbiter, out Collider collider);

        SceneDispatcher.CollisionEnd(GameObject, new Collision(
            userData?.Rigidbody, collider.IsValid() ? collider : null, Float3.Zero, Float3.Zero, 0.0f));
    }

    /// <summary>
    /// Which <see cref="Collider"/> on the other body this contact is against. Static colliders share
    /// one body per layer, so the body alone cannot say what was hit; the arbiter names the two shapes
    /// by id, and the physics world maps those back to the colliders that created them.
    /// </summary>
    private Collider ResolveOtherCollider(Arbiter arbiter, RigidBody otherBody)
    {
        PhysicsWorld physics = GameObject.IsValid() && GameObject.Scene.IsValid() ? GameObject.Scene.Physics : null;
        if (physics == null) return null;

        ArbiterKey key = arbiter.Handle.Data.Key;

        Collider first = physics.GetShapeOwner(key.Key1);
        if (first.IsValid() && first.AttachedBody == otherBody) return first;

        Collider second = physics.GetShapeOwner(key.Key2);
        if (second.IsValid() && second.AttachedBody == otherBody) return second;

        return null;
    }

    public override void OnValidate()
    {
        if (GameObject.IsNotValid() || GameObject.Scene.IsNotValid()) return;

        World? world = GameObject.Scene.Physics?.World;
        if (world == null) return;

        // Route through CreateBody so a body created here is wired up the same as one from OnEnable.
        // A raw CreateRigidBody would leave the collision events unhooked and the body out of the
        // transform-sync set, and OnEnable would then skip creation because a body already exists.
        if (_body == null || _body.Handle.IsZero)
        {
            CreateBody(world);
            return;
        }

        UpdateProperties(_body);
        UpdateShapes(_body);
        UpdateTransform(_body);
    }

    public override void Update()
    {
        if (_body == null || _body.Handle.IsZero) return;

        // Dynamic AND kinematic bodies move within the simulation (kinematic via LinearVelocity /
        // MovePosition), so the transform must follow the body. Only static bodies don't move - writing
        // their pose back would clobber a script-set transform. (This body->transform readback is
        // independent of AutoSyncTransforms, which only governs the transform->body direction.)
        if (motionType == MotionType.Static) return;

        interpTimer += Time.DeltaTime;

        Float3 position;
        Quaternion rotation;

        if (interpolation == RigidbodyInterpolation.None || !_hasPose)
        {
            position = ToFloat3(_body.Position);
            rotation = ToQuaternion(_body.Orientation);
        }
        else if (interpolation == RigidbodyInterpolation.Extrapolate)
        {
            _body.PredictPose(interpTimer, out JVector predicted, out JQuaternion predictedOrientation);
            position = ToFloat3(predicted);
            rotation = ToQuaternion(predictedOrientation);
        }
        else
        {
            // Render between the last two steps. The visual trails the simulation by up to one fixed
            // step, which is the price of never overshooting into geometry the solver has not seen.
            float t = Time.FixedDeltaTime > 0.0f ? Maths.Clamp(interpTimer / Time.FixedDeltaTime, 0.0f, 1.0f) : 1.0f;
            position = Maths.Lerp(_previousPosition, _currentPosition, t);
            rotation = Quaternion.Slerp(_previousRotation, _currentRotation, t);
        }

        Transform.Position = position;
        Transform.Rotation = rotation;

        // Remember the version we just wrote so the transform->body sync doesn't treat this
        // physics-driven change as a user edit and push it straight back.
        _lastSyncedTransformVersion = Transform.Version;
    }

    /// <summary>
    /// Records the pose the step just produced, so the next frames can render between it and the one
    /// before. Driven by the physics world for every registered body right after the step.
    /// </summary>
    internal void CapturePose()
    {
        if (_body == null || _body.Handle.IsZero) return;

        interpTimer = 0.0f;
        _previousPosition = _currentPosition;
        _previousRotation = _currentRotation;
        _currentPosition = ToFloat3(_body.Position);
        _currentRotation = ToQuaternion(_body.Orientation);

        if (!_hasPose)
        {
            _previousPosition = _currentPosition;
            _previousRotation = _currentRotation;
            _hasPose = true;
        }
    }

    /// <summary>
    /// Drops the interpolation history so a teleport snaps instead of being smeared across a frame.
    /// </summary>
    private void ResetPose()
    {
        if (_body == null || _body.Handle.IsZero) { _hasPose = false; return; }

        interpTimer = 0.0f;
        _currentPosition = _previousPosition = ToFloat3(_body.Position);
        _currentRotation = _previousRotation = ToQuaternion(_body.Orientation);
        _hasPose = true;
    }

    private static Float3 ToFloat3(JVector v) => new(v.X, v.Y, v.Z);
    private static Quaternion ToQuaternion(JQuaternion q) => new(q.X, q.Y, q.Z, q.W);

    public override void DrawGizmos()
    {
        // TODO DrawGizmos
    }

    public override void OnEnable()
    {
        if (_body == null || _body.Handle.IsZero)
        {
            CreateBody(GameObject.Scene.Physics.World);
        }

        // Claim all child colliders that aren't already claimed
        ClaimChildColliders();
    }

    /// <summary>
    /// Claims all colliders in this GameObject and its children that aren't already claimed by another rigidbody.
    /// </summary>
    private void ClaimChildColliders()
    {
        if (_body == null || _body.Handle.IsZero)
            return;

        // Get all colliders in this GameObject and its children
        var colliders = GetComponentsInChildren<Collider>();

        foreach (var collider in colliders)
        {
            // Try to attach the collider to this rigidbody
            // This will fail if the collider is already claimed by another rigidbody
            collider.TryAttachTo(this);
        }
    }

    public override void OnDisable()
    {
        if (_body == null || _body.Handle.IsZero) return;

        // Take the colliders off while the body is still alive, so their shapes are removed cleanly.
        Collider[] colliders = GetComponentsInChildren<Collider>().ToArray();
        foreach (Collider collider in colliders)
            if (collider.IsValid()) collider.Detach();

        // Unhook collision events. Removing the body discards its arbiters without raising EndCollide,
        // so the per-contact colliders have to be dropped here.
        _body.BeginCollide -= OnJitterBeginCollide;
        _body.EndCollide -= OnJitterEndCollide;
        _contactColliders.Clear();

        GameObject.Scene.Physics.UnregisterBody(this);
        GameObject.Scene.Physics.World?.Remove(_body);

        // Only now that this body is gone will the colliders resolve past it, onto an outer rigidbody
        // or their layer's static body.
        foreach (Collider collider in colliders)
            if (collider.IsValid() && collider.EnabledInHierarchy) collider.Reattach();
    }

    internal void UpdateProperties(RigidBody rb)
    {
        rb.MotionType = motionType;
        rb.EnableSpeculativeContacts = isSpeculative;
        rb.Damping = (linearDamping, angularDamping);
        rb.Friction = friction;
        rb.AffectedByGravity = useGravity;
        rb.Restitution = restitution;
        rb.EnableGyroscopicForces = enableGyroscopicForces;
        rb.DeactivationTime = System.TimeSpan.FromSeconds(deactivationTime);
        rb.DeactivationThreshold = (linearSleepThreshold, angularSleepThreshold);
        rb.Tag = new RigidBodyUserData()
        {
            Rigidbody = this,
            InstanceID = this.InstanceID,
            Layer = GameObject.LayerIndex,
        };
        // Mass/inertia is set by RegisterShapes after colliders attach their shapes.
        // Calling rb.SetMassInertia(mass) here would iterate all currently-attached shapes,
        // which throws for TriangleShape (no volume). The fallback in UpdateShapes handles
        // the no-collider case.
    }

    /// <summary>
    /// Pushes <see cref="Mass"/> onto the Jitter body, deriving the inertia tensor from its shapes.
    /// Shapes with no volume (the TriangleShapes of a concave MeshCollider) cannot report inertia, so
    /// those bodies fall back to a solid box sized to the combined bounds of every attached shape - a
    /// rough tensor that still rotates plausibly beats no body at all.
    /// </summary>
    internal void ApplyMassInertia()
    {
        if (_body == null || _body.Handle.IsZero) return;

        try
        {
            _body.SetMassInertia(mass);
        }
        catch (NotSupportedException)
        {
            _body.SetMassInertia(ApproximateBoxInertia(_body.Shapes, mass), mass);
        }
    }

    private static JMatrix ApproximateBoxInertia(ReadOnlyList<RigidBodyShape> shapes, float mass)
    {
        if (shapes.Count == 0) return JMatrix.Identity;

        JVector min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        JVector max = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (RigidBodyShape shape in shapes)
        {
            shape.CalculateBoundingBox(JQuaternion.Identity, JVector.Zero, out JBoundingBox box);
            min = JVector.Min(min, box.Min);
            max = JVector.Max(max, box.Max);
        }

        // Clamp to a small positive size so the tensor stays positive-definite (invertible) even for
        // perfectly flat or degenerate meshes.
        float sx = Maths.Max(max.X - min.X, 1e-3f);
        float sy = Maths.Max(max.Y - min.Y, 1e-3f);
        float sz = Maths.Max(max.Z - min.Z, 1e-3f);

        JMatrix inertia = JMatrix.Identity;
        inertia.M11 = (1.0f / 12.0f) * mass * (sy * sy + sz * sz);
        inertia.M22 = (1.0f / 12.0f) * mass * (sx * sx + sz * sz);
        inertia.M33 = (1.0f / 12.0f) * mass * (sx * sx + sy * sy);
        return inertia;
    }

    internal void UpdateShapes(RigidBody rb)
    {
        // Drop the owner entries first: the colliders below re-register their own shapes, but a shape
        // left over from a collider that has since gone would otherwise linger in the world's map.
        PhysicsWorld physics = GameObject.IsValid() && GameObject.Scene.IsValid() ? GameObject.Scene.Physics : null;
        if (physics != null)
            foreach (RigidBodyShape shape in rb.Shapes)
                physics.UnregisterShapeOwner(shape);

        // Remove all shapes from this rigidbody (Preserve mass/inertia, the rebuild below will refresh it)
        rb.RemoveShapes(rb.Shapes, MassInertiaUpdateMode.Preserve);

        // Get all child colliders and have them re-attach
        var colliders = GetComponentsInChildren<Collider>();
        foreach (var collider in colliders)
        {
            // Detach the collider first (in case it's attached to us or static)
            collider.Detach();
            // Then try to attach it to this rigidbody
            collider.TryAttachTo(this);
        }

        // If no colliders provided shapes, RegisterShapes was never called and mass was never set.
        if (rb.Shapes.Count == 0)
            ApplyMassInertia();
    }

    /// <summary>Unconditionally pushes the Transform pose into the body. WHEN this runs is controlled
    /// by the caller (initial creation, the pre-step sync, or an auto-synced query).</summary>
    internal void UpdateTransform(RigidBody rb)
    {
        rb.Position = new JVector(Transform.Position.X, Transform.Position.Y, Transform.Position.Z);
        rb.Orientation = new JQuaternion(Transform.Rotation.X, Transform.Rotation.Y, Transform.Rotation.Z, Transform.Rotation.W);
        ResetPose();
    }

    /// <summary>
    /// Pushes the Transform into the physics body, but only if the Transform changed since the last
    /// sync (a genuine user edit) - so it never clobbers the simulated pose. Called by the physics
    /// world before each step and, when AutoSyncTransforms is on, before queries.
    /// </summary>
    internal void SyncTransformToBody()
    {
        if (_body == null || _body.Handle.IsZero) return;
        if (Transform.Version == _lastSyncedTransformVersion) return;
        UpdateTransform(_body);
        _lastSyncedTransformVersion = Transform.Version;
    }

    /// <summary>
    /// Applies a force through the body's centre of mass, so it accelerates without spinning.
    /// </summary>
    public void AddForce(Float3 force, ForceMode mode = ForceMode.Force)
    {
        if (!TryGetBody(out RigidBody body)) return;

        var jForce = new JVector(force.X, force.Y, force.Z);
        float inverseMass = body.Data.InverseMass;

        switch (mode)
        {
            case ForceMode.Force:
                body.AddForce(jForce);
                break;

            case ForceMode.Acceleration:
                // a = F/m, so cancelling the mass means asking for a force of m*a.
                if (inverseMass > 0.0f) body.AddForce(jForce * (1.0f / inverseMass));
                break;

            case ForceMode.Impulse:
                body.Velocity += jForce * inverseMass;
                body.SetActivationState(true);
                break;

            case ForceMode.VelocityChange:
                body.Velocity += jForce;
                body.SetActivationState(true);
                break;
        }
    }

    /// <summary>
    /// Applies a force at a world-space point, which also spins the body about its centre of mass.
    /// Only <see cref="ForceMode.Force"/> and <see cref="ForceMode.Impulse"/> are meaningful here; the
    /// mass-independent modes have no defined torque.
    /// </summary>
    public void AddForceAtPosition(Float3 force, Float3 worldPosition, ForceMode mode = ForceMode.Force)
    {
        if (!TryGetBody(out RigidBody body)) return;

        var jForce = new JVector(force.X, force.Y, force.Z);
        var jPosition = new JVector(worldPosition.X, worldPosition.Y, worldPosition.Z);

        if (mode == ForceMode.Impulse)
        {
            ApplyImpulse(force, worldPosition);
            return;
        }

        if (mode == ForceMode.Acceleration)
        {
            float inverseMass = body.Data.InverseMass;
            if (inverseMass <= 0.0f) return;
            jForce *= 1.0f / inverseMass;
        }

        body.AddForce(jForce, jPosition);
    }

    /// <summary>
    /// Applies a torque about the body's centre of mass.
    /// </summary>
    public void AddTorque(Float3 torque, ForceMode mode = ForceMode.Force)
    {
        if (!TryGetBody(out RigidBody body)) return;

        var jTorque = new JVector(torque.X, torque.Y, torque.Z);

        switch (mode)
        {
            case ForceMode.Force:
                body.Torque += jTorque;
                break;

            case ForceMode.Acceleration:
                // Cancelling the inertia means asking for a torque of I*alpha.
                if (JMatrix.Inverse(body.InverseInertia, out JMatrix inertia))
                    body.Torque += JVector.Transform(jTorque, inertia);
                break;

            case ForceMode.Impulse:
                ApplyAngularImpulse(torque);
                break;

            case ForceMode.VelocityChange:
                body.AngularVelocity += jTorque;
                body.SetActivationState(true);
                break;
        }
    }

    /// <summary>
    /// The live body, creating it on demand. False when this component cannot have one (no scene yet,
    /// or it has been removed), which is the cue for every mutator to do nothing rather than throw.
    /// </summary>
    private bool TryGetBody(out RigidBody body)
    {
        EnsureBody();
        body = _body;
        return body != null && !body.Handle.IsZero;
    }

    /// <summary>
    /// Sets the activation state of this rigidbody (awake or sleeping).
    /// </summary>
    public void SetActive(bool active)
    {
        if (_body != null)
            _body.SetActivationState(active);
    }

    /// <summary>
    /// Gets the velocity at a world space point on this rigidbody.
    /// </summary>
    public Float3 GetPointVelocity(Float3 worldPoint)
    {
        if (_body == null) return Float3.Zero;

        var point = new JVector(worldPoint.X, worldPoint.Y, worldPoint.Z);
        JVector r = point - _body.Position;
        JVector velocity = _body.Velocity + JVector.Cross(_body.AngularVelocity, r);

        return new Float3(velocity.X, velocity.Y, velocity.Z);
    }

    /// <summary>
    /// Gets the center of mass in world space.
    /// </summary>
    public Float3 CenterOfMass
    {
        get
        {
            if (_body == null) return Transform.Position;
            JVector pos = _body.Position;
            return new Float3(pos.X, pos.Y, pos.Z);
        }
    }

    /// <summary>
    /// The diagonal of this body's inertia tensor in body space, in kg*m^2. Only the moments about the
    /// body's own axes; a shape whose principal axes are rotated relative to the body also has
    /// off-diagonal terms that this does not report.
    /// </summary>
    public Float3 InertiaTensor
    {
        get
        {
            // Reciprocals of the inverse diagonal are not the moments unless the tensor is diagonal,
            // so invert the matrix properly.
            if (_body == null || !JMatrix.Inverse(_body.InverseInertia, out JMatrix inertia))
                return Float3.One;

            return new Float3(inertia.M11, inertia.M22, inertia.M33);
        }
    }

    /// <summary>
    /// Applies an impulse at a position, immediately affecting velocity.
    /// </summary>
    public void ApplyImpulse(Float3 impulse, Float3 worldPosition)
    {
        if (!TryGetBody(out RigidBody body)) return;

        var jImpulse = new JVector(impulse.X, impulse.Y, impulse.Z);
        var jPosition = new JVector(worldPosition.X, worldPosition.Y, worldPosition.Z);

        JVector r = jPosition - body.Position;
        body.Velocity += jImpulse * body.Data.InverseMass;
        body.AngularVelocity += JVector.Transform(JVector.Cross(r, jImpulse), body.Data.InverseInertiaWorld);

        SetActive(true);
    }

    /// <summary>
    /// Applies an impulse to the rigidbody, immediately affecting velocity.
    /// </summary>
    public void ApplyImpulse(Float3 impulse)
    {
        if (!TryGetBody(out RigidBody body)) return;

        var jImpulse = new JVector(impulse.X, impulse.Y, impulse.Z);
        body.Velocity += jImpulse * body.Data.InverseMass;

        SetActive(true);
    }

    /// <summary>
    /// Applies an angular impulse to the rigidbody, immediately affecting angular velocity.
    /// </summary>
    public void ApplyAngularImpulse(Float3 angularImpulse)
    {
        if (!TryGetBody(out RigidBody body)) return;

        var jImpulse = new JVector(angularImpulse.X, angularImpulse.Y, angularImpulse.Z);
        body.AngularVelocity += JVector.Transform(jImpulse, body.Data.InverseInertiaWorld);

        SetActive(true);
    }

    /// <summary>
    /// Moves the rigidbody to a new position (teleport).
    /// </summary>
    public void MovePosition(Float3 position)
    {
            ResetPose();
        }
    }

    /// <summary>
    /// Rotates the rigidbody to a new rotation (teleport).
    /// </summary>
    public void MoveRotation(Quaternion rotation)
    {
            ResetPose();
        }
    }
}
