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
        if (GameObject?.Scene?.Physics == null)
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
                    // iterates remaining shapes — this throws NotSupportedException for TriangleShape.
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
            foreach (RigidBodyShape shape in _attachedShapes)
            {
                // Always use Preserve: per-shape Update mode calls SetMassInertia() after every
                // addition, which iterates ALL attached shapes. Any TriangleShape in that set
                // throws NotSupportedException. We set mass once below, after all shapes are added.
                _attachedBody.AddShape(shape, Jitter2.Dynamics.MassInertiaUpdateMode.Preserve);
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
    /// Create the Transformed Jitter Physics RigidBodyShape
    /// </summary>
    public RigidBodyShape[] CreateTransformedShapes()
    {
        // Create the base shape
        RigidBodyShape[] shapes = CreateShapes();
        if (shapes == null)
            return null;
        Rigidbody3D rb = RigidBody;
        if (rb.IsNotValid()) return shapes;

        // Get the cumulative scale from this object up to (but not including) the rigidbody
        Float3 cumulativeScale = Float3.One;
        Transform current = Transform;
        Transform rbTransform = rb.Transform;

        while (current != null)
        {
            cumulativeScale *= current.LocalScale;
            current = current.Parent;
        }

        cumulativeScale = Maths.Max(cumulativeScale, Float3.One * 0.05f);

        // Get the local rotation and position in world space
        Quaternion localRotation = Quaternion.FromEuler(Rotation);
        Float3 scaledCenter = Center * cumulativeScale;

        // Transform local position and rotation to world space
        Float3 worldCenter = Transform.TransformPoint(scaledCenter);
        Quaternion worldRotation = Transform.Rotation * localRotation;

        // Transform from world space to rigid body's local space
        Float3 rbLocalCenter = rb.Transform.InverseTransformPoint(worldCenter);
        Quaternion rbLocalRotation = Quaternion.Inverse(rb.Transform.Rotation) * worldRotation;

        // Create a scale transform matrix that includes both rotation and scale
        Float4x4 scaleMatrix = Float4x4.CreateTRS(Float3.Zero, rbLocalRotation, cumulativeScale);

        // If there's no transformation needed, return the original shape
        if (rbLocalCenter.Equals(Float3.Zero) &&
            cumulativeScale.Equals(Float3.One) &&
            rbLocalRotation == Quaternion.Identity)
            return shapes;

        // Convert to Jitter types
        var translation = new Jitter2.LinearMath.JVector(
            rbLocalCenter.X,
            rbLocalCenter.Y,
            rbLocalCenter.Z
        );

        // Convert combined rotation and scale matrix to JMatrix
        var orientation = new Jitter2.LinearMath.JMatrix(
            scaleMatrix[0, 0], scaleMatrix[0, 1], scaleMatrix[0, 2],
            scaleMatrix[1, 0], scaleMatrix[1, 1], scaleMatrix[1, 2],
            scaleMatrix[2, 0], scaleMatrix[2, 1], scaleMatrix[2, 2]
        );

        //return new TransformedShape(shape, translation, orientation);
        TransformedShape[] transformedShapes = new TransformedShape[shapes.Length];
        for (int i = 0; i < shapes.Length; i++)
            transformedShapes[i] = new TransformedShape(shapes[i], translation, orientation);

        return transformedShapes;
    }

    /// <summary>
    /// Create shapes transformed into world space for the static rigidbody.
    /// </summary>
    private RigidBodyShape[] CreateWorldTransformedShapes()
    {
        // Create the base shape
        RigidBodyShape[] shapes = CreateShapes();
        if (shapes == null)
            return null;

        // Get the cumulative scale
        Float3 cumulativeScale = Float3.One;
        Transform current = Transform;

        while (current != null)
        {
            cumulativeScale *= current.LocalScale;
            current = current.Parent;
        }

        cumulativeScale = Maths.Max(cumulativeScale, Float3.One * 0.05f);

        // Get the local rotation and position
        Quaternion localRotation = Quaternion.FromEuler(Rotation);
        Float3 scaledCenter = Center * cumulativeScale;

        // Transform to world space
        Float3 worldCenter = Transform.TransformPoint(scaledCenter);
        Quaternion worldRotation = Transform.Rotation * localRotation;

        // Create a scale transform matrix that includes both rotation and scale
        Float4x4 scaleMatrix = Float4x4.CreateTRS(Float3.Zero, worldRotation, cumulativeScale);

        // Convert to Jitter types
        var translation = new Jitter2.LinearMath.JVector(
            worldCenter.X,
            worldCenter.Y,
            worldCenter.Z
        );

        // Convert combined rotation and scale matrix to JMatrix
        var orientation = new Jitter2.LinearMath.JMatrix(
            scaleMatrix[0, 0], scaleMatrix[0, 1], scaleMatrix[0, 2],
            scaleMatrix[1, 0], scaleMatrix[1, 1], scaleMatrix[1, 2],
            scaleMatrix[2, 0], scaleMatrix[2, 1], scaleMatrix[2, 2]
        );

        // If there's no transformation needed, return the original shape
        if (worldCenter.Equals(Float3.Zero) &&
            cumulativeScale.Equals(Float3.One) &&
            worldRotation == Quaternion.Identity)
            return shapes;

        TransformedShape[] transformedShapes = new TransformedShape[shapes.Length];
        for (int i = 0; i < shapes.Length; i++)
            transformedShapes[i] = new TransformedShape(shapes[i], translation, orientation);

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
