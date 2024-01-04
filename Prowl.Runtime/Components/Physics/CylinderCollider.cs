using Jitter2.Collision.Shapes;
using Prowl.Icons;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Cylinder Collider")]
    public class CylinderCollider : Collider
    {
        public float radius = 1f;
        public float height = 1f;
        public override List<Shape> CreateShapes() => [ new CylinderShape(radius, height) ];
        public override void OnValidate()
        {
            (Shape[0] as CapsuleShape).Radius = radius;
            (Shape[0] as CapsuleShape).Length = height;
            Shape[0].UpdateShape();
            GetComponentInParent<Rigidbody>().IsActive = true;
        }
    }

}