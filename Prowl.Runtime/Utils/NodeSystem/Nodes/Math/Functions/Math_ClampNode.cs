// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Clamp")]
public class Math_ClampNode : Node
{
    public override string Title => "Clamp";
    public override float Width => 100;

    [Input] public double Value;
    [Input] public double Min;
    [Input] public double Max;
    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort input)
    {
        double value = GetInputValue(nameof(Value), Value);
        double min = GetInputValue(nameof(Min), Min);
        double max = GetInputValue(nameof(Max), Max);

        return MathD.Clamp(value, min, max);
    }
}
