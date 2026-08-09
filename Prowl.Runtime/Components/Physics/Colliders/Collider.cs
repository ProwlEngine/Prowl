// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

[ComponentIcon("\uf1b2")] // Cube subclasses override with their specific shape
public abstract class Collider : MonoBehaviour
{
    [SerializeField] private Float3 center;
    [SerializeField] private Float3 rotation;

    /// <summary>Offset of the shape from the GameObject's origin, in local space.</summary>
    public Float3 Center
    {
        get => center;
        set { center = value; Rebuild(); }
    }

    /// <summary>Euler rotation of the shape relative to the GameObject, in degrees.</summary>
    public Float3 Rotation
    {
        get => rotation;
        set { rotation = value; Rebuild(); }
    }

    protected Float4x4 GizmoMatrix =>
        Float4x4.CreateTRS(
            Float4x4.TransformPoint(center, Float4x4.CreateTRS(Transform.Position, Transform.Rotation, Transform.LossyScale)),
            Transform.Rotation * Quaternion.FromEuler(rotation),
            Transform.LossyScale);

    /// <summary>
    /// The Jitter2 rigidbody this collider is currently attached to.
    /// This could be either a Rigidbody3D's body or the PhysicsWorld's static rigidbody.
    /// </summary>
    private Jitter2.Dynamics.RigidBody _attachedBody;

    /// <summary>
    /// The Rigidbody3D component this collider is attached to, if any.
    /// Null if attached to the static rigidbody.
    /// </summary>
    private Rigidbody3D _attachedRigidbody3D;

    /// <summary>
    /// The shapes created by this collider and added to the rigidbody.
    /// </summary>
    private RigidBodyShape[] _attachedShapes;

    /// <summary>
    /// The physics world holding shape-owner entries for <see cref="_attachedShapes"/>. Captured at
    /// registration so Detach can clean up even once the GameObject can no longer reach its scene.
    /// </summary>
    private PhysicsWorld _registeredWorld;

    /// <summary>
    /// Transform version tracking for static colliders.
    /// Used to detect when the transform has moved and shapes need updating.
    /// </summary>
    private uint _lastTransformVersion;

    /// <summary>
    /// Layer tracking for static colliders.
    /// Used to detect when the layer has changed and we need to move to a different static rigidbody.
    /// </summary>
    private int _lastLayer;

    /// <summary>The attached body's local scale when the shapes were built. Compared by value because
    /// the body's own Transform version changes every step and so cannot report a rescale.</summary>
    private Float3 _lastBodyScale = Float3.One;

    protected Rigidbody3D RigidBody => GetComponentInParent<Rigidbody3D>();

    /// <summary>The Jitter body this collider's shapes are currently on, or null when it is detached.</summary>
    internal Jitter2.Dynamics.RigidBody AttachedBody => _attachedBody;

    /// <summary>
    /// Returns true if this collider is already attached to a rigidbody.
    /// Used to prevent multiple rigidbodies from claiming the same collider.
    /// </summary>
    public bool IsClaimed => _attachedBody != null;

    /// <summary>
    /// Attempts to attach this collider to a Rigidbody3D.
    /// Returns false if the collider is already claimed by another rigidbody.
    /// </summary>
    internal bool TryAttachTo(Rigidbody3D rigidbody)
    {
        if (IsClaimed && _attachedRigidbody3D != rigidbody)
            return false; // Already claimed by a different rigidbody

        // Detach from current rigidbody if any
        Detach();

        // Attach to the new rigidbody
        _attachedRigidbody3D = rigidbody;
        _attachedBody = rigidbody._body;
        RegisterShapes();
        return true;
    }

    /// <summary>
    /// Attaches this collider to the static rigidbody in the physics world.
    /// Uses the GameObject's layer to determine which static rigidbody to attach to.
    /// </summary>
    private void AttachToStatic()
    {
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsNotValid() || scene.Physics == null)
            return;

