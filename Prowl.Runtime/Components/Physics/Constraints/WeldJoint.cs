using System.Numerics;
using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} WeldJoint")]
public class WeldJoint : Joint
{
    public float Frequency = 35;
    public float DampingRatio = 5;
    private SpringSettings springSettings = new SpringSettings();
    private Weld weld = new Weld();
    
    public override void Update()
    {
        base.Update();
        
        springSettings.Frequency = Frequency;
        springSettings.DampingRatio = DampingRatio;
    }
    
    protected override ConstraintHandle Build()
    {
        weld.LocalOffset = this.Transform.InverseTransformPoint(ConnectedBody!.Transform.position);
        weld.LocalOrientation = ConnectedBody.Transform.InverseTransformRotation(this.Transform.TransformRotation(ConnectedBody.Transform.localRotation));
        weld.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<Weld>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, in weld);
    }
}