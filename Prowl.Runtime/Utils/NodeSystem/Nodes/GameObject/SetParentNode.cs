// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Set Parent")]
public class SetParentNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Set Parent";
    public override float Width => 100;

    [Input] public GameObject Target;
    [Input] public GameObject Parent;
    [Input] public bool KeepWorldPosition;

    public override void Execute(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        GameObject p = GetInputValue("Parent", Parent);
        bool worldPos = GetInputValue("KeepWorldPosition", KeepWorldPosition);

        if (t != null)
        {
            t.SetParent(p, worldPos);
        }

        ExecuteNext();
    }
}
