using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} CenterDIstanceJoint")]
public class CenterDistanceJoint : Joint
{
    public float TargetDistance;
    
    protected override ConstraintHandle Build(SpringSettings springSettings)
    {
        var distanceConstraint = new CenterDistanceConstraint();
        distanceConstraint.TargetDistance = TargetDistance;
        distanceConstraint.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<CenterDistanceConstraint>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, distanceConstraint);
    }
    
    public override void DrawGizmosSelected()
    {
        Gizmos.DrawLine(Rigidbody.Transform.position, ConnectedBody.Transform.position);
    }
}