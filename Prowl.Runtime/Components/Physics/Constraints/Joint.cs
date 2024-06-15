using System.Net;
using BepuPhysics;
using BepuPhysics.Constraints;

namespace Prowl.Runtime;

[RequireComponent(typeof(Rigidbody))]
public abstract class Joint : MonoBehaviour
{
    public Rigidbody? ConnectedBody;
    public float Frequency = 35;
    public float DampingRatio = 5;
    
    protected ConstraintHandle? ConstraintHandle;
    protected Rigidbody? Rigidbody;
    protected bool HasInitialized = false;

    public override void Update()
    {
        if (!HasInitialized)
        {
            Rigidbody ??= GetComponent<Rigidbody>();

            if (Rigidbody!.BodyHandle == null)
            {
                Debug.LogError("Rigidbody BodyHandle is null");
                return;
            }
            
            if (ConnectedBody!.BodyHandle == null)
            {
                Debug.LogError("ConnectedBody BodyHandle is null");
                return;
            }
            
            var springSettings = new SpringSettings();
            springSettings.Frequency = Frequency;
            springSettings.DampingRatio = DampingRatio;

            ConstraintHandle = Build(springSettings);
            HasInitialized = true;
        }
    }

    protected abstract ConstraintHandle Build(SpringSettings springSettings);
    
    public override void OnDisable()
    {
        if (ConstraintHandle != null) Physics.Sim!.Solver.Remove(ConstraintHandle.Value);
    }
}