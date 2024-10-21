// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Set Euler Rotation")]
public class SetEulerRotationNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Set Euler Rotation";
    public override float Width => 100;

    [Input] public bool InLocalSpace;
    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Input] public Vector3 Rotation;

    public override void Execute(NodePort input)
    {
        bool local = GetInputValue("InLocalSpace", InLocalSpace);
        GameObject t = GetInputValue("Target", Target);
        Vector3 r = GetInputValue("Rotation", Rotation);

        if (t != null)
        {
            if (local)
                t.Transform.localEulerAngles = r;
            else
                t.Transform.eulerAngles = r;
        }

        ExecuteNext();
    }
}
