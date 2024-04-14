using BepuPhysics.Collidables;
using Prowl.Icons;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Box}  Box Collider")]
    public class BoxCollider : Collider
    {
        public Vector3 size = Vector3.one;

        public override void CreateShape()
        {
            Vector3 s = size * this.GameObject.Transform.localScale;
            var box = new Box((float)s.x, (float)s.y, (float)s.z);
            shape = box;
            bodyInertia = box.ComputeInertia(mass);
            shapeIndex = Physics.Sim.Shapes.Add(box);
        }

        public override void DrawGizmosSelected()
        {
            Gizmos.Matrix = GameObject.Transform.localToWorldMatrix;
            Gizmos.Color = Color.yellow;
            Gizmos.DrawCube(Vector3.zero, size * 1.001f);
        }
    }

}