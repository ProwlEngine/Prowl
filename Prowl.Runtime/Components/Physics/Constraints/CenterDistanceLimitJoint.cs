using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} CenterDistanceLimitJoint")]
public class CenterDistanceLimitJoint : Joint
{
    public float MinimumDistance = 0;
    public float MaximumDistance = 5;
    
    public float Frequency = 35;
    public float DampingRatio = 5;
    private SpringSettings springSettings = new SpringSettings();
    private CenterDistanceLimit distanceConstraint = new CenterDistanceLimit();
    
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
        distanceConstraint.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<CenterDistanceLimit>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, in distanceConstraint);
    }
}