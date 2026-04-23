// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.ComponentModel;

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
        _lastTransformVersion = Transform.Version;
        _lastLayer = layer;
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
            try
            {
                foreach (var shape in _attachedShapes)
                {
                    _attachedBody.RemoveShape(shape, false);
                }
            }
            catch (System.InvalidOperationException)
            {
                // Shape was already removed (e.g., when rigidbody was removed from world)
                // This is fine, just continue
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
            foreach (var shape in _attachedShapes)
            {
                _attachedBody.AddShape(shape, false);
            }
        }

        if (_attachedRigidbody3D != null)
        {
            // Set mas to itself to force inertia tensor recalculation
            try
            {
                _attachedRigidbody3D.Mass = _attachedRigidbody3D.Mass;
            }
            catch (InvalidOperationException)
            {
                // This can occur if the Shapes provided are 2D and mass cannot be calculated from them
                _attachedBody.SetMassInertia(JMatrix.Identity, 1f);
            }
        }
        else
        {
            // Static bodies dont really need mass or inertia
        }
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
            bool transformChanged = Transform.Version != _lastTransformVersion;
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
