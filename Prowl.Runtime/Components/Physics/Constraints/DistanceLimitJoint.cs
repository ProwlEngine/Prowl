using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} DistanceLimitJoint")]
public class DistanceLimitJoint : Joint
{
    public float MinimumDistance = 0;
    public float MaximumDistance = 5;
    public Vector3 JointOffsetPosition;
    public Vector3 ConnectedBodyJointOffsetPosition;
    
    public float Frequency = 35;
    public float DampingRatio = 5;
    private SpringSettings springSettings = new SpringSettings();
    private DistanceLimit distanceConstraint = new DistanceLimit();
    
    public override void Update()
    {
        base.Update();
        
        springSettings.Frequency = Frequency;
        springSettings.DampingRatio = DampingRatio;
    }
    
    protected override ConstraintHandle Build()
    {
        distanceConstraint.MinimumDistance = MinimumDistance;
        distanceConstraint.MaximumDistance = MaximumDistance;
        distanceConstraint.LocalOffsetA = JointOffsetPosition;
        distanceConstraint.LocalOffsetB = ConnectedBodyJointOffsetPosition;
        distanceConstraint.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<DistanceLimit>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, in distanceConstraint);
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