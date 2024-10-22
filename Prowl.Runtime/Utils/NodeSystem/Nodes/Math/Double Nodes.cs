// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Double/Add")]
public class DoubleAddNode : Node
{
    public override string Title => "A + B";
    public override float Width => 75;

    [Input] public double A;
    [Input] public double B;

    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) + GetInputValue("B", B);
}

[Node("Math/Double/Subtract")]
public class DoubleSubtractNode : Node
{
    public override string Title => "A - B";
    public override float Width => 75;

    [Input] public double A;
    [Input] public double B;

    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) - GetInputValue("B", B);
}

[Node("Math/Double/Multiply")]
public class DoubleMultiplyNode : Node
{
    public override string Title => "A * B";
    public override float Width => 75;

    [Input] public double A;
    [Input] public double B;

    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) * GetInputValue("B", B);
}

[Node("Math/Double/Divide")]
public class DoubleDivideNode : Node
{
    public override string Title => "A / B";
    public override float Width => 75;

    [Input] public double A;
    [Input] public double B;

    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) / GetInputValue("B", B);
}

[Node("Math/Double/Modulo")]
public class DoubleModuloNode : Node
{
    public override string Title => "A % B";
    public override float Width => 75;

    [Input] public double A;
    [Input] public double B;

    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) % GetInputValue("B", B);
}

[Node("Math/Double/Compare")]
public class DoubleCompareNode : InFlowNode
{
    public override string Title => "Compare Doubles";
    public override float Width => 75;

    [Input] public double A;
    [Input] public double B;

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

        if (MathD.ApproximatelyEquals(a, b))
            ExecuteNext("OnEquals");
        else if (MathD.ApproximatelyEquals(a , b))
            ExecuteNext("OnNotEquals");
        else if (a > b)
            ExecuteNext("OnGreaterThan");
        else if (a < b)
            ExecuteNext("OnLesserThan");
    }
}
