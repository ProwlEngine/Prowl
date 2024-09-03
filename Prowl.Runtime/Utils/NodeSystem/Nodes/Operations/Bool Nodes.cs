using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.NodeSystem
{
    [Node("Operations/Boolean/AND")]
    public class BoolANDNode : Node
    {
        public override string Title => "A && B";
        public override float Width => 75;

        [Input] public bool A;
        [Input] public bool B;

        [Output, SerializeIgnore] public bool And;

        public override object GetValue(NodePort port) => GetInputValue("A", A) && GetInputValue("B", B);
    }

    [Node("Operations/Boolean/OR")]
    public class BoolORNode : Node
    {
        public override string Title => "A || B";
        public override float Width => 75;

        [Input] public bool A;
        [Input] public bool B;

        [Output, SerializeIgnore] public bool Or;

        public override object GetValue(NodePort port) => GetInputValue("A", A) || GetInputValue("B", B);
    }

    [Node("Operations/Boolean/Invert")]
    public class BoolInvertNode : Node
    {
        public override string Title => "!A";
        public override float Width => 75;

        [Input] public bool A;

        [Output, SerializeIgnore] public bool Inverted;

        public override object GetValue(NodePort port) => !GetInputValue("A", A);
    }
}
