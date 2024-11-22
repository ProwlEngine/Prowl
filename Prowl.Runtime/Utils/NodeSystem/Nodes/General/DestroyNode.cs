// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("General/Destroy")]
public class DestroyNode : InOutFlowNode
{
    public override string Title => "Destroy";
    public override float Width => 100;

    [Input] public EngineObject Target;

    public override void Execute(NodePort input)
    {
        var target = GetInputValue("Target", Target);

        if (target != null)
            target.DestroyLater();

        ExecuteNext();
    }
}
