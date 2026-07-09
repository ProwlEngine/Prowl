// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision.Shapes;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Contains information about a shape cast hit.
/// </summary>
public struct ShapeCastHit
{
    /// <summary>
    /// If the shape cast hit something.
    /// </summary>
    public bool Hit;

    /// <summary>
    /// The fraction/lambda along the sweep direction where the hit occurred (0 = start, 1 = end of sweep).
    /// Note: This is not a distance, but a normalized value between 0 and 1.
    /// </summary>
    public float Fraction;

    /// <summary>
    /// The amount of penetration at the hit point. Only valid for overlap casts.
    /// </summary>
    public float Penetration;

    /// <summary>
    /// The normal of the surface the shape hit.
    /// </summary>
    public Float3 Normal;

    /// <summary>
    /// The point in world space on the casting shape where it hit the collider (at t=0).
    /// </summary>
    public Float3 Point;

    /// <summary>
    /// The point in world space on the hit collider where the cast shape touched it (at t=0).
    /// </summary>
    public Float3 HitPoint;

    /// <summary>
    /// The Rigidbody3D of the collider that was hit.
    /// </summary>
    public Rigidbody3D Rigidbody;

    /// <summary>
    /// The Shape that was hit.
    /// </summary>
    public RigidBodyShape Shape;

    /// <summary>
    /// The Transform of the rigidbody that was hit.
    /// </summary>
    public Transform Transform;

    /// <summary>
    /// Draws debug visualization gizmos for this shape cast hit.
    /// </summary>
    /// <param name="hitColor">Color to draw the hit visualization.</param>
    /// <param name="normalColor">Color to draw the hit normal.</param>
    /// <param name="normalLength">Length of the normal arrow.</param>
    public readonly void DrawGizmos(Color hitColor, Color normalColor, float normalLength = 1.0f)
    {
        if (!Hit) return;

        // Draw hit point as a small sphere
        Debug.DrawWireSphere(Point, 0.1f, hitColor, 8);
        Debug.DrawWireSphere(HitPoint, 0.1f, hitColor, 8);

        // Draw line between the two hit points
        Debug.DrawLine(Point, HitPoint, hitColor);

        // Draw normal arrow at hit point
        Debug.DrawArrow(HitPoint, Normal * normalLength, normalColor);
    }

    /// <summary>
    /// Draws debug visualization gizmos with default colors (yellow for hit, red for normal).
    /// </summary>
    public readonly void DrawGizmos()
    {
        DrawGizmos(new Color(255, 255, 0, 200), new Color(255, 0, 0, 255), 1.0f);
    }
}
