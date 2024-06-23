using System;
using BepuPhysics;
using BepuPhysics.Constraints;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Joint} LinearAxisLimitJoint")]
public class LinearAxisLimitJoint : Joint
{
    public Vector3 JointPosition = Vector3.forward;
    public Vector3 LocalAxis = Vector3.forward;
    public float MinimumOffset = 1;
    public float MaximumOffset = 5;
    
    public float Frequency = 35;
    public float DampingRatio = 5;
    private SpringSettings springSettings = new SpringSettings();
    private LinearAxisLimit linearAxisLimit = new LinearAxisLimit();
    
    public override void Update()
    {
        base.Update();
        
        springSettings.Frequency = Frequency;
        springSettings.DampingRatio = DampingRatio;
    }
    
    protected override ConstraintHandle Build()
    {
        linearAxisLimit.LocalOffsetA = JointPosition;
        linearAxisLimit.LocalOffsetB = Vector3.zero; //ConnectedBody!.Transform.InverseTransformPoint(this.Transform.TransformPoint(JointPosition));
        linearAxisLimit.LocalAxis = LocalAxis;
        linearAxisLimit.MaximumOffset = MaximumOffset;
        linearAxisLimit.MinimumOffset = MinimumOffset;
        linearAxisLimit.SpringSettings = springSettings;
        
        return Physics.Sim!.Solver.Add<LinearAxisLimit>(Rigidbody!.BodyHandle.Value, ConnectedBody!.BodyHandle.Value, in linearAxisLimit);
    }

    public override void DrawGizmosSelected()
    {
        base.DrawGizmosSelected();
        var localAxisNormalized = LocalAxis.normalized;
        Gizmos.DrawSphere(this.Transform.TransformPoint(JointPosition), 0.05f);
        var minPos = this.Transform.TransformPoint(JointPosition * (localAxisNormalized * MinimumOffset));
        var maxPos = this.Transform.TransformPoint(JointPosition * (localAxisNormalized * MaximumOffset));
        Gizmos.DrawSphere(minPos, 0.05f);
        Gizmos.DrawSphere(maxPos, 0.05f);
        Gizmos.DrawLine(minPos, maxPos);
    }
}