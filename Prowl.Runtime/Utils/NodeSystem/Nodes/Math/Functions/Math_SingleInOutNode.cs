// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

public abstract class Math_SingleInOutNode : Node
{
    public override bool ShowTitle => false;
    public override float Width => 50;

    [Input] public double Value;
    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort input) => Execute();
    public virtual double Execute() => GetInput();
    protected double GetInput() => GetInputValue(nameof(Value), Value);
}
