// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem
{
    [Node("Flow Control/FlipFlop")]
    public class FlipFlopNode : InOutFlowNode
    {
        public override string Title => "Flip Flop";
        public override float Width => 100;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode True;
        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode False;

        [Output, SerializeIgnore]
        public bool flipped;

        public override void Execute(NodePort port)
        {
            ExecuteNext(flipped ? "True" : "False");
            flipped = !flipped;
            ExecuteNext();
        }

        public override object GetValue(NodePort port) => flipped;
    }
}
