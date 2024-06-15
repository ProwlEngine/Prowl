using System.Numerics;
using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} WeldJoint")]
public class WeldJoint : Joint
{
    protected override ConstraintHandle Build(SpringSettings springSettings)
    {
        var weld = new Weld();
        weld.LocalOffset = this.Transform.InverseTransformPoint(ConnectedBody!.Transform.position);
        weld.LocalOrientation = ConnectedBody.Transform.InverseTransformRotation(this.Transform.TransformRotation(ConnectedBody.Transform.localRotation));
        weld.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<Weld>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, weld);
    }
}