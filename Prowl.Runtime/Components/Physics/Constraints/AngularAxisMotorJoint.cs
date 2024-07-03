using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} AngularAxisMotorJoint")]
public class AngularAxisMotorJoint : Joint
{
    public Vector3 LocalRotationAxis;
    public float TargetVelocity;
    private AngularAxisMotor angularAxisMotor = new AngularAxisMotor();
    
    public override void Update()
    {
        base.Update();
        
        angularAxisMotor.LocalAxisA = LocalRotationAxis;
        angularAxisMotor.TargetVelocity = TargetVelocity;
    }

    protected override ConstraintHandle Build()
    {
        angularAxisMotor.LocalAxisA = LocalRotationAxis;
        angularAxisMotor.TargetVelocity = TargetVelocity;
        
        return Physics.Sim!.Solver.Add<AngularAxisMotor>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, in angularAxisMotor);
    }

    public override void DrawGizmosSelected()
    {
        base.DrawGizmosSelected();
        //Gizmos.DrawLine(this.Transform.position, this.Transform.position + LocalRotationAxis.normalized);
    }
}