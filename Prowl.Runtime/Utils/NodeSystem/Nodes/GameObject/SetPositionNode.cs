// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Set Position")]
public class SetPositionNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Set Position";
    public override float Width => 100;

    [Input] public bool InLocalSpace;
    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Input] public Vector3 Position;

    public override void Execute(NodePort input)
    {
        bool local = GetInputValue("InLocalSpace", InLocalSpace);
        GameObject t = GetInputValue("Target", Target);
        Vector3 p = GetInputValue("Position", Position);

        if (t != null)
        {
            if (local)
                t.Transform.localPosition = p;
            else
                t.Transform.position = p;
        }

        ExecuteNext();
    }
}
