using System;
using System.Net;
using BepuPhysics;
using BepuPhysics.Constraints;

namespace Prowl.Runtime;

[RequireComponent(typeof(Rigidbody))]
public abstract class Joint : MonoBehaviour
{
    public Rigidbody? ConnectedBody;

    protected ConstraintHandle? ConstraintHandle;
    protected bool HasInitialized = false;
    protected Rigidbody? Rigidbody => _rigidbody ??= GetComponent<Rigidbody>();
    private Rigidbody _rigidbody;

    public override void Update()
    {
        if (!HasInitialized)
        {
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
            
            ConstraintHandle = Build();
            HasInitialized = true;
        }
    }

    protected abstract ConstraintHandle Build();
    
    public override void OnDisable()
    {
        if (ConstraintHandle != null) Physics.Sim!.Solver.Remove(ConstraintHandle.Value);
    }

    public override void DrawGizmosSelected()
    {
        base.DrawGizmosSelected();
        //if (Rigidbody != null && ConnectedBody != null) 
        //    Gizmos.DrawLine(Rigidbody!.Transform.position, ConnectedBody.Transform.position);
    }
}