using Prowl.Icons;
using BepuPhysics.Constraints;
using BepuPhysics;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} BallSocketJoint")]
[RequireComponent(typeof(Rigidbody))]
public class BallSocketJoint : MonoBehaviour
{
    [SerializeField] public Rigidbody? connectedBody;
    
    private Rigidbody? rigidbody;
    
    private ConstraintHandle constraintHandle;
    
    public override void OnEnable()
    {
        if (rigidbody!.BodyHandle == null) return;
        
        constraintHandle = Physics.Sim!.Solver.Add<BallSocket>(rigidbody.BodyHandle.Value, connectedBody!.BodyHandle!.Value, new BallSocket());
    }
    
    public override void OnDisable()
    {
        Physics.Sim!.Solver.Remove(constraintHandle);
    }
}