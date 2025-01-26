// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision.Shapes;

namespace Prowl.Runtime;

public abstract class Collider : MonoBehaviour
{
    public Vector3 center;
    public Vector3 rotation;

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
        Vector3 cumulativeScale = Vector3.one;
        Transform current = this.Transform;
        Transform rbTransform = rb.Transform;

        while (current != null)
        {
            cumulativeScale = Vector3.Scale(cumulativeScale, current.localScale);
            current = current.parent;
        }

        cumulativeScale = Vector3.Max(cumulativeScale, Vector3.one * 0.05);

        // Get the local rotation and position in world space
        Quaternion localRotation = Quaternion.Euler(rotation);
        Vector3 scaledCenter = Vector3.Scale(this.center, cumulativeScale);

        // Transform local position and rotation to world space
        Vector3 worldCenter = this.Transform.TransformPoint(scaledCenter);
        Quaternion worldRotation = this.Transform.rotation * localRotation;

        // Transform from world space to rigid body's local space
        Vector3 rbLocalCenter = rb.Transform.InverseTransformPoint(worldCenter);
        Quaternion rbLocalRotation = Quaternion.Inverse(rb.Transform.rotation) * worldRotation;

        // Create a scale transform matrix that includes both rotation and scale
        Matrix4x4 scaleMatrix = Matrix4x4.TRS(Vector3.zero, rbLocalRotation, cumulativeScale);

        // If there's no transformation needed, return the original shape
        if (rbLocalCenter == Vector3.zero &&
            cumulativeScale == Vector3.one &&
            rbLocalRotation == Quaternion.identity)
            return shapes;

        // Convert to Jitter types
        var translation = new Jitter2.LinearMath.JVector(
            rbLocalCenter.x,
            rbLocalCenter.y,
            rbLocalCenter.z
        );

        // Convert combined rotation and scale matrix to JMatrix
        var orientation = new Jitter2.LinearMath.JMatrix(
            scaleMatrix.M11, scaleMatrix.M12, scaleMatrix.M13,
            scaleMatrix.M21, scaleMatrix.M22, scaleMatrix.M23,
            scaleMatrix.M31, scaleMatrix.M32, scaleMatrix.M33
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
