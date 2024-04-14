using BepuPhysics.Collidables;
using Prowl.Icons;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Circle}  Sphere Collider")]
    public class SphereCollider : Collider
    {
        public float radius = 0.5f;

        public override void CreateShape()
        {
            var sphere = new Sphere(radius);
            shape = sphere;
            bodyInertia = sphere.ComputeInertia(mass);
            shapeIndex = Physics.Sim.Shapes.Add(sphere);
        }

        public override void DrawGizmosSelected()
        {
            Gizmos.Matrix = GameObject.Transform.localToWorldMatrix;
            Gizmos.Color = Color.yellow;
            Gizmos.DrawSphere(Vector3.zero, radius * 1.05f);
        }
    }

}