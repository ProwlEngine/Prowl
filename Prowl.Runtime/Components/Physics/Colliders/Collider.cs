// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision.Shapes;

using Prowl.Vector;

namespace Prowl.Runtime;

public abstract class Collider : MonoBehaviour
{
    public Double3 center;
    public Double3 rotation;

    protected Rigidbody3D RigidBody => GetComponentInParent<Rigidbody3D>();


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
        var rb = RigidBody;
        if (rb == null) return shapes;

        // Get the cumulative scale from this object up to (but not including) the rigidbody
        Double3 cumulativeScale = Double3.One;
        Transform current = this.Transform;
        Transform rbTransform = rb.Transform;

        while (current != null)
        {
            cumulativeScale = Maths.Mul(cumulativeScale, current.localScale);
            current = current.parent;
        }

        cumulativeScale = Maths.Max(cumulativeScale, Double3.One * 0.05);

        // Get the local rotation and position in world space
        Quaternion localRotation = Quaternion.FromEuler((Float3)rotation);
        Double3 scaledCenter = Maths.Mul(this.center, cumulativeScale);

        // Transform local position and rotation to world space
        Double3 worldCenter = this.Transform.TransformPoint(scaledCenter);
        Quaternion worldRotation = this.Transform.rotation * localRotation;

        // Transform from world space to rigid body's local space
        Double3 rbLocalCenter = rb.Transform.InverseTransformPoint(worldCenter);
        Quaternion rbLocalRotation = Maths.Inverse(rb.Transform.rotation) * worldRotation;

        // Create a scale transform matrix that includes both rotation and scale
        Double4x4 scaleMatrix = Double4x4.CreateTRS(Double3.Zero, rbLocalRotation, cumulativeScale);

        // If there's no transformation needed, return the original shape
        if (rbLocalCenter.Equals(Double3.Zero) &&
            cumulativeScale.Equals(Double3.One) &&
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

    public override void OnEnable()
    {
        Rigidbody3D rb = RigidBody;
        if (rb != null)
        {
            // Refresh the Rigidbody, this will regenerate the body's shape and include this collider
            rb.OnValidate();
        }
    }

    public override void OnDisable()
    {
        Rigidbody3D rb = RigidBody;
        if (rb != null)
        {
            // Refresh the Rigidbody, this will regenerate the body's shape and remove this collider
            rb.OnValidate();
        }
    }

    public override void OnValidate()
    {
        Rigidbody3D rb = RigidBody;
        if (rb != null)
        {
            // Refresh the Rigidbody, this will regenerate the body's shape and include the changes made to this collider
            rb.OnValidate();
        }
    }
}
