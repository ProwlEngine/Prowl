using Prowl.Icons;
using BepuPhysics.Constraints;
using BepuPhysics;
using System.Numerics;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} BallSocketJoint")]
[RequireComponent(typeof(Rigidbody))]
public class BallSocketJoint : MonoBehaviour
{
    [SerializeField] public Rigidbody? connectedBody;
    public Vector3 jointPosition;
    public Vector3 connectedBodyJointPosition;
    public float angularFrequency;
    public float twiceDampingRatio;
    public float frequency;
    public float dampingRatio;
    
    private Rigidbody? rigidbody;
    
    private ConstraintHandle constraintHandle;
    
    public override void OnEnable()
    {
        if (rigidbody!.BodyHandle == null) return;

        var springSettings = new SpringSettings();
        springSettings.AngularFrequency = angularFrequency;
        springSettings.TwiceDampingRatio = twiceDampingRatio;
        springSettings.Frequency = frequency;
        springSettings.DampingRatio = dampingRatio;

        var joint = new BallSocket();
        joint.LocalOffsetA = jointPosition;
        joint.LocalOffsetB = connectedBodyJointPosition;
        joint.SpringSettings = springSettings;
        
        constraintHandle = Physics.Sim!.Solver.Add<BallSocket>(rigidbody.BodyHandle.Value, connectedBody!.BodyHandle!.Value, joint);
    }
    
    public override void OnDisable()
    {
        Physics.Sim!.Solver.Remove(constraintHandle);
    }
}