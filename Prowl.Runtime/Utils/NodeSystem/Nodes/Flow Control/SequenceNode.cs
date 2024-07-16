using System;
using System.Linq;

namespace Prowl.Runtime.NodeSystem
{
    [Node("Flow Control/Sequence")]
    public class SequenceNode : InOutFlowNode
    {
        public override string Title => "Sequence";
        public override float Width => 100;

        public override void OnValidate()
        {
            // remove any empty inputs
            foreach (var input in DynamicOutputs.ToArray())
                if (!input.IsConnected)
                    RemoveDynamicPort(input);

            if (DynamicOutputs.Count() == 0)
                AddDynamicOutput(typeof(FlowNode), ConnectionType.Override, TypeConstraint.Strict, "Then 0");

            // if all inputs are connected, add another one
            if (DynamicOutputs.All(p => p.IsConnected))
            {
                string fieldName = $"Then ";
                // find the next highest available index - Must be the highest index
                int maxIndex = DynamicOutputs.Max(p => int.Parse(p.fieldName.Substring(fieldName.Length)));
                AddDynamicOutput(typeof(FlowNode), ConnectionType.Override, TypeConstraint.Strict, $"{fieldName}{maxIndex + 1}");
            }
        }

        public override void Execute(NodePort input)
        {
            foreach (var port in DynamicOutputs)
            {
                if (port.IsConnected)
                {
                    ExecuteNext(port.fieldName);
                    return;
                }
            }
        }
    }
}
