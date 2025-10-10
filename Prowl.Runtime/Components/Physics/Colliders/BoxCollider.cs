// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision.Shapes;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

public sealed class BoxCollider : Collider
{
    [SerializeField] private Double3 size = new(1, 1, 1);

    /// <summary>
    /// Gets or sets the dimensions of the box.
    /// </summary>
    public Double3 Size
    {
        get => size;
        set
        {
            size = value;
            OnValidate();
        }
    }

    public override RigidBodyShape[] CreateShapes() => [new BoxShape(Maths.Max(size.X, 0.01), Maths.Max(size.Y, 0.01), Maths.Max(size.Z, 0.01))];
}
