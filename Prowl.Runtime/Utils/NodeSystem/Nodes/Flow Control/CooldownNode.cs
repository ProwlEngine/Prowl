namespace Prowl.Runtime.NodeSystem
{
    [Node("Flow Control/Cooldown")]
    public class CooldownNode : InOutFlowNode
    {
        public override string Title => "Cooldown";
        public override float Width => 160;

        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode Reset;

        [Input] public double Duration;

        private double lastExecute;

        public override void Execute(NodePort input)
        {
            if(input.fieldName == nameof(Reset))
                lastExecute = 0;
            else if (Time.time > lastExecute + Duration)
            {
                lastExecute = Time.time + Duration;
                ExecuteNext();
            }
        }
    }
}
