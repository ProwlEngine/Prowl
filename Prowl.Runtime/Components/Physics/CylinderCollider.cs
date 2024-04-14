using BepuPhysics.Collidables;
using Prowl.Icons;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Cylinder Collider")]
    public class CylinderCollider : Collider
    {
        public float radius = 0.5f;
        public float height = 1f;

        public override void CreateShape()
        {
            var cylinder = new Cylinder(radius, height * 2f);
            shape = cylinder;
            bodyInertia = cylinder.ComputeInertia(mass);
            shapeIndex = Physics.Sim.Shapes.Add(cylinder);
        }

        public override void DrawGizmosSelected()
        {
            Gizmos.Matrix = GameObject.Transform.localToWorldMatrix;
            Gizmos.Color = Color.yellow;
            Gizmos.DrawCylinder(Vector3.zero, radius + 0.01f, height * 2f + 0.01f);
        }
    }

}