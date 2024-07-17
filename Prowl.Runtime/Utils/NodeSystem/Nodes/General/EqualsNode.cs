using System.Linq;

namespace Prowl.Runtime.NodeSystem
{
    [Node("General/Equals")]
    public class EqualsNode : Node
    {
        public override bool ShowTitle => false;
        public override string Title => "Equals";
        public override float Width => 75;

        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.AssignableTo), SerializeIgnore] public object A;

        [Output, SerializeIgnore] public new bool Equals;

        public override void OnValidate()
        {
            var aPort = GetPort("A");
            if (!aPort.IsConnected || aPort.Connection.ValueType == null)
            {
                ClearDynamicPorts();
                return;
            }

            if (DynamicInputs.Count() > 0 && aPort.Connection.ValueType != DynamicInputs.ElementAt(0).ValueType)
                ClearDynamicPorts();

            if(DynamicInputs.Count() <= 0)
                AddDynamicInput(aPort.Connection.ValueType, ConnectionType.Override, TypeConstraint.AssignableTo, "B");
        }

        public override object GetValue(NodePort port)
        {
            return object.Equals(GetInputValue("A", A), GetInputValue<object>("B"));
        }
    }
}
