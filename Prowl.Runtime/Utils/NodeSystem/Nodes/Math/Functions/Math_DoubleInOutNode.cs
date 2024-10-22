// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

public abstract class Math_DoubleInOutNode : Node
{
    public override bool ShowTitle => false;
    public override float Width => 50;

    [Input] public double ValueA;
    [Input] public double ValueB;
    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort input) => Execute();
    public virtual double Execute() => GetInputA() + GetInputB();

    protected double GetInputA() => GetInputValue(nameof(ValueA), ValueA);
    protected double GetInputB() => GetInputValue(nameof(ValueB), ValueB);
}
