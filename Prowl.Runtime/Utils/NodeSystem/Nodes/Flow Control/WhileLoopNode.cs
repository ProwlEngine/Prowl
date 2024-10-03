// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Flow Control/While Loop")]
public class WhileLoopNode : InOutFlowNode
{
    public override string Title => "While Loop";
    public override float Width => 170;

    [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
    public FlowNode Completed;

    [Input] public bool Condition;

    public override void Execute(NodePort port)
    {
        int i = 0;
        while (GetInputValue("Condition", Condition))
        {
            ExecuteNext();
            i++;

            if (i > 1000)
            {
                Error = "Max Loop Count (1000) hit.";
                Debug.LogWarning("A While Loop node has reached its Loop Limit: " + Error);
                break;
            }
        }

        ExecuteNext(nameof(Completed));
    }
}
