using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} DistanceServoJoint")]
public class DistanceServoJoint : Joint
{
    public float BaseSpeed = 5f;
    public float MaximumForce = 20f;
    public float MaximumSpeed = 10f;
    public float TargetDistance = 5;
    public Vector3 JointOffsetPosition;
    public Vector3 ConnectedBodyJointOffsetPosition;
    ServoSettings servoSettings = new ServoSettings();

    public float Frequency = 35;
    public float DampingRatio = 5;
    private SpringSettings springSettings = new SpringSettings();
    private DistanceServo distanceConstraint = new DistanceServo();
    
    public override void Update()
    {
        base.Update();
        
        springSettings.Frequency = Frequency;
        springSettings.DampingRatio = DampingRatio;
        
        servoSettings.BaseSpeed = BaseSpeed;
        servoSettings.MaximumForce = MaximumForce;
        servoSettings.MaximumSpeed = MaximumSpeed;
    }

    protected override ConstraintHandle Build()
    {
        servoSettings.BaseSpeed = BaseSpeed;
        servoSettings.MaximumForce = MaximumForce;
        servoSettings.MaximumSpeed = MaximumSpeed;
        
        distanceConstraint.TargetDistance = TargetDistance;
        distanceConstraint.LocalOffsetA = JointOffsetPosition;
        distanceConstraint.LocalOffsetB = ConnectedBodyJointOffsetPosition;
        distanceConstraint.SpringSettings = springSettings;
        distanceConstraint.ServoSettings = servoSettings;
        
        return Physics.Sim!.Solver.Add<DistanceServo>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, in distanceConstraint);
    }
    
    public override void DrawGizmosSelected()
    {
        if (this.Rigidbody != null && this.ConnectedBody != null)
        {
            Gizmos.DrawLine(Rigidbody.Transform.TransformPoint(JointOffsetPosition), ConnectedBody.Transform.TransformPoint(ConnectedBodyJointOffsetPosition));
        }
        if (this.Rigidbody != null)
        {
            Gizmos.DrawSphere(Rigidbody.Transform.TransformPoint(JointOffsetPosition), 0.05f);
        }
        if (this.ConnectedBody != null)
        {
            Gizmos.DrawSphere(ConnectedBody.Transform.TransformPoint(ConnectedBodyJointOffsetPosition), 0.05f);
        }
    }
}