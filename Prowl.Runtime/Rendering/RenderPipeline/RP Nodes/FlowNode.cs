using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    public abstract class FlowNode : Node
    {
        public void ExecuteNext(string port = "To")
        {
            var next = GetOutputPort("To").ConnectedNode as FlowNode;

            if (next == null) return; // No connected node this is the end of the flow

            try
            {
                next.Execute();
            }
            catch (System.Exception ex)
            {
                next.Error = ex.Message;
            }
        }

        public abstract void Execute();
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
