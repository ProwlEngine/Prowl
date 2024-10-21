// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Set Layer")]
public class SetLayerNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Set Layer";
    public override float Width => 100;

    [Input] public GameObject Target;
    [Input] public string Layer;

    public override void Execute(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        string layer = GetInputValue("Layer", Layer);

        if (t != null)
        {
            t.layer = layer;
        }

        ExecuteNext();
    }
}
