// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Set Name")]
public class SetNameNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Set Name";
    public override float Width => 100;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Input] public string Name;

    public override void Execute(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        string name = GetInputValue("Name", Name);

        if (t != null)
        {
            t.Name = name;
        }

        ExecuteNext();
    }
}
