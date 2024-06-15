using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} CenterDistanceLimitJoint")]
public class CenterDistanceLimitJoint : Joint
{
    public float MinimumDistance = 0;
    public float MaximumDistance = 5;
    
    protected override ConstraintHandle Build(SpringSettings springSettings)
    {
        var distanceConstraint = new CenterDistanceLimit();
        distanceConstraint.MinimumDistance = MinimumDistance;
        distanceConstraint.MaximumDistance = MaximumDistance;
        distanceConstraint.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<CenterDistanceLimit>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, distanceConstraint);
    }
}