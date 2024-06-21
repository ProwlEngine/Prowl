using Prowl.Icons;
using BepuPhysics.Constraints;
using BepuPhysics;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} BallSocketJoint")]
public class BallSocketJoint : Joint
{
    public Vector3 JointPosition;
    public float Frequency = 35;
    public float DampingRatio = 5;
    private SpringSettings springSettings = new SpringSettings();
    private BallSocket joint = new BallSocket();

    public override void Update()
    {
        base.Update();
        
        springSettings.Frequency = Frequency;
        springSettings.DampingRatio = DampingRatio;
    }

    protected override ConstraintHandle Build()
    {
        joint.LocalOffsetA = JointPosition;
        joint.LocalOffsetB = ConnectedBody.Transform.InverseTransformPoint(this.Transform.TransformPoint(JointPosition));
        joint.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<BallSocket>(Rigidbody.BodyHandle.Value, ConnectedBody!.BodyHandle!.Value, in joint);
    }

    public override void DrawGizmosSelected()
    {
        base.DrawGizmosSelected();
        Gizmos.DrawSphere(this.Transform.TransformPoint(JointPosition), 0.05f);
    }
}