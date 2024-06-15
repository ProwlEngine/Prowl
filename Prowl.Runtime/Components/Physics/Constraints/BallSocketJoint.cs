using Prowl.Icons;
using BepuPhysics.Constraints;
using BepuPhysics;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} BallSocketJoint")]
public class BallSocketJoint : Joint
{
    public Vector3 JointPosition;
    
    protected override ConstraintHandle Build(SpringSettings springSettings)
    {
        var joint = new BallSocket();
        joint.LocalOffsetA = JointPosition;
        joint.LocalOffsetB = ConnectedBody.Transform.InverseTransformPoint(this.Transform.TransformPoint(JointPosition));
        joint.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<BallSocket>(Rigidbody.BodyHandle.Value, ConnectedBody!.BodyHandle!.Value, joint);
    }

    public override void DrawGizmosSelected()
    {
        base.DrawGizmosSelected();
        Gizmos.DrawSphere(this.Transform.TransformPoint(JointPosition), 0.05f);
    }
}