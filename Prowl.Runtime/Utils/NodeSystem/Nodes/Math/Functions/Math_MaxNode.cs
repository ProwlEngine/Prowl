// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Max")]
public class Math_MaxNode : Math_DoubleInOutNode
{
    public override string Title => "Max";
    public override double Execute() => MathD.Max(GetInputA(), GetInputB());
}
