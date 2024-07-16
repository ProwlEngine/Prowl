namespace Prowl.Runtime.NodeSystem
{
    [Node("Flow Control/Do Once")]
    public class DoOnceNode : InOutFlowNode
    {
        public override string Title => "Do Once";
        public override float Width => 160;

        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode Reset;

        [Input] public bool StartClosed;

        private bool completed;

        public override void Execute(NodePort input)
        {
            if(input.fieldName == nameof(Reset))
                completed = false;

            if (!completed)
            {
                completed = true;
                ExecuteNext();
            }
        }
    }
}
