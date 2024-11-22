// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Transform/Rotate Around")]
public class GORotateAroundNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Rotate Around";
    public override float Width => 100;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Input] public Vector3 Point;
    [Input] public Vector3 Axis;
    [Input] public double Angle;

    public override void Execute(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        Vector3 point = GetInputValue("Point", Point);
        Vector3 axis = GetInputValue("Axis", Axis);
        double angle = GetInputValue("Angle", Angle);

        if (t != null)
            t.Transform.RotateAround(point, axis, angle);

        ExecuteNext();
    }
}
