using Jitter2.Collision.Shapes;
using Prowl.Icons;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Box}  Box Collider")]
    public class BoxCollider : Collider
    {
        public Vector3 size = Vector3.one;

        public override List<Shape> CreateShapes() => [ new BoxShape(size * GameObject.transform.localScale) ];
        public override void OnValidate()
        {
            (Shape[0] as BoxShape).Size = size * GameObject.transform.localScale;
            Shape[0].UpdateShape();
            GetComponentInParent<Rigidbody>().IsActive = true;
        }

        public void DrawGizmosSelected()
        {
            Gizmos.Matrix = Matrix4x4.CreateScale(size * 1.0025f) * GameObject.GlobalCamRelative;
            Gizmos.Cube(Color.yellow);
        }
    }

}