using Jitter2.Collision.Shapes;
using Prowl.Icons;
using System.Collections.Generic;
using System.Drawing;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Capsule Collider")]
    public class CapsuleCollider : Collider
    {
        public float radius = 1f;
        public float height = 2f;
        public override List<Shape> CreateShapes() => [ new CapsuleShape(radius, height) ];
        public override void OnValidate()
        {
            (Shape[0] as CapsuleShape).Radius = radius;
            (Shape[0] as CapsuleShape).Length = height;
            Shape[0].UpdateShape();
            var rigid = GetComponentInParent<Rigidbody>();
            if (rigid != null)
                rigid.IsActive = true;
        }

        public override void DrawGizmosSelected()
        {
            Gizmos.Matrix = GameObject.transform.localToWorldMatrix;
            Gizmos.Color = Color.yellow;
            Gizmos.DrawCapsule(Vector3.zero, radius, height);
        }
    }

}