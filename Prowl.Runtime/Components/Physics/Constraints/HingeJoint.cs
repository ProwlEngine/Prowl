using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} HingeJoint")]
public class HingeJoint : Joint
{
    public Vector3 JointPosition;
    public Vector3 HingeAxis = Vector3.forward;

    protected override ConstraintHandle Build(SpringSettings springSettings)
    {
        var hinge = new Hinge();
        hinge.LocalOffsetA = JointPosition;
        hinge.LocalOffsetB = ConnectedBody!.Transform.InverseTransformPoint(this.Transform.TransformPoint(JointPosition));
        hinge.LocalHingeAxisA = HingeAxis;
        hinge.LocalHingeAxisB = ConnectedBody!.Transform.InverseTransformVector(this.Transform.TransformVector(HingeAxis));;
        hinge.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<Hinge>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, hinge);
    }

    public override void DrawGizmosSelected()
    {
        if (ConnectedBody != null)
        {
            Gizmos.DrawSphere(this.Transform.TransformPoint(JointPosition), 0.05f);
            Gizmos.DrawLine(this.Transform.TransformPoint(JointPosition), this.Transform.TransformPoint(JointPosition + HingeAxis));
        }
    }
}