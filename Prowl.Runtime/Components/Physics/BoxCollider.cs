using Jitter2.Collision.Shapes;
using Prowl.Icons;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Box}  Box Collider")]
    public class BoxCollider : Collider
    {
        public Vector3 size = Vector3.one;

        public override List<Shape> CreateShapes() => [ new BoxShape(size) ];
        public override void OnValidate()
        {
            (Shape[0] as BoxShape).Size = size;
            Shape[0].UpdateShape();
            var rigid = GetComponentInParent<Rigidbody>();
            if(rigid != null)
                rigid.IsActive = true;
        }

        public override void DrawGizmosSelected()
        {
            var mat = Matrix4x4.Identity;
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateScale(size * 1.0025f));
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateScale(GameObject.transform.lossyScale));
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateTranslation(GameObject.transform.position - Camera.Current.GameObject.transform.position));
            Gizmos.Matrix = mat;
            Gizmos.Cube(Color.yellow);
        }
    }

}