// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Transform/Rotate")]
public class GORotateNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Rotate";
    public override float Width => 100;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Input] public Vector3 EulerAngles;
    [Input] public bool RelativeToSelf = true;

    public override void Execute(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        Vector3 eulerAngles = GetInputValue("EulerAngles", EulerAngles);
        bool relativeToSelf = GetInputValue("RelativeToSelf", RelativeToSelf);

        if (t != null)
            t.Transform.Rotate(eulerAngles, relativeToSelf);

        ExecuteNext();
    }
}
