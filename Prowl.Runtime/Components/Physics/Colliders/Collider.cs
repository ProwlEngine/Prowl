// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Vector;

namespace Prowl.Runtime;

[ComponentIcon("\uf1b2")] // Cube subclasses override with their specific shape
public abstract class Collider : MonoBehaviour
{
    public Float3 Center;
    public Float3 Rotation;

    protected Float4x4 GizmoMatrix =>
        Float4x4.CreateTRS(
            Float4x4.TransformPoint(Center, Float4x4.CreateTRS(Transform.Position, Transform.Rotation, Transform.LossyScale)),
            Transform.Rotation * Quaternion.FromEuler(Rotation),
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

    protected Rigidbody3D RigidBody => GetComponentInParent<Rigidbody3D>();

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
        _lastTransformVersion = ComputeWorldTransformVersion();
        _lastLayer = layer;
    }

    /// <summary>
    /// A version that changes whenever this transform OR any ancestor changes, so static colliders
    /// follow their parents. Transform.Version alone only tracks local edits, so moving a parent would
    /// not re-register a child collider's world-space shapes.
    /// </summary>
    private uint ComputeWorldTransformVersion()
    {
        uint v = 17;
        Transform t = Transform;
        while (t != null)
        {
            v = v * 31 + t.Version;
            t = t.Parent;
        }
        return v;
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

        if (_attachedRigidbody3D != null)
        {
            // SetMassInertia(mass) sums inertia from all shapes, then scales to the requested mass.
            // TriangleShape has no volume so it throws NotSupportedException; fall back to treating the
            // body as a solid box sized to the shapes' combined bounds so it still rotates plausibly.
            try
            {
                _attachedBody.SetMassInertia(_attachedRigidbody3D.Mass);
            }
            catch (NotSupportedException)
            {
                JMatrix inertia = ApproximateBoxInertia(_attachedShapes, _attachedRigidbody3D.Mass);
                _attachedBody.SetMassInertia(inertia, _attachedRigidbody3D.Mass);
            }
        }
        // Static bodies don't need mass or inertia.
    }

    /// <summary>
    /// Builds an inertia tensor for a body whose shapes have no usable volume (e.g. concave
    /// TriangleShapes). The shapes are approximated as a single solid box sized to their combined
    /// local-space bounds, using the same formula as <c>BoxShape</c>. The tensor is taken about the box
    /// centre rather than the body's centre of mass, which is a close enough approximation for the
    /// fallback case (the alternative is a meaningless identity tensor).
    /// </summary>
    private static JMatrix ApproximateBoxInertia(RigidBodyShape[] shapes, float mass)
    {
        if (shapes == null || shapes.Length == 0)
            return JMatrix.Identity;

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

        return Maths.Max(scale, Float3.One * 0.05f);
    }

    /// <summary>
    /// Create the Jitter Physics RigidBodyShapes in the space of the attached Rigidbody3D.
    /// </summary>
    public RigidBodyShape[] CreateTransformedShapes()
    {
        Rigidbody3D rb = RigidBody;
        if (rb.IsNotValid()) return CreateShapes();

        Float3 cumulativeScale = CumulativeScale();
        Float3 worldCenter = Transform.TransformPoint(Center * cumulativeScale);
        Quaternion worldRotation = Transform.Rotation * Quaternion.FromEuler(Rotation);

        // Transform from world space into the rigid body's local space
        Float3 rbLocalCenter = rb.Transform.InverseTransformPoint(worldCenter);
        Quaternion rbLocalRotation = Quaternion.Inverse(rb.Transform.Rotation) * worldRotation;

        return BuildShapes(rbLocalCenter, rbLocalRotation, cumulativeScale);
    }

    /// <summary>
    /// Create shapes transformed into world space for the static rigidbody.
    /// </summary>
    private RigidBodyShape[] CreateWorldTransformedShapes()
    {
        Float3 cumulativeScale = CumulativeScale();
        Float3 worldCenter = Transform.TransformPoint(Center * cumulativeScale);
        Quaternion worldRotation = Transform.Rotation * Quaternion.FromEuler(Rotation);

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

    public override void OnEnable()
    {
        // First check if there's a Rigidbody3D on this GameObject or any parent
        Rigidbody3D rb = GetComponentInParent<Rigidbody3D>();

        if (rb.IsValid())
        {
            // Attach to the Rigidbody3D
            TryAttachTo(rb);
        }
        else
        {
            // No Rigidbody3D found, attach to the static rigidbody
            AttachToStatic();
        }
    }

    public override void OnDisable()
    {
        // Detach from whatever rigidbody we're attached to
        Detach();
    }

    public override void Update()
    {
        // Only track transform and layer changes if we're attached to the static rigidbody
        if (_attachedRigidbody3D == null && _attachedBody != null)
        {
            bool transformChanged = ComputeWorldTransformVersion() != _lastTransformVersion;
            bool layerChanged = GameObject.LayerIndex != _lastLayer;

            // Check if the transform or layer has changed
            if (transformChanged || layerChanged)
            {
                // Transform has moved or layer changed, update the shapes
                Detach();
                AttachToStatic();
            }
        }
    }

    public override void OnValidate()
    {
        // If we're attached to a Rigidbody3D, refresh it
        if (_attachedRigidbody3D != null)
        {
            _attachedRigidbody3D.OnValidate();
        }
        else if (_attachedBody != null)
        {
            // We're attached to the static rigidbody, just re-register
            Detach();
            AttachToStatic();
        }
    }
}
