// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

namespace Prowl.Runtime.NodeSystem
{
    [Node("Flow Control/Random")]
    public class RandomNode : InFlowNode
    {
        public override string Title => "Random";
        public override float Width => 100;

        public override void OnValidate()
        {
            // remove any empty inputs
            foreach (var input in DynamicOutputs.ToArray())
                if (!input.IsConnected)
                    RemoveDynamicPort(input);

            if (DynamicOutputs.Count() == 0)
                AddDynamicOutput(typeof(FlowNode), ConnectionType.Override, TypeConstraint.Strict, "Index 0");

            // if all inputs are connected, add another one
            if (DynamicOutputs.All(p => p.IsConnected))
            {
                string fieldName = $"Index ";
                // find the next highest available index - Must be the highest index
                int maxIndex = DynamicOutputs.Max(p => int.Parse(p.fieldName.Substring(fieldName.Length)));
                AddDynamicOutput(typeof(FlowNode), ConnectionType.Override, TypeConstraint.Strict, $"{fieldName}{maxIndex + 1}");
            }
        }

        public override void Execute(NodePort input)
        {
            // find random connected port
            var connectedPorts = DynamicOutputs.Where(p => p.IsConnected).ToArray();
            if (connectedPorts.Length == 0)
                return;
            int index = System.Random.Shared.Next(0, connectedPorts.Length);
            ExecuteNext(connectedPorts[index].fieldName);
        }
    }
}
