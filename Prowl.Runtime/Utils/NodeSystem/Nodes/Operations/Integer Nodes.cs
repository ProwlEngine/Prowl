// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem
{
    [Node("Operations/Integer/Add")]
    public class IntegerAddNode : Node
    {
        public override string Title => "A + B";
        public override float Width => 75;

        [Input] public int A;
        [Input] public int B;

        [Output, SerializeIgnore] public int Result;

        public override object GetValue(NodePort port) => GetInputValue("A", A) + GetInputValue("B", B);
    }

    [Node("Operations/Int/Subtract")]
    public class IntSubtractNode : Node
    {
        public override string Title => "A - B";
        public override float Width => 75;

        [Input] public int A;
        [Input] public int B;

        [Output, SerializeIgnore] public int Result;

        public override object GetValue(NodePort port) => GetInputValue("A", A) - GetInputValue("B", B);
    }

    [Node("Operations/Int/Multiply")]
    public class IntMultiplyNode : Node
    {
        public override string Title => "A * B";
        public override float Width => 75;

        [Input] public int A;
        [Input] public int B;

        [Output, SerializeIgnore] public int Result;

        public override object GetValue(NodePort port) => GetInputValue("A", A) * GetInputValue("B", B);
    }

    [Node("Operations/Int/Divide")]
    public class IntDivideNode : Node
    {
        public override string Title => "A / B";
        public override float Width => 75;

        [Input] public int A;
        [Input] public int B;

        [Output, SerializeIgnore] public int Result;

        public override object GetValue(NodePort port) => GetInputValue("A", A) / GetInputValue("B", B);
    }

    [Node("Operations/Int/Modulo")]
    public class IntModuloNode : Node
    {
        public override string Title => "A % B";
        public override float Width => 75;

        [Input] public int A;
        [Input] public int B;

        [Output, SerializeIgnore] public int Result;

        public override object GetValue(NodePort port) => GetInputValue("A", A) % GetInputValue("B", B);
    }

    [Node("Operations/Int/Compare")]
    public class IntCompareNode : InFlowNode
    {
        public override string Title => "Compare Ints";
        public override float Width => 75;

        [Input] public int A;
        [Input] public int B;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode OnEquals;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode OnNotEquals;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode OnGreaterThan;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode OnLesserThan;

        public override void Execute(NodePort input)
        {
            var a = GetInputValue("A", A);
            var b = GetInputValue("B", B);

            if (a == b)
                ExecuteNext("OnEquals");
            else if (a != b)
                ExecuteNext("OnNotEquals");
            else if (a > b)
                ExecuteNext("OnGreaterThan");
            else if (a < b)
                ExecuteNext("OnLesserThan");
        }
    }
}
