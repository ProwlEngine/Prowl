using Jitter2.Collision.Shapes;
using Prowl.Icons;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Circle}  Sphere Collider")]
    public class SphereCollider : Collider
    {
        public float radius = 1f;
        public override List<Shape> CreateShapes() => [ new SphereShape(radius * (float)GameObject.Scale.x) ];
        public override void OnValidate()
        {
            (Shape[0] as SphereShape).Radius = radius * (float)GameObject.Scale.x;
            Shape[0].UpdateShape();
            GetComponentInParent<Rigidbody>().IsActive = true;
        }

        public void DrawGizmosSelected()
        {
            var mat = Matrix4x4.Identity;
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateScale((radius * (float)GameObject.Scale.x) * 1.0025f));
            mat = Matrix4x4.Multiply(mat, Matrix4x4.CreateTranslation(GameObject.GlobalPosition - Camera.Current.GameObject.GlobalPosition));
            Gizmos.Matrix = mat;
            Gizmos.Sphere(Color.yellow);
        }
    }

}