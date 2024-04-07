using Jitter2.Collision.Shapes;
using Prowl.Icons;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Circle}  Sphere Collider")]
    public class SphereCollider : Collider
    {
        public float radius = 1f;
        public override List<Shape> CreateShapes() => [ new SphereShape(radius) ];
        public override void OnValidate()
        {
            (Shape[0] as SphereShape).Radius = radius;
            Shape[0].UpdateShape();
            var rigid = GetComponentInParent<Rigidbody>();
            if (rigid != null)
                rigid.IsActive = true;
        }

        public override void DrawGizmosSelected()
        {
            Gizmos.Matrix = GameObject.transform.localToWorldMatrix;
            Gizmos.Color = Color.yellow;
            Gizmos.DrawSphere(Vector3.zero, radius * 1.0025f);
        }
    }

}