// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision.Shapes;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

[AddComponentMenu("Physics/Colliders/Box Collider")]
public sealed class BoxCollider : Collider
{
    [SerializeField] private Float3 size = new(1, 1, 1);

    /// <summary>
    /// Gets or sets the dimensions of the box.
    /// </summary>
    public Float3 Size
    {
        get => size;
        set
        {
            size = value;
            OnValidate();
        }
    }

    public override RigidBodyShape[] CreateShapes() => [new BoxShape(Maths.Max(size.X, 0.01f), Maths.Max(size.Y, 0.01f), Maths.Max(size.Z, 0.01f))];

    public override void DrawGizmos()
    {
        Float4x4 matrix = Float4x4.CreateTRS(Transform.Position, Transform.Rotation * Quaternion.FromEuler(Rotation), Transform.LossyScale);
        Debug.PushMatrix(matrix);
        Debug.DrawWireCube(Center, size * 0.5f, Color.Green);
        Debug.PopMatrix();
    }
}
