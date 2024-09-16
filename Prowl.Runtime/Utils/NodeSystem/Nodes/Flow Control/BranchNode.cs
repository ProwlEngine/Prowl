// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem
{
    [Node("Flow Control/Branch")]
    public class BranchNode : InOutFlowNode
    {
        public override string Title => "Branch";
        public override float Width => 140;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode True;
        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode False;

        [Input] public bool Condition;

        public override void Execute(NodePort port)
        {
            var condition = GetInputValue<bool>("Condition");
            ExecuteNext(condition ? "True" : "False");

            ExecuteNext();
        }
    }
}
