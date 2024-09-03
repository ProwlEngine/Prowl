// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem
{
    public abstract class FlowNode : Node
    {
        public void ExecuteNext(string portName = "To")
        {
            var port = GetOutputPort(portName);
            var next = port.ConnectedNode as FlowNode;

            if (next == null) return; // No connected node this is the end of the flow

            try
            {
                next.Execute(port.GetConnection(0));
            }
            catch (System.Exception ex)
            {
                next.Error = ex.Message;
            }
        }

        public abstract void Execute(NodePort input);
    }

    public abstract class InFlowNode : FlowNode
    {
        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.Strict, true), SerializeIgnore]
        public FlowNode From;
    }

    public abstract class InOutFlowNode : FlowNode
    {
        [Output(ConnectionType.Override, TypeConstraint.Strict, true), SerializeIgnore]
        public FlowNode To;
        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.Strict, true), SerializeIgnore]
        public FlowNode From;
    }

    public abstract class OutFlowNode : FlowNode
    {
        [Output(ConnectionType.Override, TypeConstraint.Strict, true), SerializeIgnore]
        public FlowNode To;
    }
}
