using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} HingeJoint")]
public class HingeJoint : Joint
{
    public Vector3 JointPosition;
    public Vector3 HingeAxis = Vector3.forward;

    public float Frequency = 35;
    public float DampingRatio = 5;
    private SpringSettings springSettings = new SpringSettings();
    private Hinge hinge = new Hinge();
    
    public override void Update()
    {
        base.Update();
        
        springSettings.Frequency = Frequency;
        springSettings.DampingRatio = DampingRatio;
    }
    
    protected override ConstraintHandle Build()
    {
        hinge.LocalOffsetA = JointPosition;
        hinge.LocalOffsetB = ConnectedBody!.Transform.InverseTransformPoint(this.Transform.TransformPoint(JointPosition));
        hinge.LocalHingeAxisA = HingeAxis;
        hinge.LocalHingeAxisB = ConnectedBody!.Transform.InverseTransformVector(this.Transform.TransformVector(HingeAxis));;
        hinge.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<Hinge>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, in hinge);
    }

    public override void DrawGizmosSelected()
    {
        base.DrawGizmosSelected();
        //Gizmos.DrawSphere(this.Transform.TransformPoint(JointPosition), 0.05f);
        //Gizmos.DrawLine(this.Transform.TransformPoint(JointPosition), this.Transform.TransformPoint(JointPosition + HingeAxis));
    }
}