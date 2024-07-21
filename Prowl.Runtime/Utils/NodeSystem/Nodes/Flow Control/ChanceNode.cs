namespace Prowl.Runtime.NodeSystem
{
    [Node("Flow Control/Chance")]
    public class ChanceNode : InOutFlowNode
    {
        public override string Title => "Chance";
        public override float Width => 160;

        [Input] public double Min = 0;
        [Input] public double Max = 1;
        [Input] public double Chance = 0.5;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode Success;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode Failure;

        public override void Execute(NodePort input)
        {
            double chance = GetInputValue<double>("Chance");
            double min = GetInputValue<double>("Min");
            double max = GetInputValue<double>("Max");

            if (Random.Range(min, max) <= chance)
                ExecuteNext("Success");
            else
                ExecuteNext("Failure");

            ExecuteNext();
        }
    }
}
