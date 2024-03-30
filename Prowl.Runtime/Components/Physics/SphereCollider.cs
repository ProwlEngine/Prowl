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
            GetComponentInParent<Rigidbody>().IsActive = true;
        }

        public override void DrawGizmosSelected()
        {
            var mat = Matrix4x4.Identity;
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateScale(radius * 1.0025f));
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateScale(GameObject.transform.lossyScale));
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateTranslation(GameObject.transform.position - Camera.Current.GameObject.transform.position));
            Gizmos.Matrix = mat;
            Gizmos.Sphere(Color.yellow);
        }
    }

}