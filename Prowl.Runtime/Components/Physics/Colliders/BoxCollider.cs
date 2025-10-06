// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision.Shapes;

using Prowl.Echo;

namespace Prowl.Runtime;

public sealed class BoxCollider : Collider
{
    [SerializeField] private Vector3 size = new(1, 1, 1);

    /// <summary>
    /// Gets or sets the dimensions of the box.
    /// </summary>
    public Vector3 Size
    {
        get => size;
        set
        {
            size = value;
            OnValidate();
        }
    }

    public override RigidBodyShape[] CreateShapes() => [new BoxShape(MathD.Max(size.x, 0.01), MathD.Max(size.y, 0.01), MathD.Max(size.z, 0.01))];
}
