// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Lerp")]
public class Math_LerpNode : Node
{
    public override string Title => "Lerp";
    public override float Width => 100;

    [Input] public double A;
    [Input] public double B;
    [Input] public double T;
    [Output, SerializeIgnore] public double Result;

    public override object GetValue(NodePort input)
    {
        double a = GetInputValue(nameof(A), A);
        double b = GetInputValue(nameof(B), B);
        double t = GetInputValue(nameof(T), T);

        return MathD.Lerp(a, b, t);
    }
}
