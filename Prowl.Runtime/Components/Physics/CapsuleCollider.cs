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
        public float height = 1f;
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
            var mat = Matrix4x4.Identity;
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateScale(new Vector3(radius, height, radius) * 1.0025f));
            mat = Matrix4x4.Multiply(mat, GameObject.GlobalCamRelative);
            Gizmos.Matrix = mat;
            Gizmos.Capsule(Color.yellow);
        }
    }

}