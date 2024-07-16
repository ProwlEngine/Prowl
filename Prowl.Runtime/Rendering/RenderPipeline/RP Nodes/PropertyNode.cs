using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/Property")]
    public class PropertyNode : Node
    {
        public class NodeProperty
        {
            public string Name;
            public object Value;
        }

        public override string Title => "Property";
        public override float Width => 150;

        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.AssignableTo), SerializeIgnore] public object Value;

        [Output, SerializeIgnore] public NodeProperty Property;

        public string Name;

        public override object GetValue(NodePort port)
        {
            var val = GetInputValue<object>("Value");
            if (val == null) throw new System.Exception("[PropertyNode] Value is null");
            
            return new NodeProperty { Name = Name, Value = val };
        }
    }
}
