// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision.Shapes;

using Prowl.Icons;
using Prowl.Echo;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Box}  Sphere Collider")]
public sealed class SphereCollider : Collider
{
    [SerializeField] private float radius = 0.5f;

    public float Radius
    {
        get => radius;
        set
        {
            radius = value;
            OnValidate();
        }
    }

    public override RigidBodyShape CreateShape() => new SphereShape(radius);
}
