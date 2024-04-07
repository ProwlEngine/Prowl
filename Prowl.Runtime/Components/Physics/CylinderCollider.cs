using Jitter2.Collision.Shapes;
using Prowl.Icons;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Cylinder Collider")]
    public class CylinderCollider : Collider
    {
        public float radius = 1f;
        public float height = 2f;
        public override List<Shape> CreateShapes() => [ new CylinderShape(radius, height) ];
        public override void OnValidate()
        {
            (Shape[0] as CylinderShape).Radius = radius;
            (Shape[0] as CylinderShape).Height = height;
            Shape[0].UpdateShape();
            var rigid = GetComponentInParent<Rigidbody>();
            if (rigid != null)
                rigid.IsActive = true;
        }

        public override void DrawGizmosSelected()
        {
            Gizmos.Matrix = GameObject.transform.localToWorldMatrix;
            Gizmos.Color = Color.yellow;
            Gizmos.DrawCylinder(Vector3.zero, radius, height);
        }
    }

}