using BepuPhysics.Collidables;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Capsule Collider")]
public class CapsuleCollider : Collider
{
    public float radius = 0.5f;
    public float height = 1f;

    public override void CreateShape()
    {
        var s = this.GameObject.Transform.lossyScale;
        float r = radius * (float)s.x;
        var capsule = new Capsule(r, height * (float)s.y);
        shape = capsule;
        bodyInertia = capsule.ComputeInertia(mass);
        shapeIndex = Physics.Sim.Shapes.Add(capsule);
    }

    public override void DrawGizmosSelected()
    {
        Gizmos.Matrix = GameObject.Transform.localToWorldMatrix;
        Gizmos.Color = Color.yellow;
        Gizmos.DrawCapsule(Vector3.zero, radius + 0.01f, height + 0.01f);
    }
}