        _attachedRigidbody3D = null;
        // Get or create the static rigidbody for this GameObject's layer
        int layer = GameObject.LayerIndex;
        _attachedBody = GameObject.Scene.Physics.GetOrCreateStaticRigidBody(layer);
        RegisterShapes();
        _lastTransformVersion = CurrentTransformVersion();
        _lastBodyScale = Float3.One; // static colliders have no body; the version walk covers their scale
        _lastLayer = layer;
    }

    /// <summary>
    /// A version that changes whenever this transform OR any ancestor up to <paramref name="stopAt"/>
    /// changes. Transform.Version only tracks local edits, so a parent has to be walked for a child's
    /// world-space shapes to follow it.
    /// <para/>
    /// Stopping at the attached body is what makes this usable for a moving rigidbody: the solver
    /// rewrites the body's own Transform every step, so including it would rebuild the shapes of every
    /// moving body every frame, while the offset between collider and body has not changed at all.
    /// </summary>
    private uint ComputeTransformVersion(Transform stopAt)
    {
        uint v = 17;
        for (Transform t = Transform; t != null && t != stopAt; t = t.Parent)
            v = v * 31 + t.Version;

        return v;
    }

    /// <summary>The version this collider's placement is currently built against.</summary>
    private uint CurrentTransformVersion() =>
        ComputeTransformVersion(_attachedRigidbody3D.IsValid() ? _attachedRigidbody3D.Transform : null);

    /// <summary>
    /// Rebuilds this collider's shapes in place, after its size, offset, orientation or the mesh behind
    /// it changed. Cheaper than going through the rigidbody, which rebuilds every collider on the body.
    /// </summary>
    public virtual void Rebuild()
    {
        if (_attachedBody == null) return; // not in the world yet, OnEnable will build it
        Reattach();
    }

    /// <summary>
    /// Detaches this collider from its current rigidbody.
    /// </summary>
    internal void Detach()
    {
        if (_attachedBody != null && _attachedShapes != null && !_attachedBody.Handle.IsZero)
        {
            // Only try to remove shapes if the body is still registered with the physics world
            // (If the rigidbody was already removed, the shapes are already gone)
            foreach (RigidBodyShape shape in _attachedShapes)
            {
                try
                {
                    // Use Preserve: Update mode calls SetMassInertia() after each removal, which
                    // iterates remaining shapes this throws NotSupportedException for TriangleShape.
                    // Mass/inertia is recalculated in full by RegisterShapes after re-attachment.
                    _attachedBody.RemoveShape(shape, Jitter2.Dynamics.MassInertiaUpdateMode.Preserve);
                }
                catch (ArgumentException)
                {
                    // Shape was already removed from this body (e.g., UpdateShapes pre-cleared the
                    // body with RemoveShapes before calling Detach). Safe to ignore.
                }
                catch (InvalidOperationException)
                {
                    // Body was removed from the physics world; its shapes are already gone.
                }
            }
        }

        if (_registeredWorld != null && _attachedShapes != null)
        {
            foreach (RigidBodyShape shape in _attachedShapes)
                _registeredWorld.UnregisterShapeOwner(shape);
        }

        _registeredWorld = null;
        _attachedBody = null;
        _attachedRigidbody3D = null;
        _attachedShapes = null;
    }

    /// <summary>
    /// Registers this collider's shapes with its attached rigidbody.
    /// </summary>
    private void RegisterShapes()
    {
        if (_attachedBody == null || _attachedBody.Handle.IsZero)
            return;

        // Create shapes based on whether we're attached to a Rigidbody3D or static
        if (_attachedRigidbody3D != null)
        {
            // Use transformed shapes for Rigidbody3D (existing behavior)
            _attachedShapes = CreateTransformedShapes();
        }
        else
        {
            // For static rigidbody, we need world-space transformed shapes
            _attachedShapes = CreateWorldTransformedShapes();
        }

        if (_attachedShapes != null)
        {
            var scene = GameObject.IsValid() ? GameObject.Scene : null;
            _registeredWorld = scene.IsValid() ? scene.Physics : null;

            foreach (RigidBodyShape shape in _attachedShapes)
            {
                // Always use Preserve: per-shape Update mode calls SetMassInertia() after every
                // addition, which iterates ALL attached shapes. Any TriangleShape in that set
                // throws NotSupportedException. We set mass once below, after all shapes are added.
                _attachedBody.AddShape(shape, Jitter2.Dynamics.MassInertiaUpdateMode.Preserve);
                _registeredWorld?.RegisterShapeOwner(shape, this);
            }
        }

        // Mass and inertia are derived from every shape on the body, so the rigidbody owns that step.
        // Static bodies need neither.
        if (_attachedRigidbody3D.IsValid()) _attachedRigidbody3D.ApplyMassInertia();
    }

    /// <summary>
    /// Create the Jitter Physics RigidBodyShape
    /// </summary>
    public abstract RigidBodyShape[] CreateShapes();

    /// <summary>
    /// Produce the collider's shapes with the given transform already applied to their geometry, or
    /// null to let the base class wrap <see cref="CreateShapes"/> in a <see cref="TransformedShape"/>.
    /// Triangle-mesh colliders must override this: Jitter's internal-edge filter only recognises a bare
    /// <c>TriangleShape</c>, so a wrapped one silently loses edge filtering and one-sided triangles.
    /// </summary>
    protected virtual RigidBodyShape[] CreateBakedShapes(Float4x4 transform) => null;

    /// <summary>
    /// The collider's world-space scale, clamped away from zero so shapes stay non-degenerate.
    /// </summary>
    private Float3 CumulativeScale()
    {
        Float3 scale = Float3.One;
        for (Transform current = Transform; current != null; current = current.Parent)
            scale *= current.LocalScale;

        return new Float3(ClampScale(scale.X), ClampScale(scale.Y), ClampScale(scale.Z));
    }

    // Clamps magnitude rather than value, so a mirrored (negative) axis stays mirrored instead of
    // flipping to a tiny positive one.
    private static float ClampScale(float value)
    {
        const float minScale = 1e-4f;
        if (value <= -minScale || value >= minScale) return value;
        return value < 0.0f ? -minScale : minScale;
    }

    /// <summary>
    /// Create the Jitter Physics RigidBodyShapes in the space of the attached Rigidbody3D.
    /// </summary>
    public RigidBodyShape[] CreateTransformedShapes()
    {
        // Prefer the body we are actually attached to over another walk up the component tree.
        Rigidbody3D rb = _attachedRigidbody3D.IsValid() ? _attachedRigidbody3D : RigidBody;
        if (rb.IsNotValid()) return CreateShapes();

        Float3 cumulativeScale = CumulativeScale();
        Float3 worldCenter = Transform.TransformPoint(center);
        Quaternion worldRotation = Transform.Rotation * Quaternion.FromEuler(rotation);

        // A Jitter body carries no scale, so its shape offsets are plain world-space distances rotated
        // into body space. InverseTransformPoint would also divide out the body's scale and pull the
        // shape toward the origin.
        Quaternion inverseBodyRotation = Quaternion.Inverse(rb.Transform.Rotation);
        Float3 rbLocalCenter = inverseBodyRotation * (worldCenter - rb.Transform.Position);
        Quaternion rbLocalRotation = inverseBodyRotation * worldRotation;

        return BuildShapes(rbLocalCenter, rbLocalRotation, cumulativeScale);
    }

    /// <summary>
    /// Create shapes transformed into world space for the static rigidbody.
    /// </summary>
    private RigidBodyShape[] CreateWorldTransformedShapes()
    {
        Float3 cumulativeScale = CumulativeScale();
        Float3 worldCenter = Transform.TransformPoint(center);
        Quaternion worldRotation = Transform.Rotation * Quaternion.FromEuler(rotation);

        return BuildShapes(worldCenter, worldRotation, cumulativeScale);
    }

    /// <summary>
    /// Places the collider's shapes at the given pose and scale, letting colliders that bake the
    /// transform into their geometry take precedence over the TransformedShape wrapper.
    /// </summary>
    private RigidBodyShape[] BuildShapes(Float3 translation, Quaternion rotation, Float3 scale)
    {
        RigidBodyShape[] baked = CreateBakedShapes(Float4x4.CreateTRS(translation, rotation, scale));
        if (baked != null)
            return baked;

        RigidBodyShape[] shapes = CreateShapes();
        if (shapes == null)
            return null;

        if (translation.Equals(Float3.Zero) && scale.Equals(Float3.One) && rotation == Quaternion.Identity)
            return shapes;

        Float4x4 linear = Float4x4.CreateTRS(Float3.Zero, rotation, scale);
        var jTranslation = new JVector(translation.X, translation.Y, translation.Z);
        var jLinear = new JMatrix(
            linear[0, 0], linear[0, 1], linear[0, 2],
            linear[1, 0], linear[1, 1], linear[1, 2],
            linear[2, 0], linear[2, 1], linear[2, 2]);

        var transformedShapes = new RigidBodyShape[shapes.Length];
        for (int i = 0; i < shapes.Length; i++)
            transformedShapes[i] = new TransformedShape(shapes[i], jTranslation, jLinear);

        return transformedShapes;
    }

    /// <summary>
    /// The nearest enabled Rigidbody3D at or above this collider, or null when the collider belongs to
    /// static geometry. A disabled rigidbody is skipped because its Jitter body has been removed, so
    /// attaching to it would drop the collider out of the world entirely.
    /// </summary>
    private Rigidbody3D FindOwningRigidbody()
    {
        foreach (Rigidbody3D rb in GetComponentsInParent<Rigidbody3D>())
            if (rb.IsValid() && rb.EnabledInHierarchy) return rb;

        return null;
    }

    /// <summary>
    /// Re-resolves which body this collider belongs to and attaches to it, falling back to the static
    /// rigidbody for its layer.
    /// </summary>
    internal void Reattach()
    {
        Detach();

        Rigidbody3D rb = FindOwningRigidbody();
        if (rb.IsValid()) TryAttachTo(rb);
        else AttachToStatic();

        _lastTransformVersion = CurrentTransformVersion();
        _lastBodyScale = _attachedRigidbody3D.IsValid() ? _attachedRigidbody3D.Transform.LocalScale : Float3.One;
        _lastLayer = GameObject.LayerIndex;
    }

    public override void OnEnable() => Reattach();

    public override void OnDisable()
    {
        // Detach from whatever rigidbody we're attached to
        Detach();
    }

    public override void Update()
    {
        if (_attachedBody == null) return;

        // Static shapes are in world space, so any movement invalidates them. Shapes on a rigidbody are
        // in body space, so only movement relative to that body does - which is why the version walk
        // stops there.
        bool transformChanged = CurrentTransformVersion() != _lastTransformVersion;

        // Writing LocalScale bumps the transform's version, so the walk above already covers every
        // rescale it passes over. It stops at the body, so the body's own rescale is the one case left,
        // and comparing that one value beats recomputing the whole chain's scale every frame.
        bool bodyScaleChanged = _attachedRigidbody3D.IsValid() &&
            !_attachedRigidbody3D.Transform.LocalScale.Equals(_lastBodyScale);

        bool layerChanged = _attachedRigidbody3D.IsNotValid() && GameObject.LayerIndex != _lastLayer;

        if (!transformChanged && !bodyScaleChanged && !layerChanged) return;

        OnAutoRebuild();
        Reattach();
    }

    /// <summary>
    /// Called just before a transform change triggers an automatic rebuild, so a collider that is
    /// expensive to rebuild can say so.
    /// </summary>
    protected virtual void OnAutoRebuild() { }

    public override void OnValidate() => Rebuild();
}
