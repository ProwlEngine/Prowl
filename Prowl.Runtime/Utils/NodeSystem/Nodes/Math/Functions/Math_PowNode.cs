// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Pow")]
public class Math_PowNode : Math_DoubleInOutNode
{
    public override string Title => "Pow";
    public override double Execute() => MathD.Pow(GetInputA(), GetInputB());
}
