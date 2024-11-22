// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/LerpAngle")]
public class Math_LerpAngleNode : Node
{
    public override string Title => "Lerp Angle";
    public override float Width => 100;

    [Input] public double ARad;
    [Input] public double BRad;
    [Input] public double T;
    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort input)
    {
        double a = GetInputValue(nameof(ARad), ARad);
        double b = GetInputValue(nameof(BRad), BRad);
        double t = GetInputValue(nameof(T), T);

        return MathD.LerpAngle(a, b, t);
    }
}
