namespace Prowl.Runtime.NodeSystem
{
    [Node("Flow Control/TryCatch")]
    public class TryCatchNode : InOutFlowNode
    {
        public override string Title => "Try Catch";
        public override float Width => 140;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode Try;
        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode Catch;

        public override void Execute(NodePort port)
        {
            try
            {
                ExecuteNext("Try");
            }
            catch
            {
                ExecuteNext("Catch");
            }
            finally
            {
                ExecuteNext();
            }
        }
    }
}
