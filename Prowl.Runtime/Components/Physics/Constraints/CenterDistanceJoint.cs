using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} CenterDistanceJoint")]
public class CenterDistanceJoint : Joint
{
    public float TargetDistance = 1;
    
    public float Frequency = 35;
    public float DampingRatio = 5;
    private SpringSettings springSettings = new SpringSettings();
    private CenterDistanceConstraint distanceConstraint = new CenterDistanceConstraint();
    
    public override void Update()
    {
        base.Update();
        
        springSettings.Frequency = Frequency;
        springSettings.DampingRatio = DampingRatio;
    }
    
    protected override ConstraintHandle Build()
    {
        distanceConstraint.TargetDistance = TargetDistance;
        distanceConstraint.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<CenterDistanceConstraint>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, in distanceConstraint);
    }
}