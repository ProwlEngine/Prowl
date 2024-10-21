// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Set Tag")]
public class SetTagNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Set Tag";
    public override float Width => 100;

    [Input] public GameObject Target;
    [Input] public string Tag;

    public override void Execute(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        string tag = GetInputValue("Tag", Tag);

        if (t != null)
        {
            t.tag = tag;
        }

        ExecuteNext();
    }
}
